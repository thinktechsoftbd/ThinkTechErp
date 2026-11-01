-- Inventory and Purchase modules: product masters, warehouses, the stock ledger and the purchase
-- documents for any shop or trading business (agri inputs, departmental store, hardware,
-- pharmacy, ...), in Bangladesh or elsewhere.
--
-- Purchases usually start at the supplier invoice: a direct purchase invoice (cash or credit)
-- receives the stock and records what is owed in one step. Requisition, purchase order and goods
-- receipt are optional steps before it, for businesses that order ahead or receive before billing.
--
-- Conventions (as in 006):
--   * Every master and document belongs to one security.organization (the tenant). Codes and
--     document numbers are unique per organization; the API always filters by organization_id.
--   * Timestamps are UTC (now() AT TIME ZONE 'utc'); business dates (order date, expiry) are DATE.
--   * Quantities and money are NUMERIC(18, 4), so fractional units (2.5 kg, 7.25 ft) and
--     3-decimal currencies (KWD, OMR) fit; unit costs carry 6 decimals. Round for display only.
--   * Stock is always kept in the product's base unit. Document lines keep the unit the user chose
--     and the conversion factor used, so old documents never change when a pack size does.
--   * Amounts on a document are in its currency; exchange_rate converts them to the base currency.
--     Stock costs (stock_balance, stock_movement) are always in the base currency.
--   * Posted documents are never edited or deleted. Cancelling one posts reversing movements.
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE SCHEMA IF NOT EXISTS inventory;
CREATE SCHEMA IF NOT EXISTS purchase;

-- =====================================================================
-- Shared: taxes and document numbering (public, like the lookup lists; Sales will use them too)
-- =====================================================================

-- One tax at one rate: VAT 15%, VAT 5%, SD 20% (Bangladesh), GST 18%, a US state sales tax, ...
CREATE TABLE IF NOT EXISTS public.tax_rate
(
    tax_rate_id      INT GENERATED ALWAYS AS IDENTITY,
    organization_id  INT NOT NULL,
    tax_code         VARCHAR(30) NOT NULL,
    tax_name         VARCHAR(150) NOT NULL,
    tax_type         VARCHAR(20) NOT NULL,
    rate_percent     NUMERIC(9, 4) NOT NULL,
    -- Recoverable input tax (claimed back, e.g. VAT for a VAT-registered business) stays out of the
    -- item cost; non-recoverable tax (e.g. a small shop on turnover tax) is added to the cost.
    is_recoverable   BOOLEAN NOT NULL DEFAULT TRUE,
    is_active        BOOLEAN NOT NULL DEFAULT TRUE,

    created_at       TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by       INT NULL,
    updated_at       TIMESTAMP(3) NULL,
    updated_by       INT NULL,

    CONSTRAINT pk_tax_rate PRIMARY KEY (tax_rate_id),
    CONSTRAINT fk_tax_rate_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT ck_tax_rate_type
        CHECK (tax_type IN ('VAT', 'GST', 'SalesTax', 'SD', 'Excise', 'Customs', 'Other')),
    CONSTRAINT ck_tax_rate_percent CHECK (rate_percent >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tax_rate_code
    ON public.tax_rate (organization_id, lower(tax_code));

-- What a document line applies: "VAT 15%", "SD 20% + VAT 15%", "CGST 9% + SGST 9%", or "Exempt"
-- (a group with no rates).
CREATE TABLE IF NOT EXISTS public.tax_group
(
    tax_group_id     INT GENERATED ALWAYS AS IDENTITY,
    organization_id  INT NOT NULL,
    tax_group_name   VARCHAR(150) NOT NULL,
    is_active        BOOLEAN NOT NULL DEFAULT TRUE,

    created_at       TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by       INT NULL,
    updated_at       TIMESTAMP(3) NULL,
    updated_by       INT NULL,

    CONSTRAINT pk_tax_group PRIMARY KEY (tax_group_id),
    CONSTRAINT fk_tax_group_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tax_group_name
    ON public.tax_group (organization_id, lower(tax_group_name));

CREATE TABLE IF NOT EXISTS public.tax_group_rate
(
    tax_group_id  INT NOT NULL,
    tax_rate_id   INT NOT NULL,
    sequence_no   SMALLINT NOT NULL DEFAULT 1,
    -- Charged on the amount plus the taxes before it (Bangladesh: VAT on the SD-inclusive value).
    is_compound   BOOLEAN NOT NULL DEFAULT FALSE,

    CONSTRAINT pk_tax_group_rate PRIMARY KEY (tax_group_id, tax_rate_id),
    CONSTRAINT fk_tax_group_rate_tax_group
        FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id) ON DELETE CASCADE,
    CONSTRAINT fk_tax_group_rate_tax_rate
        FOREIGN KEY (tax_rate_id) REFERENCES public.tax_rate (tax_rate_id)
);

-- Document numbers such as PO-2026-00001. The API takes the next number with
--   UPDATE public.document_sequence SET next_number = next_number + 1
--   WHERE organization_id = @Org AND document_type = @Type AND fiscal_year = @Year
--   RETURNING next_number - 1
-- inside the saving transaction, which also serializes concurrent saves of the same type.
CREATE TABLE IF NOT EXISTS public.document_sequence
(
    document_sequence_id INT GENERATED ALWAYS AS IDENTITY,
    organization_id      INT NOT NULL,
    -- PurchaseRequisition, PurchaseOrder, GoodsReceipt, PurchaseInvoice, PurchaseReturn,
    -- SupplierPayment, StockAdjustment, StockTransfer, StockCount, SalesInvoice, SalesReturn,
    -- CustomerReceipt
    document_type        VARCHAR(30) NOT NULL,
    -- 0 = one series forever; otherwise the fiscal year the series belongs to (restarts yearly).
    fiscal_year          INT NOT NULL DEFAULT 0,
    prefix               VARCHAR(20) NOT NULL DEFAULT '',
    next_number          BIGINT NOT NULL DEFAULT 1,
    number_padding       SMALLINT NOT NULL DEFAULT 5,

    created_at           TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by           INT NULL,
    updated_at           TIMESTAMP(3) NULL,
    updated_by           INT NULL,

    CONSTRAINT pk_document_sequence PRIMARY KEY (document_sequence_id),
    CONSTRAINT fk_document_sequence_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT ux_document_sequence UNIQUE (organization_id, document_type, fiscal_year),
    CONSTRAINT ck_document_sequence_next CHECK (next_number > 0),
    CONSTRAINT ck_document_sequence_padding CHECK (number_padding BETWEEN 1 AND 12)
);

-- Suppliers are grouped with a lookup list, managed under Settings -> Lookup Data.
INSERT INTO public.lookup_table (lookup_table_name)
VALUES ('Supplier Group')
ON CONFLICT DO NOTHING;

INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name)
SELECT t.lookup_table_id, v.name
FROM public.lookup_table t
CROSS JOIN (VALUES ('Manufacturer'), ('Importer'), ('Distributor'), ('Wholesaler'),
                   ('Government / Dealer'), ('Local Supplier')) AS v(name)
WHERE lower(t.lookup_table_name) = 'supplier group'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Inventory masters
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.unit
(
    unit_id          INT GENERATED ALWAYS AS IDENTITY,
    organization_id  INT NOT NULL,
    unit_code        VARCHAR(20) NOT NULL,      -- pcs, kg, bag, ltr, box, dozen, ft, sqft
    unit_name        VARCHAR(100) NOT NULL,
    unit_name_local  VARCHAR(100) NULL,         -- e.g. Bangla: পিস, কেজি, বস্তা
    unit_type        VARCHAR(20) NOT NULL DEFAULT 'Count',
    -- UN/ECE Recommendation 20 code (H87 piece, KGM kilogram, MTR metre, LTR litre), for e-invoicing.
    iso_code         VARCHAR(10) NULL,
    -- kg and ft can be sold as 2.5; pieces can't.
    allow_decimal    BOOLEAN NOT NULL DEFAULT FALSE,
    is_active        BOOLEAN NOT NULL DEFAULT TRUE,

    created_at       TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by       INT NULL,
    updated_at       TIMESTAMP(3) NULL,
    updated_by       INT NULL,

    CONSTRAINT pk_unit PRIMARY KEY (unit_id),
    CONSTRAINT fk_unit_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT ck_unit_type
        CHECK (unit_type IN ('Count', 'Weight', 'Volume', 'Length', 'Area', 'Time', 'Other'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_unit_code
    ON inventory.unit (organization_id, lower(unit_code));

-- A tree: Fertilizer > Urea, Pesticide > Insecticide, Hardware > Pipes & Fittings.
CREATE TABLE IF NOT EXISTS inventory.category
(
    category_id         INT GENERATED ALWAYS AS IDENTITY,
    organization_id     INT NOT NULL,
    parent_category_id  INT NULL,
    category_code       VARCHAR(30) NULL,
    category_name       VARCHAR(150) NOT NULL,
    category_name_local VARCHAR(150) NULL,
    sort_order          INT NOT NULL DEFAULT 0,
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,

    created_at          TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by          INT NULL,
    updated_at          TIMESTAMP(3) NULL,
    updated_by          INT NULL,

    CONSTRAINT pk_category PRIMARY KEY (category_id),
    CONSTRAINT fk_category_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_category_parent
        FOREIGN KEY (parent_category_id) REFERENCES inventory.category (category_id),
    CONSTRAINT ck_category_parent CHECK (parent_category_id <> category_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_category_name
    ON inventory.category (organization_id, parent_category_id, lower(category_name)) NULLS NOT DISTINCT;

CREATE UNIQUE INDEX IF NOT EXISTS ux_category_code
    ON inventory.category (organization_id, lower(category_code)) WHERE category_code IS NOT NULL;

-- Brand or manufacturing company: ACI, Syngenta, BCIC, Unilever, RFL, ...
CREATE TABLE IF NOT EXISTS inventory.brand
(
    brand_id         INT GENERATED ALWAYS AS IDENTITY,
    organization_id  INT NOT NULL,
    brand_name       VARCHAR(150) NOT NULL,
    brand_name_local VARCHAR(150) NULL,
    is_active        BOOLEAN NOT NULL DEFAULT TRUE,

    created_at       TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by       INT NULL,
    updated_at       TIMESTAMP(3) NULL,
    updated_by       INT NULL,

    CONSTRAINT pk_brand PRIMARY KEY (brand_id),
    CONSTRAINT fk_brand_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_brand_name
    ON inventory.brand (organization_id, lower(brand_name));

-- A shop, outlet or godown that holds stock. A business with several shops has one per shop.
CREATE TABLE IF NOT EXISTS inventory.warehouse
(
    warehouse_id         INT GENERATED ALWAYS AS IDENTITY,
    organization_id      INT NOT NULL,
    warehouse_code       VARCHAR(30) NOT NULL,
    warehouse_name       VARCHAR(150) NOT NULL,
    warehouse_name_local VARCHAR(150) NULL,
    -- Store: sells from here (shop floor, outlet). Warehouse: storage only (godown).
    warehouse_type       VARCHAR(20) NOT NULL DEFAULT 'Store',
    address              VARCHAR(500) NULL,
    phone                VARCHAR(30) NULL,
    is_active            BOOLEAN NOT NULL DEFAULT TRUE,

    created_at           TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by           INT NULL,
    updated_at           TIMESTAMP(3) NULL,
    updated_by           INT NULL,

    CONSTRAINT pk_warehouse PRIMARY KEY (warehouse_id),
    CONSTRAINT fk_warehouse_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT ck_warehouse_type CHECK (warehouse_type IN ('Store', 'Warehouse'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_warehouse_code
    ON inventory.warehouse (organization_id, lower(warehouse_code));

-- One row per organization, created when the Inventory module is subscribed.
CREATE TABLE IF NOT EXISTS inventory.inventory_setting
(
    organization_id              INT NOT NULL,
    base_currency_lookup_data_id BIGINT NOT NULL,
    -- Bangladesh: 7 (July-June). Most of the world: 1; India, UK: 4.
    fiscal_year_start_month      SMALLINT NOT NULL DEFAULT 1,
    -- Prices typed on documents include tax (MRP-style retail) or exclude it (B2B, imports).
    prices_include_tax           BOOLEAN NOT NULL DEFAULT FALSE,
    allow_negative_stock         BOOLEAN NOT NULL DEFAULT FALSE,
    -- Purchase orders and stock adjustments wait for approval before they take effect.
    require_approval             BOOLEAN NOT NULL DEFAULT FALSE,
    -- Batches expiring within this many days are on the expiry alert.
    expiry_alert_days            INT NOT NULL DEFAULT 30,
    default_warehouse_id         INT NULL,

    created_at                   TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                   INT NULL,
    updated_at                   TIMESTAMP(3) NULL,
    updated_by                   INT NULL,

    CONSTRAINT pk_inventory_setting PRIMARY KEY (organization_id),
    CONSTRAINT fk_inventory_setting_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_inventory_setting_currency
        FOREIGN KEY (base_currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT fk_inventory_setting_warehouse
        FOREIGN KEY (default_warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT ck_inventory_setting_month CHECK (fiscal_year_start_month BETWEEN 1 AND 12),
    CONSTRAINT ck_inventory_setting_expiry CHECK (expiry_alert_days >= 0)
);

-- Anything bought or sold. Each variant (size, colour, pack) is a product of its own.
CREATE TABLE IF NOT EXISTS inventory.product
(
    product_id            INT GENERATED ALWAYS AS IDENTITY,
    object_id             uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id       INT NOT NULL,
    product_code          VARCHAR(50) NOT NULL,     -- SKU
    product_name          VARCHAR(250) NOT NULL,
    product_name_local    VARCHAR(250) NULL,
    -- Stock: counted in stock. NonStock: bought or sold but not counted (consumables, packing bags).
    -- Service: no quantity at all (delivery, installation).
    product_type          VARCHAR(20) NOT NULL DEFAULT 'Stock',
    -- Set on a variant: the product it is a variant of, and what differs ({"size": "L", "color": "Red"}).
    parent_product_id     INT NULL,
    variant_attributes    jsonb NULL,
    category_id           INT NULL,
    brand_id              INT NULL,
    -- Stock is kept in this unit; packs and conversions are in product_unit.
    base_unit_id          INT NOT NULL,
    -- None. Batch: lot number and expiry (fertilizer, pesticide, seed, food, medicine).
    -- Serial: one number per item (power tools, electronics).
    tracking_type         VARCHAR(10) NOT NULL DEFAULT 'None',
    has_expiry            BOOLEAN NOT NULL DEFAULT FALSE,
    shelf_life_days       INT NULL,
    -- Active ingredient or generic name: pesticide "Imidacloprid 70% WG", medicine "Paracetamol 500 mg".
    generic_name          VARCHAR(250) NULL,
    -- Government registration of the product: pesticide or fertilizer registration, drug licence.
    registration_no       VARCHAR(100) NULL,
    hs_code               VARCHAR(20) NULL,         -- customs tariff code, for imports
    country_of_origin     CHAR(2) NULL,             -- ISO 3166-1 alpha-2
    purchase_tax_group_id INT NULL,
    sale_tax_group_id     INT NULL,
    -- Per base unit, in the base currency. Pack prices are on product_unit.
    last_purchase_cost    NUMERIC(18, 6) NOT NULL DEFAULT 0,
    sale_price            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Maximum retail price printed on the pack (Bangladesh, India); NULL where there is none.
    mrp                   NUMERIC(18, 4) NULL,
    reorder_level         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    reorder_quantity      NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Fields only some businesses need: {"npk": "46-0-0"}, {"toxicity_class": "II"},
    -- {"pack_size": "500 ml"}, {"warranty_months": 12}. The category decides which ones the UI shows.
    attributes            jsonb NOT NULL DEFAULT '{}',
    image_url             VARCHAR(500) NULL,
    is_active             BOOLEAN NOT NULL DEFAULT TRUE,

    created_at            TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by            INT NULL,
    updated_at            TIMESTAMP(3) NULL,
    updated_by            INT NULL,

    CONSTRAINT pk_product PRIMARY KEY (product_id),
    CONSTRAINT fk_product_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_product_parent
        FOREIGN KEY (parent_product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_product_category
        FOREIGN KEY (category_id) REFERENCES inventory.category (category_id),
    CONSTRAINT fk_product_brand
        FOREIGN KEY (brand_id) REFERENCES inventory.brand (brand_id),
    CONSTRAINT fk_product_base_unit
        FOREIGN KEY (base_unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_product_purchase_tax_group
        FOREIGN KEY (purchase_tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT fk_product_sale_tax_group
        FOREIGN KEY (sale_tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ck_product_type CHECK (product_type IN ('Stock', 'NonStock', 'Service')),
    CONSTRAINT ck_product_tracking CHECK (tracking_type IN ('None', 'Batch', 'Serial')),
    CONSTRAINT ck_product_tracking_stock CHECK (tracking_type = 'None' OR product_type = 'Stock'),
    -- The expiry date lives on the batch.
    CONSTRAINT ck_product_expiry CHECK (NOT has_expiry OR tracking_type = 'Batch'),
    CONSTRAINT ck_product_parent CHECK (parent_product_id <> product_id),
    CONSTRAINT ck_product_prices
        CHECK (last_purchase_cost >= 0 AND sale_price >= 0 AND (mrp IS NULL OR mrp >= 0)),
    CONSTRAINT ck_product_reorder CHECK (reorder_level >= 0 AND reorder_quantity >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_product_object_id ON inventory.product (object_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_code
    ON inventory.product (organization_id, lower(product_code));
CREATE INDEX IF NOT EXISTS idx_product_name
    ON inventory.product (organization_id, lower(product_name));
CREATE INDEX IF NOT EXISTS idx_product_category ON inventory.product (category_id);
CREATE INDEX IF NOT EXISTS idx_product_parent
    ON inventory.product (parent_product_id) WHERE parent_product_id IS NOT NULL;

-- The units a product is bought or sold in. The base unit has a row too, with factor 1, so every
-- document line can point at one.
CREATE TABLE IF NOT EXISTS inventory.product_unit
(
    product_unit_id     INT GENERATED ALWAYS AS IDENTITY,
    product_id          INT NOT NULL,
    unit_id             INT NOT NULL,
    -- Base units in one of this unit: bag of urea = 50 (kg), box of screws = 100 (pcs), dozen = 12.
    conversion_factor   NUMERIC(18, 6) NOT NULL,
    -- Price of one whole pack, when it isn't simply factor x base price (wholesale carton price).
    sale_price          NUMERIC(18, 4) NULL,
    is_purchase_default BOOLEAN NOT NULL DEFAULT FALSE,
    is_sale_default     BOOLEAN NOT NULL DEFAULT FALSE,

    created_at          TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by          INT NULL,
    updated_at          TIMESTAMP(3) NULL,
    updated_by          INT NULL,

    CONSTRAINT pk_product_unit PRIMARY KEY (product_unit_id),
    CONSTRAINT fk_product_unit_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id) ON DELETE CASCADE,
    CONSTRAINT fk_product_unit_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT ux_product_unit UNIQUE (product_id, unit_id),
    CONSTRAINT ck_product_unit_factor CHECK (conversion_factor > 0),
    CONSTRAINT ck_product_unit_price CHECK (sale_price IS NULL OR sale_price >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_product_unit_purchase_default
    ON inventory.product_unit (product_id) WHERE is_purchase_default;
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_unit_sale_default
    ON inventory.product_unit (product_id) WHERE is_sale_default;

-- A product can carry several barcodes: the manufacturer's EAN, the shop's own label, a carton code.
CREATE TABLE IF NOT EXISTS inventory.product_barcode
(
    product_barcode_id INT GENERATED ALWAYS AS IDENTITY,
    organization_id    INT NOT NULL,
    product_id         INT NOT NULL,
    -- The pack the barcode is printed on; NULL = one base unit.
    unit_id            INT NULL,
    barcode            VARCHAR(64) NOT NULL,

    created_at         TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by         INT NULL,

    CONSTRAINT pk_product_barcode PRIMARY KEY (product_barcode_id),
    CONSTRAINT fk_product_barcode_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_product_barcode_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id) ON DELETE CASCADE,
    CONSTRAINT fk_product_barcode_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT ux_product_barcode UNIQUE (organization_id, barcode)
);

CREATE INDEX IF NOT EXISTS idx_product_barcode_product ON inventory.product_barcode (product_id);

-- A lot of a batch-tracked product, created by the first receipt (or opening stock) that brings it.
CREATE TABLE IF NOT EXISTS inventory.batch
(
    batch_id         BIGINT GENERATED ALWAYS AS IDENTITY,
    organization_id  INT NOT NULL,
    product_id       INT NOT NULL,
    batch_no         VARCHAR(60) NOT NULL,
    manufacture_date DATE NULL,
    expiry_date      DATE NULL,

    created_at       TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by       INT NULL,

    CONSTRAINT pk_batch PRIMARY KEY (batch_id),
    CONSTRAINT fk_batch_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_batch_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT ux_batch_no UNIQUE (product_id, batch_no),
    CONSTRAINT ck_batch_dates
        CHECK (expiry_date IS NULL OR manufacture_date IS NULL OR expiry_date >= manufacture_date)
);

-- Expiry alert: batches by expiry date.
CREATE INDEX IF NOT EXISTS idx_batch_expiry
    ON inventory.batch (organization_id, expiry_date) WHERE expiry_date IS NOT NULL;

-- One item of a serial-tracked product, from receipt until it leaves the stock.
CREATE TABLE IF NOT EXISTS inventory.serial_number
(
    serial_number_id           BIGINT GENERATED ALWAYS AS IDENTITY,
    organization_id            INT NOT NULL,
    product_id                 INT NOT NULL,
    serial_no                  VARCHAR(100) NOT NULL,
    batch_id                   BIGINT NULL,
    -- Where the item is now; NULL once it is in transit or has left the stock.
    warehouse_id               INT NULL,
    status                     VARCHAR(20) NOT NULL DEFAULT 'InStock',
    -- The supplier's warranty, for claims against the supplier.
    supplier_warranty_end_date DATE NULL,

    created_at                 TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                 INT NULL,
    updated_at                 TIMESTAMP(3) NULL,
    updated_by                 INT NULL,

    CONSTRAINT pk_serial_number PRIMARY KEY (serial_number_id),
    CONSTRAINT fk_serial_number_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_serial_number_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_serial_number_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT fk_serial_number_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT ux_serial_number UNIQUE (product_id, serial_no),
    CONSTRAINT ck_serial_number_status
        CHECK (status IN ('InStock', 'InTransit', 'Sold', 'ReturnedToSupplier', 'Scrapped')),
    CONSTRAINT ck_serial_number_warehouse CHECK ((status = 'InStock') = (warehouse_id IS NOT NULL))
);

-- =====================================================================
-- Stock: the ledger (stock_movement) and the running totals it keeps (stock_balance)
-- =====================================================================

-- What is in each warehouse now, per product and batch. Only the posting code writes it, in the same
-- transaction as the movement, after locking the rows (SELECT ... FOR UPDATE, in product order).
CREATE TABLE IF NOT EXISTS inventory.stock_balance
(
    stock_balance_id   BIGINT GENERATED ALWAYS AS IDENTITY,
    organization_id    INT NOT NULL,
    warehouse_id       INT NOT NULL,
    product_id         INT NOT NULL,
    -- NULL for products without batch tracking.
    batch_id           BIGINT NULL,
    quantity           NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Promised to sales orders or transfers that haven't left yet.
    reserved_quantity  NUMERIC(18, 4) NOT NULL DEFAULT 0,
    available_quantity NUMERIC(18, 4) GENERATED ALWAYS AS (quantity - reserved_quantity) STORED,
    -- Moving weighted average cost per base unit (for a batch row: that batch's cost).
    average_cost       NUMERIC(18, 6) NOT NULL DEFAULT 0,
    last_movement_at   TIMESTAMP(3) NULL,

    CONSTRAINT pk_stock_balance PRIMARY KEY (stock_balance_id),
    CONSTRAINT fk_stock_balance_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_stock_balance_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_stock_balance_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_stock_balance_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT ux_stock_balance UNIQUE NULLS NOT DISTINCT (warehouse_id, product_id, batch_id),
    CONSTRAINT ck_stock_balance_reserved CHECK (reserved_quantity >= 0),
    CONSTRAINT ck_stock_balance_cost CHECK (average_cost >= 0)
);

CREATE INDEX IF NOT EXISTS idx_stock_balance_product
    ON inventory.stock_balance (organization_id, product_id);

-- Every change to stock, ever. Rows are only inserted: a cancelled document adds reversing rows.
-- Big tenants can partition it by movement_date later.
CREATE TABLE IF NOT EXISTS inventory.stock_movement
(
    stock_movement_id          BIGINT GENERATED ALWAYS AS IDENTITY,
    organization_id            INT NOT NULL,
    movement_date              DATE NOT NULL,
    warehouse_id               INT NOT NULL,
    product_id                 INT NOT NULL,
    batch_id                   BIGINT NULL,
    movement_type              VARCHAR(30) NOT NULL,
    -- Signed, in the base unit: + into the warehouse, - out of it. 0 only for CostAdjustment
    -- (e.g. the supplier's invoice price differs from the price the goods were received at).
    quantity                   NUMERIC(18, 4) NOT NULL,
    -- Per base unit, base currency: receipts carry purchase cost plus landed charges; issues carry
    -- the average cost at the time.
    unit_cost                  NUMERIC(18, 6) NOT NULL,
    total_cost                 NUMERIC(18, 4) NOT NULL,     -- signed, base currency
    -- The stock_balance row after this movement (in posting order), for the stock card.
    balance_quantity           NUMERIC(18, 4) NOT NULL,
    balance_average_cost       NUMERIC(18, 6) NOT NULL,
    -- The document that caused it, e.g. ('GoodsReceipt', goods_receipt_id, goods_receipt_line_id).
    source_document_type       VARCHAR(30) NOT NULL,
    source_document_id         BIGINT NOT NULL,
    source_line_id             BIGINT NULL,
    source_document_no         VARCHAR(40) NOT NULL,
    -- On the rows that cancel a posted document: the row each one reverses.
    reverses_stock_movement_id BIGINT NULL,
    remarks                    VARCHAR(500) NULL,

    created_at                 TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                 INT NULL,

    CONSTRAINT pk_stock_movement PRIMARY KEY (stock_movement_id),
    CONSTRAINT fk_stock_movement_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_stock_movement_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_stock_movement_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_stock_movement_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT fk_stock_movement_reverses
        FOREIGN KEY (reverses_stock_movement_id) REFERENCES inventory.stock_movement (stock_movement_id),
    CONSTRAINT ck_stock_movement_type
        CHECK (movement_type IN ('Opening', 'PurchaseReceipt', 'PurchaseReturn', 'Sale', 'SaleReturn',
                                 'TransferOut', 'TransferIn', 'AdjustmentIn', 'AdjustmentOut',
                                 'CostAdjustment')),
    CONSTRAINT ck_stock_movement_quantity CHECK (quantity <> 0 OR movement_type = 'CostAdjustment')
);

-- Stock card of one product in one warehouse, in posting order.
CREATE INDEX IF NOT EXISTS idx_stock_movement_card
    ON inventory.stock_movement (product_id, warehouse_id, stock_movement_id);
CREATE INDEX IF NOT EXISTS idx_stock_movement_source
    ON inventory.stock_movement (source_document_type, source_document_id);
CREATE INDEX IF NOT EXISTS idx_stock_movement_date
    ON inventory.stock_movement (organization_id, movement_date);

-- Which serial-tracked items a movement moved.
CREATE TABLE IF NOT EXISTS inventory.stock_movement_serial
(
    stock_movement_id BIGINT NOT NULL,
    serial_number_id  BIGINT NOT NULL,

    CONSTRAINT pk_stock_movement_serial PRIMARY KEY (stock_movement_id, serial_number_id),
    CONSTRAINT fk_stock_movement_serial_movement
        FOREIGN KEY (stock_movement_id) REFERENCES inventory.stock_movement (stock_movement_id),
    CONSTRAINT fk_stock_movement_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

CREATE INDEX IF NOT EXISTS idx_stock_movement_serial_serial
    ON inventory.stock_movement_serial (serial_number_id);

-- =====================================================================
-- Inventory documents: stock count, adjustment, transfer
-- =====================================================================

-- Physical stock count. The expected quantities are frozen when counting starts; posting turns the
-- differences into a stock adjustment (reason CountDifference).
CREATE TABLE IF NOT EXISTS inventory.stock_count
(
    stock_count_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id       uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id INT NOT NULL,
    count_no        VARCHAR(40) NOT NULL,
    count_date      DATE NOT NULL,
    warehouse_id    INT NOT NULL,
    -- Count one category at a time; NULL = everything in the warehouse.
    category_id     INT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'Draft',
    started_at      TIMESTAMP(3) NULL,
    posted_at       TIMESTAMP(3) NULL,
    posted_by       INT NULL,
    remarks         VARCHAR(1000) NULL,

    created_at      TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by      INT NULL,
    updated_at      TIMESTAMP(3) NULL,
    updated_by      INT NULL,

    CONSTRAINT pk_stock_count PRIMARY KEY (stock_count_id),
    CONSTRAINT fk_stock_count_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_stock_count_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_stock_count_category
        FOREIGN KEY (category_id) REFERENCES inventory.category (category_id),
    CONSTRAINT ux_stock_count_no UNIQUE (organization_id, count_no),
    CONSTRAINT ck_stock_count_status CHECK (status IN ('Draft', 'InProgress', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_count_object_id ON inventory.stock_count (object_id);

CREATE TABLE IF NOT EXISTS inventory.stock_count_line
(
    stock_count_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    stock_count_id      BIGINT NOT NULL,
    product_id          INT NOT NULL,
    batch_id            BIGINT NULL,
    -- Base units: what the system expected when counting started, and what was counted.
    system_quantity     NUMERIC(18, 4) NOT NULL,
    counted_quantity    NUMERIC(18, 4) NULL,
    difference_quantity NUMERIC(18, 4) GENERATED ALWAYS AS (counted_quantity - system_quantity) STORED,
    unit_cost           NUMERIC(18, 6) NOT NULL DEFAULT 0,
    remarks             VARCHAR(500) NULL,

    CONSTRAINT pk_stock_count_line PRIMARY KEY (stock_count_line_id),
    CONSTRAINT fk_stock_count_line_stock_count
        FOREIGN KEY (stock_count_id) REFERENCES inventory.stock_count (stock_count_id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_count_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_stock_count_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT ux_stock_count_line UNIQUE NULLS NOT DISTINCT (stock_count_id, product_id, batch_id),
    CONSTRAINT ck_stock_count_line_counted CHECK (counted_quantity IS NULL OR counted_quantity >= 0)
);

-- Stock in or out without a purchase or sale: opening stock at go-live, damage, expiry, loss,
-- internal use, samples, count differences.
CREATE TABLE IF NOT EXISTS inventory.stock_adjustment
(
    stock_adjustment_id BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id           uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id     INT NOT NULL,
    adjustment_no       VARCHAR(40) NOT NULL,
    adjustment_date     DATE NOT NULL,
    warehouse_id        INT NOT NULL,
    reason              VARCHAR(30) NOT NULL,
    stock_count_id      BIGINT NULL,             -- set when posted from a stock count
    status              VARCHAR(20) NOT NULL DEFAULT 'Draft',
    total_value         NUMERIC(18, 4) NOT NULL DEFAULT 0,   -- signed, base currency
    approved_at         TIMESTAMP(3) NULL,
    approved_by         INT NULL,
    posted_at           TIMESTAMP(3) NULL,
    posted_by           INT NULL,
    remarks             VARCHAR(1000) NULL,

    created_at          TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by          INT NULL,
    updated_at          TIMESTAMP(3) NULL,
    updated_by          INT NULL,

    CONSTRAINT pk_stock_adjustment PRIMARY KEY (stock_adjustment_id),
    CONSTRAINT fk_stock_adjustment_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_stock_adjustment_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_stock_adjustment_stock_count
        FOREIGN KEY (stock_count_id) REFERENCES inventory.stock_count (stock_count_id),
    CONSTRAINT ux_stock_adjustment_no UNIQUE (organization_id, adjustment_no),
    CONSTRAINT ck_stock_adjustment_reason
        CHECK (reason IN ('Opening', 'Damage', 'Expired', 'Lost', 'InternalUse', 'Sample',
                          'CountDifference', 'Other')),
    CONSTRAINT ck_stock_adjustment_status
        CHECK (status IN ('Draft', 'PendingApproval', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_adjustment_object_id
    ON inventory.stock_adjustment (object_id);

CREATE TABLE IF NOT EXISTS inventory.stock_adjustment_line
(
    stock_adjustment_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    stock_adjustment_id      BIGINT NOT NULL,
    line_no                  INT NOT NULL,
    product_id               INT NOT NULL,
    unit_id                  INT NOT NULL,
    conversion_factor        NUMERIC(18, 6) NOT NULL,
    -- Signed, in unit_id: + adds stock, - removes it.
    quantity                 NUMERIC(18, 4) NOT NULL,
    base_quantity            NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    -- An existing batch (batch_id), or a new one (batch_no and dates; created when posted).
    batch_id                 BIGINT NULL,
    batch_no                 VARCHAR(60) NULL,
    manufacture_date         DATE NULL,
    expiry_date              DATE NULL,
    -- Per base unit. Needed for stock added (opening stock); stock removed uses the average cost.
    unit_cost                NUMERIC(18, 6) NULL,
    remarks                  VARCHAR(500) NULL,

    CONSTRAINT pk_stock_adjustment_line PRIMARY KEY (stock_adjustment_line_id),
    CONSTRAINT fk_stock_adjustment_line_adjustment
        FOREIGN KEY (stock_adjustment_id) REFERENCES inventory.stock_adjustment (stock_adjustment_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_stock_adjustment_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_stock_adjustment_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_stock_adjustment_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT ux_stock_adjustment_line UNIQUE (stock_adjustment_id, line_no),
    CONSTRAINT ck_stock_adjustment_line_quantity CHECK (quantity <> 0),
    CONSTRAINT ck_stock_adjustment_line_factor CHECK (conversion_factor > 0),
    CONSTRAINT ck_stock_adjustment_line_cost CHECK (unit_cost IS NULL OR unit_cost >= 0)
);

CREATE INDEX IF NOT EXISTS idx_stock_adjustment_line_product
    ON inventory.stock_adjustment_line (product_id);

-- Stock moved between warehouses (godown to shop, shop to shop). Dispatch takes it out of the source;
-- receiving puts it into the destination. Until then it is in transit (dispatched, not received).
CREATE TABLE IF NOT EXISTS inventory.stock_transfer
(
    stock_transfer_id     BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id             uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id       INT NOT NULL,
    transfer_no           VARCHAR(40) NOT NULL,
    transfer_date         DATE NOT NULL,
    from_warehouse_id     INT NOT NULL,
    to_warehouse_id       INT NOT NULL,
    status                VARCHAR(20) NOT NULL DEFAULT 'Draft',
    dispatched_at         TIMESTAMP(3) NULL,
    dispatched_by         INT NULL,
    received_at           TIMESTAMP(3) NULL,
    received_by           INT NULL,
    remarks               VARCHAR(1000) NULL,

    created_at            TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by            INT NULL,
    updated_at            TIMESTAMP(3) NULL,
    updated_by            INT NULL,

    CONSTRAINT pk_stock_transfer PRIMARY KEY (stock_transfer_id),
    CONSTRAINT fk_stock_transfer_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_stock_transfer_from
        FOREIGN KEY (from_warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_stock_transfer_to
        FOREIGN KEY (to_warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT ux_stock_transfer_no UNIQUE (organization_id, transfer_no),
    CONSTRAINT ck_stock_transfer_warehouses CHECK (from_warehouse_id <> to_warehouse_id),
    CONSTRAINT ck_stock_transfer_status
        CHECK (status IN ('Draft', 'InTransit', 'Received', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_transfer_object_id ON inventory.stock_transfer (object_id);

CREATE TABLE IF NOT EXISTS inventory.stock_transfer_line
(
    stock_transfer_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    stock_transfer_id      BIGINT NOT NULL,
    line_no                INT NOT NULL,
    product_id             INT NOT NULL,
    unit_id                INT NOT NULL,
    conversion_factor      NUMERIC(18, 6) NOT NULL,
    quantity               NUMERIC(18, 4) NOT NULL,
    base_quantity          NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    batch_id               BIGINT NULL,
    -- Base units that arrived; a shortfall is posted as an adjustment (reason Lost or Damage).
    received_base_quantity NUMERIC(18, 4) NULL,
    unit_cost              NUMERIC(18, 6) NOT NULL DEFAULT 0,   -- average cost at dispatch
    remarks                VARCHAR(500) NULL,

    CONSTRAINT pk_stock_transfer_line PRIMARY KEY (stock_transfer_line_id),
    CONSTRAINT fk_stock_transfer_line_transfer
        FOREIGN KEY (stock_transfer_id) REFERENCES inventory.stock_transfer (stock_transfer_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_stock_transfer_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_stock_transfer_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_stock_transfer_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT ux_stock_transfer_line UNIQUE (stock_transfer_id, line_no),
    CONSTRAINT ck_stock_transfer_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_stock_transfer_line_received
        CHECK (received_base_quantity IS NULL OR received_base_quantity >= 0)
);

-- =====================================================================
-- Purchase masters
-- =====================================================================

CREATE TABLE IF NOT EXISTS purchase.supplier
(
    supplier_id                   INT GENERATED ALWAYS AS IDENTITY,
    object_id                     uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id               INT NOT NULL,
    supplier_code                 VARCHAR(30) NOT NULL,
    supplier_name                 VARCHAR(250) NOT NULL,
    supplier_name_local           VARCHAR(250) NULL,
    supplier_group_lookup_data_id BIGINT NULL,   -- lookup "Supplier Group"
    contact_person                VARCHAR(150) NULL,
    phone                         VARCHAR(30) NULL,
    mobile                        VARCHAR(30) NULL,
    email                         VARCHAR(150) NULL,
    address_line1                 VARCHAR(250) NULL,
    address_line2                 VARCHAR(250) NULL,
    city                          VARCHAR(100) NULL,
    state                         VARCHAR(100) NULL,
    postal_code                   VARCHAR(20) NULL,
    country_code                  CHAR(2) NULL,  -- ISO 3166-1 alpha-2: BD, IN, CN, US
    -- VAT/GST registration (Bangladesh: BIN) and income-tax number (Bangladesh: TIN).
    tax_registration_no           VARCHAR(50) NULL,
    tax_id_no                     VARCHAR(50) NULL,
    trade_license_no              VARCHAR(50) NULL,
    -- Licence needed to deal in regulated goods: fertilizer or pesticide dealer, drug licence.
    regulatory_license_no         VARCHAR(100) NULL,
    -- Usual purchase currency; NULL = the base currency.
    currency_lookup_data_id       BIGINT NULL,
    payment_terms_days            INT NOT NULL DEFAULT 0,  -- 0 = cash on delivery
    credit_limit                  NUMERIC(18, 4) NULL,
    -- Owed to the supplier (+) or advanced to them (-) at go-live, in the base currency.
    opening_balance               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    opening_balance_date          DATE NULL,
    bank_name                     VARCHAR(150) NULL,
    bank_branch                   VARCHAR(150) NULL,
    bank_account_name             VARCHAR(150) NULL,
    bank_account_no               VARCHAR(50) NULL,
    -- Bangladesh routing number, SWIFT/BIC, IFSC, sort code, ...
    bank_routing_code             VARCHAR(30) NULL,
    mobile_wallet_no              VARCHAR(30) NULL,  -- bKash, Nagad, ...
    remarks                       VARCHAR(1000) NULL,
    is_active                     BOOLEAN NOT NULL DEFAULT TRUE,

    created_at                    TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                    INT NULL,
    updated_at                    TIMESTAMP(3) NULL,
    updated_by                    INT NULL,

    CONSTRAINT pk_supplier PRIMARY KEY (supplier_id),
    CONSTRAINT fk_supplier_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_supplier_group
        FOREIGN KEY (supplier_group_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT fk_supplier_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ck_supplier_terms CHECK (payment_terms_days >= 0),
    CONSTRAINT ck_supplier_credit_limit CHECK (credit_limit IS NULL OR credit_limit >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_supplier_object_id ON purchase.supplier (object_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_supplier_code
    ON purchase.supplier (organization_id, lower(supplier_code));
CREATE INDEX IF NOT EXISTS idx_supplier_name
    ON purchase.supplier (organization_id, lower(supplier_name));

-- Who supplies what: their item code, usual pack, last price and lead time.
CREATE TABLE IF NOT EXISTS purchase.supplier_product
(
    supplier_product_id     INT GENERATED ALWAYS AS IDENTITY,
    supplier_id             INT NOT NULL,
    product_id              INT NOT NULL,
    unit_id                 INT NOT NULL,
    supplier_product_code   VARCHAR(50) NULL,
    last_price              NUMERIC(18, 6) NULL,   -- per unit_id, in currency_lookup_data_id
    currency_lookup_data_id BIGINT NULL,
    lead_time_days          INT NULL,
    minimum_order_quantity  NUMERIC(18, 4) NULL,
    is_preferred            BOOLEAN NOT NULL DEFAULT FALSE,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_supplier_product PRIMARY KEY (supplier_product_id),
    CONSTRAINT fk_supplier_product_supplier
        FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id) ON DELETE CASCADE,
    CONSTRAINT fk_supplier_product_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id) ON DELETE CASCADE,
    CONSTRAINT fk_supplier_product_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_supplier_product_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_supplier_product UNIQUE (supplier_id, product_id, unit_id)
);

CREATE INDEX IF NOT EXISTS idx_supplier_product_product ON purchase.supplier_product (product_id);

-- =====================================================================
-- Purchase documents
-- =====================================================================

-- Optional internal request to buy (from a branch, or from the reorder alert); approved ones are
-- turned into purchase orders. Small shops skip it.
CREATE TABLE IF NOT EXISTS purchase.purchase_requisition
(
    purchase_requisition_id BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    requisition_no          VARCHAR(40) NOT NULL,
    requisition_date        DATE NOT NULL,
    required_date           DATE NULL,
    warehouse_id            INT NOT NULL,
    status                  VARCHAR(20) NOT NULL DEFAULT 'Draft',
    approved_at             TIMESTAMP(3) NULL,
    approved_by             INT NULL,
    remarks                 VARCHAR(1000) NULL,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_purchase_requisition PRIMARY KEY (purchase_requisition_id),
    CONSTRAINT fk_purchase_requisition_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_purchase_requisition_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT ux_purchase_requisition_no UNIQUE (organization_id, requisition_no),
    CONSTRAINT ck_purchase_requisition_status
        CHECK (status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'PartiallyOrdered',
                          'Ordered', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_requisition_object_id
    ON purchase.purchase_requisition (object_id);

CREATE TABLE IF NOT EXISTS purchase.purchase_requisition_line
(
    purchase_requisition_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    purchase_requisition_id      BIGINT NOT NULL,
    line_no                      INT NOT NULL,
    product_id                   INT NOT NULL,
    unit_id                      INT NOT NULL,
    conversion_factor            NUMERIC(18, 6) NOT NULL,
    quantity                     NUMERIC(18, 4) NOT NULL,
    ordered_quantity             NUMERIC(18, 4) NOT NULL DEFAULT 0,   -- in unit_id
    suggested_supplier_id        INT NULL,
    remarks                      VARCHAR(500) NULL,

    CONSTRAINT pk_purchase_requisition_line PRIMARY KEY (purchase_requisition_line_id),
    CONSTRAINT fk_purchase_requisition_line_requisition
        FOREIGN KEY (purchase_requisition_id)
        REFERENCES purchase.purchase_requisition (purchase_requisition_id) ON DELETE CASCADE,
    CONSTRAINT fk_purchase_requisition_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_purchase_requisition_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_purchase_requisition_line_supplier
        FOREIGN KEY (suggested_supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT ux_purchase_requisition_line UNIQUE (purchase_requisition_id, line_no),
    CONSTRAINT ck_purchase_requisition_line_quantity
        CHECK (quantity > 0 AND conversion_factor > 0 AND ordered_quantity >= 0)
);

CREATE TABLE IF NOT EXISTS purchase.purchase_order
(
    purchase_order_id       BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    order_no                VARCHAR(40) NOT NULL,
    order_date              DATE NOT NULL,
    expected_date           DATE NULL,
    supplier_id             INT NOT NULL,
    warehouse_id            INT NOT NULL,     -- deliver to
    supplier_reference      VARCHAR(60) NULL, -- their quotation or proforma number
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    payment_terms_days      INT NOT NULL DEFAULT 0,
    subtotal_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    discount_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    charge_amount           NUMERIC(18, 4) NOT NULL DEFAULT 0,  -- expected freight etc.
    total_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'Draft',
    approved_at             TIMESTAMP(3) NULL,
    approved_by             INT NULL,
    terms_and_conditions    TEXT NULL,
    remarks                 VARCHAR(1000) NULL,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_purchase_order PRIMARY KEY (purchase_order_id),
    CONSTRAINT fk_purchase_order_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_purchase_order_supplier
        FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_purchase_order_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_purchase_order_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_purchase_order_no UNIQUE (organization_id, order_no),
    CONSTRAINT ck_purchase_order_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_purchase_order_status
        CHECK (status IN ('Draft', 'PendingApproval', 'Approved', 'PartiallyReceived', 'Received',
                          'Closed', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_order_object_id ON purchase.purchase_order (object_id);
CREATE INDEX IF NOT EXISTS idx_purchase_order_supplier
    ON purchase.purchase_order (supplier_id, order_date);
CREATE INDEX IF NOT EXISTS idx_purchase_order_status
    ON purchase.purchase_order (organization_id, status, order_date);

CREATE TABLE IF NOT EXISTS purchase.purchase_order_line
(
    purchase_order_line_id       BIGINT GENERATED ALWAYS AS IDENTITY,
    purchase_order_id            BIGINT NOT NULL,
    line_no                      INT NOT NULL,
    purchase_requisition_line_id BIGINT NULL,
    product_id                   INT NOT NULL,
    description                  VARCHAR(500) NULL,
    unit_id                      INT NOT NULL,
    conversion_factor            NUMERIC(18, 6) NOT NULL,
    quantity                     NUMERIC(18, 4) NOT NULL,
    base_quantity                NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    unit_price                   NUMERIC(18, 6) NOT NULL DEFAULT 0,   -- per unit_id
    discount_percent             NUMERIC(9, 4) NOT NULL DEFAULT 0,
    discount_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_group_id                 INT NULL,
    tax_amount                   NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_total                   NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- In unit_id, kept up to date by goods receipts and supplier invoices.
    received_quantity            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    invoiced_quantity            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    remarks                      VARCHAR(500) NULL,

    CONSTRAINT pk_purchase_order_line PRIMARY KEY (purchase_order_line_id),
    CONSTRAINT fk_purchase_order_line_order
        FOREIGN KEY (purchase_order_id) REFERENCES purchase.purchase_order (purchase_order_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_purchase_order_line_requisition_line
        FOREIGN KEY (purchase_requisition_line_id)
        REFERENCES purchase.purchase_requisition_line (purchase_requisition_line_id),
    CONSTRAINT fk_purchase_order_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_purchase_order_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_purchase_order_line_tax_group
        FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ux_purchase_order_line UNIQUE (purchase_order_id, line_no),
    CONSTRAINT ck_purchase_order_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_purchase_order_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0
               AND discount_percent BETWEEN 0 AND 100),
    CONSTRAINT ck_purchase_order_line_progress CHECK (received_quantity >= 0 AND invoiced_quantity >= 0)
);

CREATE INDEX IF NOT EXISTS idx_purchase_order_line_product ON purchase.purchase_order_line (product_id);

-- Goods receipt (GRN), optional: goods received before the bill, which is entered later against it.
-- Posting it puts the stock in. Most shops skip it and enter the supplier invoice directly.
CREATE TABLE IF NOT EXISTS purchase.goods_receipt
(
    goods_receipt_id        BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    receipt_no              VARCHAR(40) NOT NULL,
    receipt_date            DATE NOT NULL,
    -- NULL only for a cash purchase from a walk-in supplier (see purchase_invoice.purchase_type).
    supplier_id             INT NULL,
    warehouse_id            INT NOT NULL,
    purchase_order_id       BIGINT NULL,
    supplier_challan_no     VARCHAR(60) NULL,   -- the supplier's delivery challan / note
    supplier_challan_date   DATE NULL,
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    status                  VARCHAR(20) NOT NULL DEFAULT 'Draft',
    posted_at               TIMESTAMP(3) NULL,
    posted_by               INT NULL,
    remarks                 VARCHAR(1000) NULL,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_goods_receipt PRIMARY KEY (goods_receipt_id),
    CONSTRAINT fk_goods_receipt_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_goods_receipt_supplier
        FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_goods_receipt_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_goods_receipt_order
        FOREIGN KEY (purchase_order_id) REFERENCES purchase.purchase_order (purchase_order_id),
    CONSTRAINT fk_goods_receipt_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_goods_receipt_no UNIQUE (organization_id, receipt_no),
    CONSTRAINT ck_goods_receipt_supplier CHECK (supplier_id IS NOT NULL OR purchase_order_id IS NULL),
    CONSTRAINT ck_goods_receipt_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_goods_receipt_status CHECK (status IN ('Draft', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_goods_receipt_object_id ON purchase.goods_receipt (object_id);
CREATE INDEX IF NOT EXISTS idx_goods_receipt_supplier
    ON purchase.goods_receipt (supplier_id, receipt_date);
CREATE INDEX IF NOT EXISTS idx_goods_receipt_order
    ON purchase.goods_receipt (purchase_order_id) WHERE purchase_order_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS purchase.goods_receipt_line
(
    goods_receipt_line_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    goods_receipt_id       BIGINT NOT NULL,
    line_no                INT NOT NULL,
    purchase_order_line_id BIGINT NULL,
    product_id             INT NOT NULL,
    unit_id                INT NOT NULL,
    conversion_factor      NUMERIC(18, 6) NOT NULL,
    -- Accepted into stock; rejected_quantity (damaged on arrival) went back with the delivery.
    quantity               NUMERIC(18, 4) NOT NULL,
    base_quantity          NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    rejected_quantity      NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- A new batch (created when posted) or an existing one.
    batch_no               VARCHAR(60) NULL,
    manufacture_date       DATE NULL,
    expiry_date            DATE NULL,
    batch_id               BIGINT NULL,
    unit_price             NUMERIC(18, 6) NOT NULL DEFAULT 0,   -- per unit_id, document currency
    discount_amount        NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- This line's share of the receipt's purchase_charge rows, base currency.
    charge_amount          NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- What one base unit cost, base currency: (price - discount) x rate + non-recoverable tax +
    -- charges, divided by base_quantity. This is the cost the stock movement carries.
    landed_unit_cost       NUMERIC(18, 6) NOT NULL DEFAULT 0,
    -- In unit_id, kept up to date by supplier invoices and returns.
    invoiced_quantity      NUMERIC(18, 4) NOT NULL DEFAULT 0,
    returned_quantity      NUMERIC(18, 4) NOT NULL DEFAULT 0,
    remarks                VARCHAR(500) NULL,

    CONSTRAINT pk_goods_receipt_line PRIMARY KEY (goods_receipt_line_id),
    CONSTRAINT fk_goods_receipt_line_receipt
        FOREIGN KEY (goods_receipt_id) REFERENCES purchase.goods_receipt (goods_receipt_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_goods_receipt_line_order_line
        FOREIGN KEY (purchase_order_line_id) REFERENCES purchase.purchase_order_line (purchase_order_line_id),
    CONSTRAINT fk_goods_receipt_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_goods_receipt_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_goods_receipt_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT ux_goods_receipt_line UNIQUE (goods_receipt_id, line_no),
    CONSTRAINT ck_goods_receipt_line_quantity
        CHECK (quantity > 0 AND conversion_factor > 0 AND rejected_quantity >= 0),
    CONSTRAINT ck_goods_receipt_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND charge_amount >= 0 AND landed_unit_cost >= 0),
    CONSTRAINT ck_goods_receipt_line_progress CHECK (invoiced_quantity >= 0 AND returned_quantity >= 0)
);

CREATE INDEX IF NOT EXISTS idx_goods_receipt_line_product ON purchase.goods_receipt_line (product_id);
CREATE INDEX IF NOT EXISTS idx_goods_receipt_line_order_line
    ON purchase.goods_receipt_line (purchase_order_line_id) WHERE purchase_order_line_id IS NOT NULL;

-- Serial numbers typed or scanned on a receipt line; posting creates the serial_number rows.
CREATE TABLE IF NOT EXISTS purchase.goods_receipt_serial
(
    goods_receipt_line_id BIGINT NOT NULL,
    serial_no             VARCHAR(100) NOT NULL,
    serial_number_id      BIGINT NULL,   -- set when posted

    CONSTRAINT pk_goods_receipt_serial PRIMARY KEY (goods_receipt_line_id, serial_no),
    CONSTRAINT fk_goods_receipt_serial_line
        FOREIGN KEY (goods_receipt_line_id) REFERENCES purchase.goods_receipt_line (goods_receipt_line_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_goods_receipt_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

-- The supplier's bill: what was bought and what is owed. The usual way to enter a purchase.
--   receives_stock = true:  a direct purchase. Posting it also puts the stock into warehouse_id, with
--                           batches, expiry dates, serial numbers and landed cost on its own lines.
--   receives_stock = false: a bill for goods already received on goods receipts (lines point at
--                           goods_receipt_line); posting it changes no stock.
-- Either way it is Cash or Credit:
--   Cash:   paid in full on the spot (paid_amount = total_amount). The supplier may be a walk-in one,
--           named in supplier_name only.
--   Credit: all or part is paid later, so the supplier must be on file: posting adds the unpaid part
--           to their dues (supplier_ledger), settled later by supplier payments.
CREATE TABLE IF NOT EXISTS purchase.purchase_invoice
(
    purchase_invoice_id     BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    invoice_no              VARCHAR(40) NOT NULL,
    invoice_date            DATE NOT NULL,
    purchase_type           VARCHAR(10) NOT NULL DEFAULT 'Cash',
    due_date                DATE NULL,           -- credit: invoice_date + the supplier's payment terms
    supplier_id             INT NULL,            -- required for Credit
    -- A walk-in supplier on a cash purchase, for the memo; otherwise taken from the supplier.
    supplier_name           VARCHAR(250) NULL,
    supplier_mobile         VARCHAR(30) NULL,
    receives_stock          BOOLEAN NOT NULL DEFAULT TRUE,
    warehouse_id            INT NULL,            -- where a direct purchase puts the stock
    -- Set when buying against a purchase order (with or without a goods receipt in between).
    purchase_order_id       BIGINT NULL,
    supplier_invoice_no     VARCHAR(60) NULL,
    supplier_invoice_date   DATE NULL,
    -- The supplier's tax (VAT) invoice; Bangladesh: Mushak-6.3 number. Needed to claim input VAT.
    tax_invoice_no          VARCHAR(60) NULL,
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    subtotal_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    discount_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    charge_amount           NUMERIC(18, 4) NOT NULL DEFAULT 0,  -- charges this supplier billed
    round_off_amount        NUMERIC(18, 4) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Paid on the spot with the purchase: all of it for Cash, any part (or nothing) for Credit.
    -- A cheque is recorded as a supplier payment instead, because it can bounce.
    paid_amount             NUMERIC(18, 4) NOT NULL DEFAULT 0,
    payment_method          VARCHAR(20) NULL,
    payment_provider        VARCHAR(100) NULL,   -- bank or wallet name
    payment_reference_no    VARCHAR(100) NULL,   -- transaction ID
    -- paid_amount plus what later payments and returns settled (invoice_allocation), document currency.
    settled_amount          NUMERIC(18, 4) NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'Draft',
    payment_status          VARCHAR(20) NOT NULL DEFAULT 'Unpaid',
    posted_at               TIMESTAMP(3) NULL,
    posted_by               INT NULL,
    remarks                 VARCHAR(1000) NULL,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_purchase_invoice PRIMARY KEY (purchase_invoice_id),
    CONSTRAINT fk_purchase_invoice_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_purchase_invoice_supplier
        FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_purchase_invoice_order
        FOREIGN KEY (purchase_order_id) REFERENCES purchase.purchase_order (purchase_order_id),
    CONSTRAINT fk_purchase_invoice_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_purchase_invoice_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_purchase_invoice_no UNIQUE (organization_id, invoice_no),
    CONSTRAINT ck_purchase_invoice_warehouse CHECK (NOT receives_stock OR warehouse_id IS NOT NULL),
    CONSTRAINT ck_purchase_invoice_order_supplier CHECK (purchase_order_id IS NULL OR supplier_id IS NOT NULL),
    CONSTRAINT ck_purchase_invoice_type CHECK (purchase_type IN ('Cash', 'Credit')),
    CONSTRAINT ck_purchase_invoice_credit_supplier CHECK (purchase_type = 'Cash' OR supplier_id IS NOT NULL),
    CONSTRAINT ck_purchase_invoice_cash_paid CHECK (purchase_type = 'Credit' OR paid_amount = total_amount),
    CONSTRAINT ck_purchase_invoice_paid
        CHECK (paid_amount BETWEEN 0 AND total_amount AND (paid_amount = 0 OR payment_method IS NOT NULL)),
    CONSTRAINT ck_purchase_invoice_payment_method
        CHECK (payment_method IN ('Cash', 'Bank', 'MobileWallet', 'Card', 'Other')),
    CONSTRAINT ck_purchase_invoice_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_purchase_invoice_settled
        CHECK (settled_amount >= paid_amount AND settled_amount <= total_amount),
    CONSTRAINT ck_purchase_invoice_status CHECK (status IN ('Draft', 'Posted', 'Cancelled')),
    CONSTRAINT ck_purchase_invoice_payment_status
        CHECK (payment_status IN ('Unpaid', 'PartiallyPaid', 'Paid'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_invoice_object_id
    ON purchase.purchase_invoice (object_id);
-- The same supplier bill can't be entered twice.
CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_invoice_supplier_invoice
    ON purchase.purchase_invoice (supplier_id, lower(supplier_invoice_no))
    WHERE supplier_invoice_no IS NOT NULL AND status <> 'Cancelled';
CREATE INDEX IF NOT EXISTS idx_purchase_invoice_supplier
    ON purchase.purchase_invoice (supplier_id, invoice_date);
-- Payables due: unpaid invoices by due date.
CREATE INDEX IF NOT EXISTS idx_purchase_invoice_due
    ON purchase.purchase_invoice (organization_id, due_date)
    WHERE status = 'Posted' AND payment_status <> 'Paid';

CREATE TABLE IF NOT EXISTS purchase.purchase_invoice_line
(
    purchase_invoice_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    purchase_invoice_id      BIGINT NOT NULL,
    line_no                  INT NOT NULL,
    -- Billing goods already received (receives_stock = false): the receipt line billed.
    goods_receipt_line_id    BIGINT NULL,
    purchase_order_line_id   BIGINT NULL,
    product_id               INT NOT NULL,
    description              VARCHAR(500) NULL,
    unit_id                  INT NOT NULL,
    conversion_factor        NUMERIC(18, 6) NOT NULL,
    quantity                 NUMERIC(18, 4) NOT NULL,
    base_quantity            NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    unit_price               NUMERIC(18, 6) NOT NULL DEFAULT 0,
    discount_percent         NUMERIC(9, 4) NOT NULL DEFAULT 0,
    discount_amount          NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_group_id             INT NULL,
    tax_amount               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_total               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Direct purchase only (receives_stock = true), as on goods_receipt_line: a new batch (created
    -- when posted) or an existing one, this line's share of the invoice's purchase_charge rows, and
    -- the cost per base unit the stock movement carries.
    batch_no                 VARCHAR(60) NULL,
    manufacture_date         DATE NULL,
    expiry_date              DATE NULL,
    batch_id                 BIGINT NULL,
    charge_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    landed_unit_cost         NUMERIC(18, 6) NOT NULL DEFAULT 0,
    -- In unit_id, kept up to date by purchase returns.
    returned_quantity        NUMERIC(18, 4) NOT NULL DEFAULT 0,

    CONSTRAINT pk_purchase_invoice_line PRIMARY KEY (purchase_invoice_line_id),
    CONSTRAINT fk_purchase_invoice_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT fk_purchase_invoice_line_invoice
        FOREIGN KEY (purchase_invoice_id) REFERENCES purchase.purchase_invoice (purchase_invoice_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_purchase_invoice_line_receipt_line
        FOREIGN KEY (goods_receipt_line_id) REFERENCES purchase.goods_receipt_line (goods_receipt_line_id),
    CONSTRAINT fk_purchase_invoice_line_order_line
        FOREIGN KEY (purchase_order_line_id) REFERENCES purchase.purchase_order_line (purchase_order_line_id),
    CONSTRAINT fk_purchase_invoice_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_purchase_invoice_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_purchase_invoice_line_tax_group
        FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ux_purchase_invoice_line UNIQUE (purchase_invoice_id, line_no),
    CONSTRAINT ck_purchase_invoice_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_purchase_invoice_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0
               AND discount_percent BETWEEN 0 AND 100
               AND charge_amount >= 0 AND landed_unit_cost >= 0 AND returned_quantity >= 0),
    -- A line billing a goods receipt took its stock (and batch) from that receipt.
    CONSTRAINT ck_purchase_invoice_line_stock
        CHECK (goods_receipt_line_id IS NULL OR (batch_no IS NULL AND batch_id IS NULL AND charge_amount = 0))
);

CREATE INDEX IF NOT EXISTS idx_purchase_invoice_line_receipt_line
    ON purchase.purchase_invoice_line (goods_receipt_line_id) WHERE goods_receipt_line_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_purchase_invoice_line_product ON purchase.purchase_invoice_line (product_id);

-- Serial numbers typed or scanned on a direct purchase line; posting creates the serial_number rows.
CREATE TABLE IF NOT EXISTS purchase.purchase_invoice_serial
(
    purchase_invoice_line_id BIGINT NOT NULL,
    serial_no                VARCHAR(100) NOT NULL,
    serial_number_id         BIGINT NULL,   -- set when posted

    CONSTRAINT pk_purchase_invoice_serial PRIMARY KEY (purchase_invoice_line_id, serial_no),
    CONSTRAINT fk_purchase_invoice_serial_line
        FOREIGN KEY (purchase_invoice_line_id)
        REFERENCES purchase.purchase_invoice_line (purchase_invoice_line_id) ON DELETE CASCADE,
    CONSTRAINT fk_purchase_invoice_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

-- Costs of getting the goods in, spread over the stock lines' cost (landed cost): transport
-- (truck/van fare), labour (loading/unloading), customs duty, C&F, insurance. They belong to the
-- document that brought the stock in: a direct purchase invoice or a goods receipt.
CREATE TABLE IF NOT EXISTS purchase.purchase_charge
(
    purchase_charge_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    purchase_invoice_id BIGINT NULL,
    goods_receipt_id    BIGINT NULL,
    charge_name         VARCHAR(150) NOT NULL,
    amount              NUMERIC(18, 4) NOT NULL,   -- base currency
    -- Spread by line value or by base quantity (better for bulk goods like fertilizer or cement).
    allocation_method   VARCHAR(10) NOT NULL DEFAULT 'Value',
    -- Who is owed it: the supplier (it is on their invoice), a transporter set up as a supplier, or
    -- NULL when it was paid on the spot.
    payee_supplier_id   INT NULL,
    remarks             VARCHAR(500) NULL,

    created_at          TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by          INT NULL,

    CONSTRAINT pk_purchase_charge PRIMARY KEY (purchase_charge_id),
    CONSTRAINT fk_purchase_charge_invoice
        FOREIGN KEY (purchase_invoice_id) REFERENCES purchase.purchase_invoice (purchase_invoice_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_purchase_charge_receipt
        FOREIGN KEY (goods_receipt_id) REFERENCES purchase.goods_receipt (goods_receipt_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_purchase_charge_payee
        FOREIGN KEY (payee_supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT ck_purchase_charge_document CHECK (num_nonnulls(purchase_invoice_id, goods_receipt_id) = 1),
    CONSTRAINT ck_purchase_charge_amount CHECK (amount > 0),
    CONSTRAINT ck_purchase_charge_method CHECK (allocation_method IN ('Value', 'Quantity'))
);

CREATE INDEX IF NOT EXISTS idx_purchase_charge_invoice
    ON purchase.purchase_charge (purchase_invoice_id) WHERE purchase_invoice_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_purchase_charge_receipt
    ON purchase.purchase_charge (goods_receipt_id) WHERE goods_receipt_id IS NOT NULL;

-- Each tax on an invoice line, for the purchase VAT register (Bangladesh: Mushak-6.1) and tax returns.
CREATE TABLE IF NOT EXISTS purchase.purchase_invoice_line_tax
(
    purchase_invoice_line_id BIGINT NOT NULL,
    tax_rate_id              INT NOT NULL,
    taxable_amount           NUMERIC(18, 4) NOT NULL,
    tax_amount               NUMERIC(18, 4) NOT NULL,

    CONSTRAINT pk_purchase_invoice_line_tax PRIMARY KEY (purchase_invoice_line_id, tax_rate_id),
    CONSTRAINT fk_purchase_invoice_line_tax_line
        FOREIGN KEY (purchase_invoice_line_id)
        REFERENCES purchase.purchase_invoice_line (purchase_invoice_line_id) ON DELETE CASCADE,
    CONSTRAINT fk_purchase_invoice_line_tax_rate
        FOREIGN KEY (tax_rate_id) REFERENCES public.tax_rate (tax_rate_id)
);

-- Goods sent back to the supplier (debit note). Posting takes the stock out, and either:
--   AdjustDue: the value is deducted from what is owed (on credit), so the supplier must be on file;
--   Refund:    the supplier pays it back on the spot (refund_* columns), e.g. for a cash purchase.
CREATE TABLE IF NOT EXISTS purchase.purchase_return
(
    purchase_return_id      BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    return_no               VARCHAR(40) NOT NULL,
    return_date             DATE NOT NULL,
    supplier_id             INT NULL,            -- required for AdjustDue
    supplier_name           VARCHAR(250) NULL,   -- a walk-in supplier, for the memo
    warehouse_id            INT NOT NULL,
    goods_receipt_id        BIGINT NULL,
    purchase_invoice_id     BIGINT NULL,
    reason                  VARCHAR(20) NOT NULL,
    settlement_type         VARCHAR(10) NOT NULL DEFAULT 'AdjustDue',
    refund_method           VARCHAR(20) NULL,
    refund_provider         VARCHAR(100) NULL,
    refund_reference_no     VARCHAR(100) NULL,
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    subtotal_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    discount_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'Draft',
    posted_at               TIMESTAMP(3) NULL,
    posted_by               INT NULL,
    remarks                 VARCHAR(1000) NULL,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_purchase_return PRIMARY KEY (purchase_return_id),
    CONSTRAINT fk_purchase_return_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_purchase_return_supplier
        FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_purchase_return_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_purchase_return_receipt
        FOREIGN KEY (goods_receipt_id) REFERENCES purchase.goods_receipt (goods_receipt_id),
    CONSTRAINT fk_purchase_return_invoice
        FOREIGN KEY (purchase_invoice_id) REFERENCES purchase.purchase_invoice (purchase_invoice_id),
    CONSTRAINT fk_purchase_return_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_purchase_return_no UNIQUE (organization_id, return_no),
    CONSTRAINT ck_purchase_return_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_purchase_return_reason
        CHECK (reason IN ('Damaged', 'Expired', 'WrongItem', 'Excess', 'QualityIssue', 'Other')),
    CONSTRAINT ck_purchase_return_settlement CHECK (settlement_type IN ('AdjustDue', 'Refund')),
    CONSTRAINT ck_purchase_return_due_supplier CHECK (settlement_type = 'Refund' OR supplier_id IS NOT NULL),
    CONSTRAINT ck_purchase_return_refund_method
        CHECK (refund_method IS NULL OR (settlement_type = 'Refund'
                                         AND refund_method IN ('Cash', 'Bank', 'MobileWallet', 'Card', 'Other'))),
    CONSTRAINT ck_purchase_return_status CHECK (status IN ('Draft', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_return_object_id ON purchase.purchase_return (object_id);
CREATE INDEX IF NOT EXISTS idx_purchase_return_supplier
    ON purchase.purchase_return (supplier_id, return_date);

CREATE TABLE IF NOT EXISTS purchase.purchase_return_line
(
    purchase_return_line_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    purchase_return_id       BIGINT NOT NULL,
    line_no                  INT NOT NULL,
    goods_receipt_line_id    BIGINT NULL,
    purchase_invoice_line_id BIGINT NULL,
    product_id               INT NOT NULL,
    unit_id                  INT NOT NULL,
    conversion_factor        NUMERIC(18, 6) NOT NULL,
    quantity                 NUMERIC(18, 4) NOT NULL,
    base_quantity            NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    batch_id                 BIGINT NULL,
    unit_price               NUMERIC(18, 6) NOT NULL DEFAULT 0,
    discount_amount          NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_group_id             INT NULL,
    tax_amount               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_total               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    remarks                  VARCHAR(500) NULL,

    CONSTRAINT pk_purchase_return_line PRIMARY KEY (purchase_return_line_id),
    CONSTRAINT fk_purchase_return_line_return
        FOREIGN KEY (purchase_return_id) REFERENCES purchase.purchase_return (purchase_return_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_purchase_return_line_receipt_line
        FOREIGN KEY (goods_receipt_line_id) REFERENCES purchase.goods_receipt_line (goods_receipt_line_id),
    CONSTRAINT fk_purchase_return_line_invoice_line
        FOREIGN KEY (purchase_invoice_line_id)
        REFERENCES purchase.purchase_invoice_line (purchase_invoice_line_id),
    CONSTRAINT fk_purchase_return_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_purchase_return_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_purchase_return_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT fk_purchase_return_line_tax_group
        FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ux_purchase_return_line UNIQUE (purchase_return_id, line_no),
    CONSTRAINT ck_purchase_return_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_purchase_return_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0)
);

-- The serial-tracked items a return line sends back.
CREATE TABLE IF NOT EXISTS purchase.purchase_return_serial
(
    purchase_return_line_id BIGINT NOT NULL,
    serial_number_id        BIGINT NOT NULL,

    CONSTRAINT pk_purchase_return_serial PRIMARY KEY (purchase_return_line_id, serial_number_id),
    CONSTRAINT fk_purchase_return_serial_line
        FOREIGN KEY (purchase_return_line_id) REFERENCES purchase.purchase_return_line (purchase_return_line_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_purchase_return_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

-- Money paid to a supplier after the purchase, or refunded by one (e.g. an unused advance).
-- A payment not allocated to invoices is an advance.
CREATE TABLE IF NOT EXISTS purchase.supplier_payment
(
    supplier_payment_id     BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    payment_no              VARCHAR(40) NOT NULL,
    payment_date            DATE NOT NULL,
    supplier_id             INT NOT NULL,
    payment_type            VARCHAR(10) NOT NULL DEFAULT 'Payment',
    -- MobileWallet: bKash, Nagad, Rocket, PayPal, ... (payment_provider says which).
    payment_method          VARCHAR(20) NOT NULL,
    payment_provider        VARCHAR(100) NULL,   -- bank or wallet name
    reference_no            VARCHAR(100) NULL,   -- transaction ID, cheque or transfer number
    cheque_date             DATE NULL,           -- post-dated cheques are common
    cheque_status           VARCHAR(10) NULL,
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    amount                  NUMERIC(18, 4) NOT NULL,
    -- Tax withheld from the supplier and paid to the government for them (Bangladesh: VDS / AIT;
    -- elsewhere TDS or withholding tax). It settles the supplier's dues just like cash.
    tax_deducted_amount     NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Of amount + tax_deducted_amount, how much is allocated to invoices.
    allocated_amount        NUMERIC(18, 4) NOT NULL DEFAULT 0,
    status                  VARCHAR(20) NOT NULL DEFAULT 'Draft',
    posted_at               TIMESTAMP(3) NULL,
    posted_by               INT NULL,
    remarks                 VARCHAR(1000) NULL,

    created_at              TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT NULL,
    updated_at              TIMESTAMP(3) NULL,
    updated_by              INT NULL,

    CONSTRAINT pk_supplier_payment PRIMARY KEY (supplier_payment_id),
    CONSTRAINT fk_supplier_payment_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_supplier_payment_supplier
        FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_supplier_payment_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_supplier_payment_no UNIQUE (organization_id, payment_no),
    CONSTRAINT ck_supplier_payment_type CHECK (payment_type IN ('Payment', 'Refund')),
    CONSTRAINT ck_supplier_payment_method
        CHECK (payment_method IN ('Cash', 'Bank', 'Cheque', 'MobileWallet', 'Card', 'Other')),
    CONSTRAINT ck_supplier_payment_cheque
        CHECK (cheque_status IS NULL OR (payment_method = 'Cheque'
                                         AND cheque_status IN ('Pending', 'Cleared', 'Bounced'))),
    CONSTRAINT ck_supplier_payment_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_supplier_payment_amount
        CHECK (amount > 0 AND tax_deducted_amount >= 0
               AND allocated_amount BETWEEN 0 AND amount + tax_deducted_amount),
    CONSTRAINT ck_supplier_payment_status CHECK (status IN ('Draft', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_supplier_payment_object_id ON purchase.supplier_payment (object_id);
CREATE INDEX IF NOT EXISTS idx_supplier_payment_supplier
    ON purchase.supplier_payment (supplier_id, payment_date);

-- Which invoices a payment or a return settles, in the invoice's currency (allocate only between
-- documents in the same currency).
CREATE TABLE IF NOT EXISTS purchase.invoice_allocation
(
    invoice_allocation_id BIGINT GENERATED ALWAYS AS IDENTITY,
    organization_id       INT NOT NULL,
    purchase_invoice_id   BIGINT NOT NULL,
    supplier_payment_id   BIGINT NULL,
    purchase_return_id    BIGINT NULL,
    allocation_date       DATE NOT NULL,
    amount                NUMERIC(18, 4) NOT NULL,

    created_at            TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by            INT NULL,

    CONSTRAINT pk_invoice_allocation PRIMARY KEY (invoice_allocation_id),
    CONSTRAINT fk_invoice_allocation_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_invoice_allocation_invoice
        FOREIGN KEY (purchase_invoice_id) REFERENCES purchase.purchase_invoice (purchase_invoice_id),
    CONSTRAINT fk_invoice_allocation_payment
        FOREIGN KEY (supplier_payment_id) REFERENCES purchase.supplier_payment (supplier_payment_id),
    CONSTRAINT fk_invoice_allocation_return
        FOREIGN KEY (purchase_return_id) REFERENCES purchase.purchase_return (purchase_return_id),
    CONSTRAINT ck_invoice_allocation_source CHECK (num_nonnulls(supplier_payment_id, purchase_return_id) = 1),
    CONSTRAINT ck_invoice_allocation_amount CHECK (amount > 0)
);

CREATE INDEX IF NOT EXISTS idx_invoice_allocation_invoice ON purchase.invoice_allocation (purchase_invoice_id);
CREATE INDEX IF NOT EXISTS idx_invoice_allocation_payment
    ON purchase.invoice_allocation (supplier_payment_id) WHERE supplier_payment_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_invoice_allocation_return
    ON purchase.invoice_allocation (purchase_return_id) WHERE purchase_return_id IS NOT NULL;

-- =====================================================================
-- Views
-- =====================================================================

-- Stock per product and warehouse (batches added up), with its value and the reorder flag.
CREATE OR REPLACE VIEW inventory.stock_on_hand AS
SELECT b.organization_id,
       b.warehouse_id,
       b.product_id,
       sum(b.quantity)                  AS quantity,
       sum(b.reserved_quantity)         AS reserved_quantity,
       sum(b.available_quantity)        AS available_quantity,
       round(sum(b.quantity * b.average_cost), 4) AS stock_value,
       p.reorder_level,
       sum(b.quantity) <= p.reorder_level AND p.reorder_level > 0 AS needs_reorder
FROM inventory.stock_balance b
JOIN inventory.product p ON p.product_id = b.product_id
GROUP BY b.organization_id, b.warehouse_id, b.product_id, p.reorder_level;

-- Everything that changed what is owed to each supplier on file, in the base currency, with the
-- running balance (+ = owed to the supplier, - = the supplier owes us / advance). A cash purchase
-- from a supplier on file shows as Invoice and PaidOnInvoice, netting to zero.
CREATE OR REPLACE VIEW purchase.supplier_ledger AS
WITH entries AS
(
    SELECT s.organization_id, s.supplier_id, s.opening_balance_date AS entry_date, 0 AS entry_order,
           'Opening' AS entry_type, NULL::bigint AS document_id, NULL::varchar AS document_no,
           greatest(-s.opening_balance, 0) AS debit_amount,
           greatest(s.opening_balance, 0)  AS credit_amount
    FROM purchase.supplier s
    WHERE s.opening_balance <> 0

    UNION ALL
    SELECT i.organization_id, i.supplier_id, i.invoice_date, 1, 'Invoice', i.purchase_invoice_id,
           i.invoice_no, 0, round(i.total_amount * i.exchange_rate, 4)
    FROM purchase.purchase_invoice i
    WHERE i.status = 'Posted' AND i.supplier_id IS NOT NULL

    UNION ALL
    SELECT i.organization_id, i.supplier_id, i.invoice_date, 2, 'PaidOnInvoice', i.purchase_invoice_id,
           i.invoice_no, round(i.paid_amount * i.exchange_rate, 4), 0
    FROM purchase.purchase_invoice i
    WHERE i.status = 'Posted' AND i.supplier_id IS NOT NULL AND i.paid_amount > 0

    UNION ALL
    SELECT r.organization_id, r.supplier_id, r.return_date, 3, 'Return', r.purchase_return_id,
           r.return_no, round(r.total_amount * r.exchange_rate, 4), 0
    FROM purchase.purchase_return r
    WHERE r.status = 'Posted' AND r.supplier_id IS NOT NULL

    UNION ALL
    SELECT r.organization_id, r.supplier_id, r.return_date, 4, 'ReturnRefund', r.purchase_return_id,
           r.return_no, 0, round(r.total_amount * r.exchange_rate, 4)
    FROM purchase.purchase_return r
    WHERE r.status = 'Posted' AND r.supplier_id IS NOT NULL AND r.settlement_type = 'Refund'

    UNION ALL
    SELECT p.organization_id, p.supplier_id, p.payment_date, 5, p.payment_type, p.supplier_payment_id,
           p.payment_no,
           round(CASE WHEN p.payment_type = 'Payment' THEN (p.amount + p.tax_deducted_amount) * p.exchange_rate ELSE 0 END, 4),
           round(CASE WHEN p.payment_type = 'Refund' THEN p.amount * p.exchange_rate ELSE 0 END, 4)
    FROM purchase.supplier_payment p
    WHERE p.status = 'Posted'
      AND NOT (p.payment_method = 'Cheque' AND p.cheque_status = 'Bounced')
)
SELECT e.organization_id,
       e.supplier_id,
       e.entry_date,
       e.entry_type,
       e.document_id,
       e.document_no,
       e.debit_amount,
       e.credit_amount,
       sum(e.credit_amount - e.debit_amount) OVER (
           PARTITION BY e.supplier_id
           ORDER BY e.entry_date NULLS FIRST, e.entry_order, e.document_id
           ROWS UNBOUNDED PRECEDING) AS balance
FROM entries e;

COMMIT;
