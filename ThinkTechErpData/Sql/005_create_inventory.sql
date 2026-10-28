-- Inventory: purchases, sales, returns and damage, kept consistent by a stock ledger.
--
-- How stock works
--   * Every change to stock is a row in inventory.stock_movements, which is append-only.
--     On-hand quantity and moving-average cost per product, warehouse and batch live in
--     inventory.stock_balances; only inventory.apply_stock_movement changes them, and stock
--     can never go negative.
--   * Purchases, sales, returns, damages and stock adjustments are entered as 'draft' (free
--     to edit or delete) and change stock only when posted with the matching inventory.post_*
--     function. A posted document is never edited: inventory.cancel_document books
--     reversing movements instead, so the ledger keeps the full history.
--
-- Conventions
--   * IDs are GENERATED ALWAYS AS IDENTITY: the database always assigns them, so sequences
--     can't fall behind rows inserted with explicit IDs (see 004_security_fix_sequences.sql).
--   * Business dates are "date"; audit times are timestamptz. *_by columns hold
--     security."User".id, without a cross-schema foreign key (as in the sms schema).
--   * Money is numeric(14,2), quantities numeric(14,3), unit costs numeric(18,6).
--   * Functions are not executable by PUBLIC; grant the API's runtime role access in a
--     permissions script, as 002_sms_api_permissions.sql does for sms.

BEGIN;

CREATE SCHEMA IF NOT EXISTS inventory;

-- =====================================================================
-- Document numbers: PUR-2026-000001, SAL-2026-000001, ...
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.document_sequences
(
    document_type text NOT NULL,
    year          int NOT NULL,
    last_number   int NOT NULL DEFAULT 0 CHECK (last_number >= 0),
    PRIMARY KEY (document_type, year)
);

-- Gapless per type and year: the counter row stays locked until the inserting transaction
-- ends, and a rollback gives the number back.
CREATE OR REPLACE FUNCTION inventory.next_document_no(p_document_type text, p_date date DEFAULT CURRENT_DATE)
RETURNS text
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_prefix text;
    v_year   int := extract(year FROM p_date)::int;
    v_number int;
BEGIN
    v_prefix := CASE p_document_type
                    WHEN 'purchase' THEN 'PUR'
                    WHEN 'purchase_return' THEN 'PRT'
                    WHEN 'sale' THEN 'SAL'
                    WHEN 'sales_return' THEN 'SRT'
                    WHEN 'damage' THEN 'DMG'
                    WHEN 'adjustment' THEN 'ADJ'
                    WHEN 'payment' THEN 'PAY'
                END;
    IF v_prefix IS NULL THEN
        RAISE EXCEPTION 'Unknown document type %', p_document_type;
    END IF;

    INSERT INTO inventory.document_sequences AS s (document_type, year, last_number)
    VALUES (p_document_type, v_year, 1)
    ON CONFLICT (document_type, year) DO UPDATE SET last_number = s.last_number + 1
    RETURNING last_number INTO v_number;

    RETURN format('%s-%s-%s', v_prefix, v_year, lpad(v_number::text, 6, '0'));
END;
$function$;

-- =====================================================================
-- Master data
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.units
(
    unit_id        int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code           text NOT NULL,
    name           text NOT NULL,
    allow_fraction boolean NOT NULL DEFAULT false,
    is_active      boolean NOT NULL DEFAULT true,
    CONSTRAINT units_code_not_blank CHECK (length(btrim(code)) > 0),
    CONSTRAINT units_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_units_code ON inventory.units (lower(code));

INSERT INTO inventory.units (code, name, allow_fraction)
VALUES ('PCS', 'Piece', false),
       ('BOX', 'Box', false),
       ('ML', 'Millilitre', true)
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS inventory.categories
(
    category_id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    parent_id   int REFERENCES inventory.categories(category_id),
    name        text NOT NULL,
    is_active   boolean NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  int,
    CONSTRAINT categories_name_not_blank CHECK (length(btrim(name)) > 0),
    CONSTRAINT categories_not_own_parent CHECK (parent_id IS NULL OR parent_id <> category_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_categories_name
    ON inventory.categories (coalesce(parent_id, 0), lower(name));

CREATE TABLE IF NOT EXISTS inventory.brands
(
    brand_id   int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name       text NOT NULL,
    is_active  boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by int,
    CONSTRAINT brands_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_brands_name ON inventory.brands (lower(name));

CREATE TABLE IF NOT EXISTS inventory.products
(
    product_id     int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sku            text NOT NULL,
    barcode        text,
    name           text NOT NULL,
    description    text,
    category_id    int REFERENCES inventory.categories(category_id),
    brand_id       int REFERENCES inventory.brands(brand_id),
    unit_id        int NOT NULL REFERENCES inventory.units(unit_id),
    purchase_price numeric(14, 2) NOT NULL DEFAULT 0 CHECK (purchase_price >= 0),
    sale_price     numeric(14, 2) NOT NULL DEFAULT 0 CHECK (sale_price >= 0),
    tax_rate       numeric(5, 2) NOT NULL DEFAULT 0 CHECK (tax_rate BETWEEN 0 AND 100),
    reorder_level  numeric(14, 3) NOT NULL DEFAULT 0 CHECK (reorder_level >= 0),
    -- Batch (lot) tracking: when true every stock movement of the product needs a batch.
    track_batches  boolean NOT NULL DEFAULT false,
    is_active      boolean NOT NULL DEFAULT true,
    created_at     timestamptz NOT NULL DEFAULT now(),
    created_by     int,
    updated_at     timestamptz NOT NULL DEFAULT now(),
    updated_by     int,
    CONSTRAINT products_sku_not_blank CHECK (length(btrim(sku)) > 0),
    CONSTRAINT products_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_products_sku ON inventory.products (lower(sku));
CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_products_barcode
    ON inventory.products (barcode)
    WHERE barcode IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_products_category ON inventory.products (category_id);
CREATE INDEX IF NOT EXISTS ix_inventory_products_name ON inventory.products (lower(name));

CREATE TABLE IF NOT EXISTS inventory.product_batches
(
    batch_id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id      int NOT NULL REFERENCES inventory.products(product_id),
    batch_no        text NOT NULL,
    manufactured_on date,
    expires_on      date,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      int,
    -- Lets item and stock tables reference (product_id, batch_id), so a batch can only be
    -- used with its own product.
    CONSTRAINT product_batches_product_batch_key UNIQUE (product_id, batch_id),
    CONSTRAINT product_batches_no_not_blank CHECK (length(btrim(batch_no)) > 0),
    CONSTRAINT product_batches_dates CHECK (expires_on IS NULL OR manufactured_on IS NULL OR expires_on >= manufactured_on)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_product_batches_no
    ON inventory.product_batches (product_id, lower(batch_no));
CREATE INDEX IF NOT EXISTS ix_inventory_product_batches_expiry
    ON inventory.product_batches (expires_on)
    WHERE expires_on IS NOT NULL;

CREATE TABLE IF NOT EXISTS inventory.warehouses
(
    warehouse_id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code         text NOT NULL,
    name         text NOT NULL,
    address      text,
    phone        text,
    is_active    boolean NOT NULL DEFAULT true,
    created_at   timestamptz NOT NULL DEFAULT now(),
    created_by   int,
    updated_at   timestamptz NOT NULL DEFAULT now(),
    updated_by   int,
    CONSTRAINT warehouses_code_not_blank CHECK (length(btrim(code)) > 0),
    CONSTRAINT warehouses_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_warehouses_code ON inventory.warehouses (lower(code));

CREATE TABLE IF NOT EXISTS inventory.suppliers
(
    supplier_id     int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code            text NOT NULL,
    name            text NOT NULL,
    contact_person  text,
    phone           text,
    email           text,
    address         text,
    tax_number      text,
    -- What we already owed the supplier when the system went live.
    opening_balance numeric(14, 2) NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      int,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      int,
    CONSTRAINT suppliers_code_not_blank CHECK (length(btrim(code)) > 0),
    CONSTRAINT suppliers_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_suppliers_code ON inventory.suppliers (lower(code));
CREATE INDEX IF NOT EXISTS ix_inventory_suppliers_name ON inventory.suppliers (lower(name));

CREATE TABLE IF NOT EXISTS inventory.customers
(
    customer_id     int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code            text NOT NULL,
    name            text NOT NULL,
    phone           text,
    email           text,
    address         text,
    -- NULL means no credit limit.
    credit_limit    numeric(14, 2) CHECK (credit_limit IS NULL OR credit_limit >= 0),
    -- What the customer already owed us when the system went live.
    opening_balance numeric(14, 2) NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      int,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      int,
    CONSTRAINT customers_code_not_blank CHECK (length(btrim(code)) > 0),
    CONSTRAINT customers_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_customers_code ON inventory.customers (lower(code));
CREATE INDEX IF NOT EXISTS ix_inventory_customers_name ON inventory.customers (lower(name));
CREATE INDEX IF NOT EXISTS ix_inventory_customers_phone ON inventory.customers (phone) WHERE phone IS NOT NULL;

CREATE TABLE IF NOT EXISTS inventory.damage_reasons
(
    damage_reason_id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name             text NOT NULL,
    is_active        boolean NOT NULL DEFAULT true,
    CONSTRAINT damage_reasons_name_not_blank CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_damage_reasons_name ON inventory.damage_reasons (lower(name));

INSERT INTO inventory.damage_reasons (name)
VALUES ('Broken'), ('Leaked'), ('Expired'), ('Defective'), ('Lost or stolen')
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Stock: balances and the movement ledger
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.stock_balances
(
    stock_balance_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id       int NOT NULL REFERENCES inventory.products(product_id),
    warehouse_id     int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    batch_id         bigint,
    quantity         numeric(14, 3) NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    avg_cost         numeric(18, 6) NOT NULL DEFAULT 0 CHECK (avg_cost >= 0),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT stock_balances_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id),
    CONSTRAINT ux_inventory_stock_balances UNIQUE NULLS NOT DISTINCT (product_id, warehouse_id, batch_id)
);

CREATE INDEX IF NOT EXISTS ix_inventory_stock_balances_warehouse ON inventory.stock_balances (warehouse_id);

CREATE TABLE IF NOT EXISTS inventory.stock_movements
(
    movement_id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id       int NOT NULL REFERENCES inventory.products(product_id),
    warehouse_id     int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    batch_id         bigint,
    -- Also names the document table: purchase -> inventory.purchases, sale -> inventory.sales, ...
    movement_type    text NOT NULL CHECK (movement_type IN
                         ('purchase', 'purchase_return', 'sale', 'sales_return', 'damage', 'adjustment')),
    -- Positive into stock, negative out of stock.
    quantity         numeric(14, 3) NOT NULL CHECK (quantity <> 0),
    unit_cost        numeric(18, 6) NOT NULL CHECK (unit_cost >= 0),
    balance_after    numeric(14, 3) NOT NULL CHECK (balance_after >= 0),
    document_id      bigint NOT NULL,
    document_item_id bigint,
    -- Booked by cancel_document to undo an earlier movement of the same document.
    is_reversal      boolean NOT NULL DEFAULT false,
    movement_date    date NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       int,
    CONSTRAINT stock_movements_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id),
    CONSTRAINT stock_movements_direction CHECK
    (
        movement_type = 'adjustment'
        OR (movement_type IN ('purchase', 'sales_return') AND (quantity > 0) <> is_reversal)
        OR (movement_type IN ('purchase_return', 'sale', 'damage') AND (quantity < 0) <> is_reversal)
    )
);

CREATE INDEX IF NOT EXISTS ix_inventory_stock_movements_product
    ON inventory.stock_movements (product_id, warehouse_id, movement_date, movement_id);
CREATE INDEX IF NOT EXISTS ix_inventory_stock_movements_document
    ON inventory.stock_movements (movement_type, document_id);

CREATE OR REPLACE FUNCTION inventory.reject_stock_movement_change()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    RAISE EXCEPTION 'inventory.stock_movements is append-only; cancel the document to reverse its movements';
END;
$function$;

CREATE OR REPLACE TRIGGER stock_movements_append_only
    BEFORE UPDATE OR DELETE ON inventory.stock_movements
    FOR EACH ROW EXECUTE FUNCTION inventory.reject_stock_movement_change();

-- =====================================================================
-- Purchases and purchase returns
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.purchases
(
    purchase_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    purchase_no         text NOT NULL UNIQUE DEFAULT inventory.next_document_no('purchase'),
    supplier_id         int NOT NULL REFERENCES inventory.suppliers(supplier_id),
    warehouse_id        int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    purchase_date       date NOT NULL DEFAULT CURRENT_DATE,
    supplier_invoice_no text,
    status              text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted', 'cancelled')),
    -- Sum of the item line totals; recalculated when the purchase is posted.
    subtotal            numeric(14, 2) NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    discount_amount     numeric(14, 2) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    -- Freight, customs and similar costs on the whole invoice.
    other_charges       numeric(14, 2) NOT NULL DEFAULT 0 CHECK (other_charges >= 0),
    total_amount        numeric(14, 2) GENERATED ALWAYS AS (subtotal - discount_amount + other_charges) STORED,
    note                text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          int,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    updated_by          int,
    posted_at           timestamptz,
    posted_by           int,
    cancelled_at        timestamptz,
    cancelled_by        int,
    cancel_reason       text,
    CONSTRAINT purchases_total_not_negative CHECK (subtotal - discount_amount + other_charges >= 0),
    CONSTRAINT purchases_status_audit CHECK
    (
        (status = 'draft' AND posted_at IS NULL AND cancelled_at IS NULL)
        OR (status = 'posted' AND posted_at IS NOT NULL AND cancelled_at IS NULL)
        OR (status = 'cancelled' AND cancelled_at IS NOT NULL)
    )
);

-- The same supplier invoice can't be entered twice.
CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_purchases_supplier_invoice
    ON inventory.purchases (supplier_id, lower(supplier_invoice_no))
    WHERE supplier_invoice_no IS NOT NULL AND status <> 'cancelled';
CREATE INDEX IF NOT EXISTS ix_inventory_purchases_supplier_date
    ON inventory.purchases (supplier_id, purchase_date DESC);
CREATE INDEX IF NOT EXISTS ix_inventory_purchases_date ON inventory.purchases (purchase_date DESC);

CREATE TABLE IF NOT EXISTS inventory.purchase_items
(
    purchase_item_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    purchase_id      bigint NOT NULL REFERENCES inventory.purchases(purchase_id) ON DELETE CASCADE,
    product_id       int NOT NULL REFERENCES inventory.products(product_id),
    batch_id         bigint,
    quantity         numeric(14, 3) NOT NULL CHECK (quantity > 0),
    unit_cost        numeric(14, 2) NOT NULL CHECK (unit_cost >= 0),
    discount_amount  numeric(14, 2) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    tax_amount       numeric(14, 2) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    line_total       numeric(14, 2) GENERATED ALWAYS AS
                         (round(quantity * unit_cost, 2) - discount_amount + tax_amount) STORED,
    CONSTRAINT purchase_items_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id),
    CONSTRAINT purchase_items_discount CHECK (discount_amount <= round(quantity * unit_cost, 2))
);

CREATE INDEX IF NOT EXISTS ix_inventory_purchase_items_purchase ON inventory.purchase_items (purchase_id);
CREATE INDEX IF NOT EXISTS ix_inventory_purchase_items_product ON inventory.purchase_items (product_id);

CREATE TABLE IF NOT EXISTS inventory.purchase_returns
(
    purchase_return_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    return_no          text NOT NULL UNIQUE DEFAULT inventory.next_document_no('purchase_return'),
    -- The purchase being returned against, when the return covers a single purchase.
    purchase_id        bigint REFERENCES inventory.purchases(purchase_id),
    supplier_id        int NOT NULL REFERENCES inventory.suppliers(supplier_id),
    warehouse_id       int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    return_date        date NOT NULL DEFAULT CURRENT_DATE,
    reason             text,
    status             text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted', 'cancelled')),
    -- Sum of the item line totals; recalculated when the return is posted.
    total_amount       numeric(14, 2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    note               text,
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by         int,
    updated_at         timestamptz NOT NULL DEFAULT now(),
    updated_by         int,
    posted_at          timestamptz,
    posted_by          int,
    cancelled_at       timestamptz,
    cancelled_by       int,
    cancel_reason      text,
    CONSTRAINT purchase_returns_status_audit CHECK
    (
        (status = 'draft' AND posted_at IS NULL AND cancelled_at IS NULL)
        OR (status = 'posted' AND posted_at IS NOT NULL AND cancelled_at IS NULL)
        OR (status = 'cancelled' AND cancelled_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_inventory_purchase_returns_supplier_date
    ON inventory.purchase_returns (supplier_id, return_date DESC);
CREATE INDEX IF NOT EXISTS ix_inventory_purchase_returns_purchase
    ON inventory.purchase_returns (purchase_id)
    WHERE purchase_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS inventory.purchase_return_items
(
    purchase_return_item_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    purchase_return_id      bigint NOT NULL REFERENCES inventory.purchase_returns(purchase_return_id) ON DELETE CASCADE,
    -- The purchase line being returned; when set, posting checks the returnable quantity.
    purchase_item_id        bigint REFERENCES inventory.purchase_items(purchase_item_id),
    product_id              int NOT NULL REFERENCES inventory.products(product_id),
    batch_id                bigint,
    quantity                numeric(14, 3) NOT NULL CHECK (quantity > 0),
    -- Price credited by the supplier.
    unit_cost               numeric(14, 2) NOT NULL CHECK (unit_cost >= 0),
    tax_amount              numeric(14, 2) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    line_total              numeric(14, 2) GENERATED ALWAYS AS (round(quantity * unit_cost, 2) + tax_amount) STORED,
    CONSTRAINT purchase_return_items_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id)
);

CREATE INDEX IF NOT EXISTS ix_inventory_purchase_return_items_return
    ON inventory.purchase_return_items (purchase_return_id);
CREATE INDEX IF NOT EXISTS ix_inventory_purchase_return_items_purchase_item
    ON inventory.purchase_return_items (purchase_item_id)
    WHERE purchase_item_id IS NOT NULL;

-- =====================================================================
-- Sales and sales returns
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.sales
(
    sale_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sale_no         text NOT NULL UNIQUE DEFAULT inventory.next_document_no('sale'),
    -- NULL for a walk-in sale; walk_in_name/phone can then go on the receipt.
    customer_id     int REFERENCES inventory.customers(customer_id),
    walk_in_name    text,
    walk_in_phone   text,
    warehouse_id    int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    sale_date       date NOT NULL DEFAULT CURRENT_DATE,
    status          text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted', 'cancelled')),
    -- Sum of the item line totals; recalculated when the sale is posted.
    subtotal        numeric(14, 2) NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    discount_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    -- Delivery and similar charges on the whole invoice.
    other_charges   numeric(14, 2) NOT NULL DEFAULT 0 CHECK (other_charges >= 0),
    total_amount    numeric(14, 2) GENERATED ALWAYS AS (subtotal - discount_amount + other_charges) STORED,
    note            text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      int,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      int,
    posted_at       timestamptz,
    posted_by       int,
    cancelled_at    timestamptz,
    cancelled_by    int,
    cancel_reason   text,
    CONSTRAINT sales_total_not_negative CHECK (subtotal - discount_amount + other_charges >= 0),
    CONSTRAINT sales_walk_in CHECK (customer_id IS NULL OR (walk_in_name IS NULL AND walk_in_phone IS NULL)),
    CONSTRAINT sales_status_audit CHECK
    (
        (status = 'draft' AND posted_at IS NULL AND cancelled_at IS NULL)
        OR (status = 'posted' AND posted_at IS NOT NULL AND cancelled_at IS NULL)
        OR (status = 'cancelled' AND cancelled_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_inventory_sales_customer_date
    ON inventory.sales (customer_id, sale_date DESC)
    WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_sales_date ON inventory.sales (sale_date DESC);

CREATE TABLE IF NOT EXISTS inventory.sale_items
(
    sale_item_id    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sale_id         bigint NOT NULL REFERENCES inventory.sales(sale_id) ON DELETE CASCADE,
    product_id      int NOT NULL REFERENCES inventory.products(product_id),
    batch_id        bigint,
    quantity        numeric(14, 3) NOT NULL CHECK (quantity > 0),
    unit_price      numeric(14, 2) NOT NULL CHECK (unit_price >= 0),
    discount_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    tax_amount      numeric(14, 2) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    line_total      numeric(14, 2) GENERATED ALWAYS AS
                        (round(quantity * unit_price, 2) - discount_amount + tax_amount) STORED,
    -- Cost of goods sold per unit (the moving-average cost), filled in when the sale is posted.
    unit_cost       numeric(18, 6) CHECK (unit_cost IS NULL OR unit_cost >= 0),
    CONSTRAINT sale_items_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id),
    CONSTRAINT sale_items_discount CHECK (discount_amount <= round(quantity * unit_price, 2))
);

CREATE INDEX IF NOT EXISTS ix_inventory_sale_items_sale ON inventory.sale_items (sale_id);
CREATE INDEX IF NOT EXISTS ix_inventory_sale_items_product ON inventory.sale_items (product_id);

CREATE TABLE IF NOT EXISTS inventory.sales_returns
(
    sales_return_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    return_no       text NOT NULL UNIQUE DEFAULT inventory.next_document_no('sales_return'),
    -- The sale being returned against, when the return covers a single sale.
    sale_id         bigint REFERENCES inventory.sales(sale_id),
    customer_id     int REFERENCES inventory.customers(customer_id),
    warehouse_id    int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    return_date     date NOT NULL DEFAULT CURRENT_DATE,
    reason          text,
    status          text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted', 'cancelled')),
    -- Sum of the item line totals; recalculated when the return is posted.
    total_amount    numeric(14, 2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    note            text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      int,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      int,
    posted_at       timestamptz,
    posted_by       int,
    cancelled_at    timestamptz,
    cancelled_by    int,
    cancel_reason   text,
    CONSTRAINT sales_returns_status_audit CHECK
    (
        (status = 'draft' AND posted_at IS NULL AND cancelled_at IS NULL)
        OR (status = 'posted' AND posted_at IS NOT NULL AND cancelled_at IS NULL)
        OR (status = 'cancelled' AND cancelled_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_inventory_sales_returns_customer_date
    ON inventory.sales_returns (customer_id, return_date DESC)
    WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_sales_returns_sale
    ON inventory.sales_returns (sale_id)
    WHERE sale_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS inventory.sales_return_items
(
    sales_return_item_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sales_return_id      bigint NOT NULL REFERENCES inventory.sales_returns(sales_return_id) ON DELETE CASCADE,
    -- The sale line being returned; when set, posting checks the returnable quantity.
    sale_item_id         bigint REFERENCES inventory.sale_items(sale_item_id),
    product_id           int NOT NULL REFERENCES inventory.products(product_id),
    batch_id             bigint,
    quantity             numeric(14, 3) NOT NULL CHECK (quantity > 0),
    -- Price credited to the customer.
    unit_price           numeric(14, 2) NOT NULL CHECK (unit_price >= 0),
    tax_amount           numeric(14, 2) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    line_total           numeric(14, 2) GENERATED ALWAYS AS (round(quantity * unit_price, 2) + tax_amount) STORED,
    -- false: the returned goods are unsellable and do not go back into stock (a loss at unit_cost).
    restock              boolean NOT NULL DEFAULT true,
    -- Cost value of the returned goods, filled in when the return is posted.
    unit_cost            numeric(18, 6) CHECK (unit_cost IS NULL OR unit_cost >= 0),
    CONSTRAINT sales_return_items_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id)
);

CREATE INDEX IF NOT EXISTS ix_inventory_sales_return_items_return
    ON inventory.sales_return_items (sales_return_id);
CREATE INDEX IF NOT EXISTS ix_inventory_sales_return_items_sale_item
    ON inventory.sales_return_items (sale_item_id)
    WHERE sale_item_id IS NOT NULL;

-- =====================================================================
-- Damage and stock adjustments
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.damages
(
    damage_id     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    damage_no     text NOT NULL UNIQUE DEFAULT inventory.next_document_no('damage'),
    warehouse_id  int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    damage_date   date NOT NULL DEFAULT CURRENT_DATE,
    status        text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted', 'cancelled')),
    -- Cost value written off; calculated when the damage is posted.
    total_cost    numeric(14, 2) NOT NULL DEFAULT 0 CHECK (total_cost >= 0),
    note          text,
    created_at    timestamptz NOT NULL DEFAULT now(),
    created_by    int,
    updated_at    timestamptz NOT NULL DEFAULT now(),
    updated_by    int,
    posted_at     timestamptz,
    posted_by     int,
    cancelled_at  timestamptz,
    cancelled_by  int,
    cancel_reason text,
    CONSTRAINT damages_status_audit CHECK
    (
        (status = 'draft' AND posted_at IS NULL AND cancelled_at IS NULL)
        OR (status = 'posted' AND posted_at IS NOT NULL AND cancelled_at IS NULL)
        OR (status = 'cancelled' AND cancelled_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_inventory_damages_date ON inventory.damages (damage_date DESC);

CREATE TABLE IF NOT EXISTS inventory.damage_items
(
    damage_item_id   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    damage_id        bigint NOT NULL REFERENCES inventory.damages(damage_id) ON DELETE CASCADE,
    product_id       int NOT NULL REFERENCES inventory.products(product_id),
    batch_id         bigint,
    quantity         numeric(14, 3) NOT NULL CHECK (quantity > 0),
    damage_reason_id int NOT NULL REFERENCES inventory.damage_reasons(damage_reason_id),
    -- Moving-average cost written off per unit, filled in when the damage is posted.
    unit_cost        numeric(18, 6) CHECK (unit_cost IS NULL OR unit_cost >= 0),
    note             text,
    CONSTRAINT damage_items_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id)
);

CREATE INDEX IF NOT EXISTS ix_inventory_damage_items_damage ON inventory.damage_items (damage_id);
CREATE INDEX IF NOT EXISTS ix_inventory_damage_items_product ON inventory.damage_items (product_id);

-- Opening stock at go-live, corrections after a physical count, and other manual changes.
CREATE TABLE IF NOT EXISTS inventory.stock_adjustments
(
    adjustment_id   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    adjustment_no   text NOT NULL UNIQUE DEFAULT inventory.next_document_no('adjustment'),
    warehouse_id    int NOT NULL REFERENCES inventory.warehouses(warehouse_id),
    adjustment_date date NOT NULL DEFAULT CURRENT_DATE,
    adjustment_type text NOT NULL CHECK (adjustment_type IN ('opening', 'count', 'correction')),
    status          text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted', 'cancelled')),
    note            text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      int,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      int,
    posted_at       timestamptz,
    posted_by       int,
    cancelled_at    timestamptz,
    cancelled_by    int,
    cancel_reason   text,
    CONSTRAINT stock_adjustments_status_audit CHECK
    (
        (status = 'draft' AND posted_at IS NULL AND cancelled_at IS NULL)
        OR (status = 'posted' AND posted_at IS NOT NULL AND cancelled_at IS NULL)
        OR (status = 'cancelled' AND cancelled_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_inventory_stock_adjustments_date ON inventory.stock_adjustments (adjustment_date DESC);

CREATE TABLE IF NOT EXISTS inventory.stock_adjustment_items
(
    adjustment_item_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    adjustment_id      bigint NOT NULL REFERENCES inventory.stock_adjustments(adjustment_id) ON DELETE CASCADE,
    product_id         int NOT NULL REFERENCES inventory.products(product_id),
    batch_id           bigint,
    -- Positive adds stock, negative removes it (for a count: counted minus on hand).
    quantity_change    numeric(14, 3) NOT NULL CHECK (quantity_change <> 0),
    -- Required when adding stock; for removals it is filled in with the average cost on posting.
    unit_cost          numeric(18, 6) CHECK (unit_cost IS NULL OR unit_cost >= 0),
    note               text,
    CONSTRAINT stock_adjustment_items_batch_fk FOREIGN KEY (product_id, batch_id)
        REFERENCES inventory.product_batches (product_id, batch_id),
    CONSTRAINT stock_adjustment_items_cost CHECK (quantity_change < 0 OR unit_cost IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_inventory_stock_adjustment_items_adjustment
    ON inventory.stock_adjustment_items (adjustment_id);

-- =====================================================================
-- Payments: supplier payments, customer receipts and refunds
-- =====================================================================

CREATE TABLE IF NOT EXISTS inventory.payments
(
    payment_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    payment_no         text NOT NULL UNIQUE DEFAULT inventory.next_document_no('payment'),
    payment_date       date NOT NULL DEFAULT CURRENT_DATE,
    -- 'out': money we pay (to a supplier, or a refund to a customer).
    -- 'in':  money we receive (from a customer, or a refund from a supplier).
    direction          text NOT NULL CHECK (direction IN ('in', 'out')),
    supplier_id        int REFERENCES inventory.suppliers(supplier_id),
    customer_id        int REFERENCES inventory.customers(customer_id),
    -- The document it settles, if any (an advance has none).
    purchase_id        bigint REFERENCES inventory.purchases(purchase_id),
    purchase_return_id bigint REFERENCES inventory.purchase_returns(purchase_return_id),
    sale_id            bigint REFERENCES inventory.sales(sale_id),
    sales_return_id    bigint REFERENCES inventory.sales_returns(sales_return_id),
    method             text NOT NULL CHECK (method IN ('cash', 'bank', 'card', 'mobile_banking', 'cheque', 'other')),
    amount             numeric(14, 2) NOT NULL CHECK (amount > 0),
    reference          text,
    note               text,
    status             text NOT NULL DEFAULT 'posted' CHECK (status IN ('posted', 'cancelled')),
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by         int,
    cancelled_at       timestamptz,
    cancelled_by       int,
    cancel_reason      text,
    CONSTRAINT payments_one_party CHECK (num_nonnulls(supplier_id, customer_id) <= 1),
    CONSTRAINT payments_one_document CHECK (num_nonnulls(purchase_id, purchase_return_id, sale_id, sales_return_id) <= 1),
    CONSTRAINT payments_has_party_or_document CHECK
        (num_nonnulls(supplier_id, customer_id, purchase_id, purchase_return_id, sale_id, sales_return_id) > 0),
    CONSTRAINT payments_document_shape CHECK
    (
        (purchase_id IS NULL OR (direction = 'out' AND supplier_id IS NOT NULL))
        AND (purchase_return_id IS NULL OR (direction = 'in' AND supplier_id IS NOT NULL))
        AND (sale_id IS NULL OR (direction = 'in' AND supplier_id IS NULL))
        AND (sales_return_id IS NULL OR (direction = 'out' AND supplier_id IS NULL))
    ),
    CONSTRAINT payments_status_audit CHECK ((status = 'cancelled') = (cancelled_at IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_inventory_payments_supplier ON inventory.payments (supplier_id) WHERE supplier_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_payments_customer ON inventory.payments (customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_payments_purchase ON inventory.payments (purchase_id) WHERE purchase_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_payments_sale ON inventory.payments (sale_id) WHERE sale_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_inventory_payments_date ON inventory.payments (payment_date DESC);

-- =====================================================================
-- Stock functions
-- =====================================================================

-- The only writer of stock_balances and stock_movements. Locks the balance row, refuses to go
-- below zero, keeps the moving-average cost and appends the ledger row.
-- p_unit_cost: cost of incoming stock; NULL means the current average cost (used for stock
-- leaving at average cost). Returns the unit cost the movement was booked at.
CREATE OR REPLACE FUNCTION inventory.apply_stock_movement
(
    p_movement_type    text,
    p_document_id      bigint,
    p_document_item_id bigint,
    p_product_id       int,
    p_warehouse_id     int,
    p_batch_id         bigint,
    p_quantity         numeric,
    p_unit_cost        numeric,
    p_movement_date    date,
    p_user_id          int,
    p_is_reversal      boolean DEFAULT false
)
RETURNS numeric
LANGUAGE plpgsql
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_product        inventory.products%ROWTYPE;
    v_allow_fraction boolean;
    v_balance        inventory.stock_balances%ROWTYPE;
    v_new_quantity   numeric;
    v_cost           numeric;
BEGIN
    SELECT * INTO v_product FROM inventory.products WHERE product_id = p_product_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Product % does not exist', p_product_id;
    ELSIF v_product.track_batches AND p_batch_id IS NULL THEN
        RAISE EXCEPTION 'Product % is tracked by batch; choose a batch', v_product.sku;
    ELSIF NOT v_product.track_batches AND p_batch_id IS NOT NULL THEN
        RAISE EXCEPTION 'Product % is not tracked by batch', v_product.sku;
    END IF;

    SELECT allow_fraction INTO v_allow_fraction FROM inventory.units WHERE unit_id = v_product.unit_id;
    IF NOT v_allow_fraction AND p_quantity <> trunc(p_quantity) THEN
        RAISE EXCEPTION 'Quantity of % must be a whole number', v_product.sku;
    END IF;

    INSERT INTO inventory.stock_balances (product_id, warehouse_id, batch_id)
    VALUES (p_product_id, p_warehouse_id, p_batch_id)
    ON CONFLICT DO NOTHING;

    SELECT * INTO v_balance
    FROM inventory.stock_balances
    WHERE product_id = p_product_id
      AND warehouse_id = p_warehouse_id
      AND batch_id IS NOT DISTINCT FROM p_batch_id
    FOR UPDATE;

    v_new_quantity := v_balance.quantity + p_quantity;
    IF v_new_quantity < 0 THEN
        RAISE EXCEPTION 'Not enough stock of % in warehouse %: % on hand, % needed',
            v_product.sku, p_warehouse_id, v_balance.quantity, -p_quantity;
    END IF;

    -- Incoming stock blends its cost into the average. Stock leaving at the average leaves the
    -- average unchanged; a reversal takes stock out at the cost it came in at.
    v_cost := coalesce(p_unit_cost, v_balance.avg_cost);

    UPDATE inventory.stock_balances
       SET quantity = v_new_quantity,
           avg_cost = CASE
                          WHEN v_new_quantity = 0 THEN v_balance.avg_cost
                          ELSE greatest(round((v_balance.quantity * v_balance.avg_cost + p_quantity * v_cost)
                                              / v_new_quantity, 6), 0)
                      END,
           updated_at = now()
     WHERE stock_balance_id = v_balance.stock_balance_id;

    INSERT INTO inventory.stock_movements
        (product_id, warehouse_id, batch_id, movement_type, quantity, unit_cost, balance_after,
         document_id, document_item_id, is_reversal, movement_date, created_by)
    VALUES
        (p_product_id, p_warehouse_id, p_batch_id, p_movement_type, p_quantity, v_cost, v_new_quantity,
         p_document_id, p_document_item_id, p_is_reversal, p_movement_date, p_user_id);

    RETURN v_cost;
END;
$function$;

-- Posting functions lock the document, require 'draft', apply each line's stock movement and
-- mark it 'posted'. Lines are processed in (product, batch) order in every function, so
-- concurrent postings lock stock rows in the same order and cannot deadlock.

CREATE OR REPLACE FUNCTION inventory.post_purchase(p_purchase_id bigint, p_user_id int)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_purchase inventory.purchases%ROWTYPE;
    v_item     inventory.purchase_items%ROWTYPE;
BEGIN
    SELECT * INTO v_purchase FROM inventory.purchases WHERE purchase_id = p_purchase_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Purchase % does not exist', p_purchase_id;
    ELSIF v_purchase.status <> 'draft' THEN
        RAISE EXCEPTION 'Purchase % is %; only drafts can be posted', v_purchase.purchase_no, v_purchase.status;
    ELSIF NOT EXISTS (SELECT 1 FROM inventory.purchase_items WHERE purchase_id = p_purchase_id) THEN
        RAISE EXCEPTION 'Purchase % has no items', v_purchase.purchase_no;
    END IF;

    FOR v_item IN
        SELECT * FROM inventory.purchase_items
        WHERE purchase_id = p_purchase_id
        ORDER BY product_id, batch_id NULLS FIRST, purchase_item_id
    LOOP
        -- Stock is valued at the net unit cost: after the line discount, before tax.
        PERFORM inventory.apply_stock_movement(
            'purchase', p_purchase_id, v_item.purchase_item_id, v_item.product_id, v_purchase.warehouse_id,
            v_item.batch_id, v_item.quantity,
            (round(v_item.quantity * v_item.unit_cost, 2) - v_item.discount_amount) / v_item.quantity,
            v_purchase.purchase_date, p_user_id);
    END LOOP;

    UPDATE inventory.purchases
       SET status = 'posted',
           subtotal = (SELECT sum(line_total) FROM inventory.purchase_items WHERE purchase_id = p_purchase_id),
           posted_at = now(), posted_by = p_user_id,
           updated_at = now(), updated_by = p_user_id
     WHERE purchase_id = p_purchase_id;
END;
$function$;

CREATE OR REPLACE FUNCTION inventory.post_purchase_return(p_purchase_return_id bigint, p_user_id int)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_return inventory.purchase_returns%ROWTYPE;
    v_item   inventory.purchase_return_items%ROWTYPE;
    v_line   record;
BEGIN
    SELECT * INTO v_return FROM inventory.purchase_returns WHERE purchase_return_id = p_purchase_return_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Purchase return % does not exist', p_purchase_return_id;
    ELSIF v_return.status <> 'draft' THEN
        RAISE EXCEPTION 'Purchase return % is %; only drafts can be posted', v_return.return_no, v_return.status;
    ELSIF NOT EXISTS (SELECT 1 FROM inventory.purchase_return_items WHERE purchase_return_id = p_purchase_return_id) THEN
        RAISE EXCEPTION 'Purchase return % has no items', v_return.return_no;
    END IF;

    FOR v_item IN
        SELECT * FROM inventory.purchase_return_items
        WHERE purchase_return_id = p_purchase_return_id
        ORDER BY product_id, batch_id NULLS FIRST, purchase_return_item_id
    LOOP
        IF v_item.purchase_item_id IS NOT NULL THEN
            -- Lock the purchase line so two returns can't both take its last units.
            SELECT pi.purchase_id, pi.product_id, pi.batch_id,
                   pi.quantity - coalesce((
                       SELECT sum(ri.quantity)
                       FROM inventory.purchase_return_items ri
                       JOIN inventory.purchase_returns r ON r.purchase_return_id = ri.purchase_return_id
                       WHERE ri.purchase_item_id = pi.purchase_item_id AND r.status = 'posted'), 0) AS returnable
              INTO v_line
              FROM inventory.purchase_items pi
              JOIN inventory.purchases p ON p.purchase_id = pi.purchase_id
             WHERE pi.purchase_item_id = v_item.purchase_item_id AND p.status = 'posted'
               FOR UPDATE OF pi;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Return line % does not refer to a posted purchase line', v_item.purchase_return_item_id;
            ELSIF v_line.product_id <> v_item.product_id
                  OR v_line.batch_id IS DISTINCT FROM v_item.batch_id
                  OR (v_return.purchase_id IS NOT NULL AND v_line.purchase_id <> v_return.purchase_id) THEN
                RAISE EXCEPTION 'Return line % does not match its purchase line', v_item.purchase_return_item_id;
            ELSIF v_item.quantity > v_line.returnable THEN
                RAISE EXCEPTION 'Only % of purchase line % can still be returned', v_line.returnable, v_item.purchase_item_id;
            END IF;
        END IF;

        PERFORM inventory.apply_stock_movement(
            'purchase_return', p_purchase_return_id, v_item.purchase_return_item_id, v_item.product_id,
            v_return.warehouse_id, v_item.batch_id, -v_item.quantity, NULL, v_return.return_date, p_user_id);
    END LOOP;

    UPDATE inventory.purchase_returns
       SET status = 'posted',
           total_amount = (SELECT sum(line_total) FROM inventory.purchase_return_items
                           WHERE purchase_return_id = p_purchase_return_id),
           posted_at = now(), posted_by = p_user_id,
           updated_at = now(), updated_by = p_user_id
     WHERE purchase_return_id = p_purchase_return_id;
END;
$function$;

CREATE OR REPLACE FUNCTION inventory.post_sale(p_sale_id bigint, p_user_id int)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_sale inventory.sales%ROWTYPE;
    v_item inventory.sale_items%ROWTYPE;
    v_cost numeric;
BEGIN
    SELECT * INTO v_sale FROM inventory.sales WHERE sale_id = p_sale_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Sale % does not exist', p_sale_id;
    ELSIF v_sale.status <> 'draft' THEN
        RAISE EXCEPTION 'Sale % is %; only drafts can be posted', v_sale.sale_no, v_sale.status;
    ELSIF NOT EXISTS (SELECT 1 FROM inventory.sale_items WHERE sale_id = p_sale_id) THEN
        RAISE EXCEPTION 'Sale % has no items', v_sale.sale_no;
    END IF;

    FOR v_item IN
        SELECT * FROM inventory.sale_items
        WHERE sale_id = p_sale_id
        ORDER BY product_id, batch_id NULLS FIRST, sale_item_id
    LOOP
        v_cost := inventory.apply_stock_movement(
            'sale', p_sale_id, v_item.sale_item_id, v_item.product_id, v_sale.warehouse_id,
            v_item.batch_id, -v_item.quantity, NULL, v_sale.sale_date, p_user_id);
        UPDATE inventory.sale_items SET unit_cost = v_cost WHERE sale_item_id = v_item.sale_item_id;
    END LOOP;

    UPDATE inventory.sales
       SET status = 'posted',
           subtotal = (SELECT sum(line_total) FROM inventory.sale_items WHERE sale_id = p_sale_id),
           posted_at = now(), posted_by = p_user_id,
           updated_at = now(), updated_by = p_user_id
     WHERE sale_id = p_sale_id;
END;
$function$;

CREATE OR REPLACE FUNCTION inventory.post_sales_return(p_sales_return_id bigint, p_user_id int)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_return inventory.sales_returns%ROWTYPE;
    v_item   inventory.sales_return_items%ROWTYPE;
    v_line   record;
    v_cost   numeric;
BEGIN
    SELECT * INTO v_return FROM inventory.sales_returns WHERE sales_return_id = p_sales_return_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Sales return % does not exist', p_sales_return_id;
    ELSIF v_return.status <> 'draft' THEN
        RAISE EXCEPTION 'Sales return % is %; only drafts can be posted', v_return.return_no, v_return.status;
    ELSIF NOT EXISTS (SELECT 1 FROM inventory.sales_return_items WHERE sales_return_id = p_sales_return_id) THEN
        RAISE EXCEPTION 'Sales return % has no items', v_return.return_no;
    END IF;

    FOR v_item IN
        SELECT * FROM inventory.sales_return_items
        WHERE sales_return_id = p_sales_return_id
        ORDER BY product_id, batch_id NULLS FIRST, sales_return_item_id
    LOOP
        IF v_item.sale_item_id IS NOT NULL THEN
            -- Lock the sale line so two returns can't both take its last units.
            SELECT si.sale_id, si.product_id, si.batch_id, si.unit_cost,
                   si.quantity - coalesce((
                       SELECT sum(ri.quantity)
                       FROM inventory.sales_return_items ri
                       JOIN inventory.sales_returns r ON r.sales_return_id = ri.sales_return_id
                       WHERE ri.sale_item_id = si.sale_item_id AND r.status = 'posted'), 0) AS returnable
              INTO v_line
              FROM inventory.sale_items si
              JOIN inventory.sales s ON s.sale_id = si.sale_id
             WHERE si.sale_item_id = v_item.sale_item_id AND s.status = 'posted'
               FOR UPDATE OF si;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Return line % does not refer to a posted sale line', v_item.sales_return_item_id;
            ELSIF v_line.product_id <> v_item.product_id
                  OR v_line.batch_id IS DISTINCT FROM v_item.batch_id
                  OR (v_return.sale_id IS NOT NULL AND v_line.sale_id <> v_return.sale_id) THEN
                RAISE EXCEPTION 'Return line % does not match its sale line', v_item.sales_return_item_id;
            ELSIF v_item.quantity > v_line.returnable THEN
                RAISE EXCEPTION 'Only % of sale line % can still be returned', v_line.returnable, v_item.sale_item_id;
            END IF;
            -- Goods come back at the cost they left with.
            v_cost := v_line.unit_cost;
        ELSE
            -- No sale line: value at the current average cost, or the product's purchase price.
            SELECT coalesce(
                       (SELECT b.avg_cost FROM inventory.stock_balances b
                        WHERE b.product_id = v_item.product_id AND b.warehouse_id = v_return.warehouse_id
                          AND b.batch_id IS NOT DISTINCT FROM v_item.batch_id AND b.quantity > 0),
                       p.purchase_price)
              INTO v_cost
              FROM inventory.products p
             WHERE p.product_id = v_item.product_id;
        END IF;

        IF v_item.restock THEN
            v_cost := inventory.apply_stock_movement(
                'sales_return', p_sales_return_id, v_item.sales_return_item_id, v_item.product_id,
                v_return.warehouse_id, v_item.batch_id, v_item.quantity, v_cost, v_return.return_date, p_user_id);
        END IF;

        UPDATE inventory.sales_return_items SET unit_cost = v_cost
         WHERE sales_return_item_id = v_item.sales_return_item_id;
    END LOOP;

    UPDATE inventory.sales_returns
       SET status = 'posted',
           total_amount = (SELECT sum(line_total) FROM inventory.sales_return_items
                           WHERE sales_return_id = p_sales_return_id),
           posted_at = now(), posted_by = p_user_id,
           updated_at = now(), updated_by = p_user_id
     WHERE sales_return_id = p_sales_return_id;
END;
$function$;

CREATE OR REPLACE FUNCTION inventory.post_damage(p_damage_id bigint, p_user_id int)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_damage inventory.damages%ROWTYPE;
    v_item   inventory.damage_items%ROWTYPE;
    v_cost   numeric;
BEGIN
    SELECT * INTO v_damage FROM inventory.damages WHERE damage_id = p_damage_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Damage % does not exist', p_damage_id;
    ELSIF v_damage.status <> 'draft' THEN
        RAISE EXCEPTION 'Damage % is %; only drafts can be posted', v_damage.damage_no, v_damage.status;
    ELSIF NOT EXISTS (SELECT 1 FROM inventory.damage_items WHERE damage_id = p_damage_id) THEN
        RAISE EXCEPTION 'Damage % has no items', v_damage.damage_no;
    END IF;

    FOR v_item IN
        SELECT * FROM inventory.damage_items
        WHERE damage_id = p_damage_id
        ORDER BY product_id, batch_id NULLS FIRST, damage_item_id
    LOOP
        v_cost := inventory.apply_stock_movement(
            'damage', p_damage_id, v_item.damage_item_id, v_item.product_id, v_damage.warehouse_id,
            v_item.batch_id, -v_item.quantity, NULL, v_damage.damage_date, p_user_id);
        UPDATE inventory.damage_items SET unit_cost = v_cost WHERE damage_item_id = v_item.damage_item_id;
    END LOOP;

    UPDATE inventory.damages
       SET status = 'posted',
           total_cost = (SELECT sum(round(quantity * unit_cost, 2)) FROM inventory.damage_items
                         WHERE damage_id = p_damage_id),
           posted_at = now(), posted_by = p_user_id,
           updated_at = now(), updated_by = p_user_id
     WHERE damage_id = p_damage_id;
END;
$function$;

CREATE OR REPLACE FUNCTION inventory.post_adjustment(p_adjustment_id bigint, p_user_id int)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_adjustment inventory.stock_adjustments%ROWTYPE;
    v_item       inventory.stock_adjustment_items%ROWTYPE;
    v_cost       numeric;
BEGIN
    SELECT * INTO v_adjustment FROM inventory.stock_adjustments WHERE adjustment_id = p_adjustment_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Stock adjustment % does not exist', p_adjustment_id;
    ELSIF v_adjustment.status <> 'draft' THEN
        RAISE EXCEPTION 'Stock adjustment % is %; only drafts can be posted', v_adjustment.adjustment_no, v_adjustment.status;
    ELSIF NOT EXISTS (SELECT 1 FROM inventory.stock_adjustment_items WHERE adjustment_id = p_adjustment_id) THEN
        RAISE EXCEPTION 'Stock adjustment % has no items', v_adjustment.adjustment_no;
    END IF;

    FOR v_item IN
        SELECT * FROM inventory.stock_adjustment_items
        WHERE adjustment_id = p_adjustment_id
        ORDER BY product_id, batch_id NULLS FIRST, adjustment_item_id
    LOOP
        v_cost := inventory.apply_stock_movement(
            'adjustment', p_adjustment_id, v_item.adjustment_item_id, v_item.product_id, v_adjustment.warehouse_id,
            v_item.batch_id, v_item.quantity_change,
            CASE WHEN v_item.quantity_change > 0 THEN v_item.unit_cost END,
            v_adjustment.adjustment_date, p_user_id);
        UPDATE inventory.stock_adjustment_items SET unit_cost = v_cost
         WHERE adjustment_item_id = v_item.adjustment_item_id;
    END LOOP;

    UPDATE inventory.stock_adjustments
       SET status = 'posted',
           posted_at = now(), posted_by = p_user_id,
           updated_at = now(), updated_by = p_user_id
     WHERE adjustment_id = p_adjustment_id;
END;
$function$;

-- Cancels a draft, or a posted document by booking reversing movements (each line's stock
-- goes back at the cost it was booked at). Fails if that would make stock negative, e.g.
-- cancelling a purchase whose goods were already sold.
-- p_document_type: purchase, purchase_return, sale, sales_return, damage or adjustment.
CREATE OR REPLACE FUNCTION inventory.cancel_document
(
    p_document_type text,
    p_document_id   bigint,
    p_user_id       int,
    p_reason        text DEFAULT NULL
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, inventory
AS $function$
DECLARE
    v_table        text;
    v_key          text;
    v_status       text;
    v_has_payments boolean := false;
    v_movement     inventory.stock_movements%ROWTYPE;
BEGIN
    SELECT d.table_name, d.key_column INTO v_table, v_key
    FROM (VALUES ('purchase', 'purchases', 'purchase_id'),
                 ('purchase_return', 'purchase_returns', 'purchase_return_id'),
                 ('sale', 'sales', 'sale_id'),
                 ('sales_return', 'sales_returns', 'sales_return_id'),
                 ('damage', 'damages', 'damage_id'),
                 ('adjustment', 'stock_adjustments', 'adjustment_id'))
         AS d(document_type, table_name, key_column)
    WHERE d.document_type = p_document_type;
    IF v_table IS NULL THEN
        RAISE EXCEPTION 'Unknown document type %', p_document_type;
    END IF;

    EXECUTE format('SELECT status FROM inventory.%I WHERE %I = $1 FOR UPDATE', v_table, v_key)
       INTO v_status
      USING p_document_id;
    IF v_status IS NULL THEN
        RAISE EXCEPTION '% % does not exist', p_document_type, p_document_id;
    ELSIF v_status = 'cancelled' THEN
        RAISE EXCEPTION '% % is already cancelled', p_document_type, p_document_id;
    END IF;

    -- Payment columns are named like the document keys (purchase_id, sale_id, ...).
    IF p_document_type IN ('purchase', 'purchase_return', 'sale', 'sales_return') THEN
        EXECUTE format('SELECT EXISTS (SELECT 1 FROM inventory.payments WHERE %I = $1 AND status = ''posted'')', v_key)
           INTO v_has_payments
          USING p_document_id;
        IF v_has_payments THEN
            RAISE EXCEPTION 'Cancel the payments of % % first', p_document_type, p_document_id;
        END IF;
    END IF;

    -- Returns posted against a purchase or sale must be cancelled first, or the returned
    -- quantity would be reversed twice.
    IF v_status = 'posted' AND p_document_type = 'purchase' AND EXISTS (
        SELECT 1 FROM inventory.purchase_returns r
        WHERE r.status = 'posted'
          AND (r.purchase_id = p_document_id OR EXISTS (
                SELECT 1 FROM inventory.purchase_return_items ri
                JOIN inventory.purchase_items pi ON pi.purchase_item_id = ri.purchase_item_id
                WHERE ri.purchase_return_id = r.purchase_return_id AND pi.purchase_id = p_document_id))) THEN
        RAISE EXCEPTION 'Cancel the posted returns of purchase % first', p_document_id;
    ELSIF v_status = 'posted' AND p_document_type = 'sale' AND EXISTS (
        SELECT 1 FROM inventory.sales_returns r
        WHERE r.status = 'posted'
          AND (r.sale_id = p_document_id OR EXISTS (
                SELECT 1 FROM inventory.sales_return_items ri
                JOIN inventory.sale_items si ON si.sale_item_id = ri.sale_item_id
                WHERE ri.sales_return_id = r.sales_return_id AND si.sale_id = p_document_id))) THEN
        RAISE EXCEPTION 'Cancel the posted returns of sale % first', p_document_id;
    END IF;

    IF v_status = 'posted' THEN
        FOR v_movement IN
            SELECT * FROM inventory.stock_movements
            WHERE movement_type = p_document_type AND document_id = p_document_id AND NOT is_reversal
            ORDER BY product_id, batch_id NULLS FIRST, movement_id
        LOOP
            PERFORM inventory.apply_stock_movement(
                v_movement.movement_type, v_movement.document_id, v_movement.document_item_id,
                v_movement.product_id, v_movement.warehouse_id, v_movement.batch_id,
                -v_movement.quantity, v_movement.unit_cost, CURRENT_DATE, p_user_id, true);
        END LOOP;
    END IF;

    EXECUTE format(
        'UPDATE inventory.%I
            SET status = ''cancelled'', cancelled_at = now(), cancelled_by = $2, cancel_reason = $3,
                updated_at = now(), updated_by = $2
          WHERE %I = $1', v_table, v_key)
      USING p_document_id, p_user_id, p_reason;
END;
$function$;

-- =====================================================================
-- Reporting views
-- =====================================================================

CREATE OR REPLACE VIEW inventory.stock_on_hand AS
SELECT b.product_id,
       p.sku,
       p.name AS product_name,
       b.warehouse_id,
       w.name AS warehouse_name,
       b.batch_id,
       pb.batch_no,
       pb.expires_on,
       b.quantity,
       b.avg_cost,
       round(b.quantity * b.avg_cost, 2) AS stock_value
FROM inventory.stock_balances b
JOIN inventory.products p ON p.product_id = b.product_id
JOIN inventory.warehouses w ON w.warehouse_id = b.warehouse_id
LEFT JOIN inventory.product_batches pb ON pb.batch_id = b.batch_id;

-- Active products at or below their reorder level, across all warehouses.
CREATE OR REPLACE VIEW inventory.low_stock AS
SELECT p.product_id,
       p.sku,
       p.name AS product_name,
       p.reorder_level,
       coalesce(sum(b.quantity), 0) AS quantity
FROM inventory.products p
LEFT JOIN inventory.stock_balances b ON b.product_id = p.product_id
WHERE p.is_active AND p.reorder_level > 0
GROUP BY p.product_id, p.sku, p.name, p.reorder_level
HAVING coalesce(sum(b.quantity), 0) <= p.reorder_level;

-- Per-invoice dues. Returns are settled at the party level (see supplier_balances and
-- customer_balances), not against individual invoices.
CREATE OR REPLACE VIEW inventory.purchase_dues AS
SELECT p.purchase_id,
       p.purchase_no,
       p.supplier_id,
       p.purchase_date,
       p.total_amount,
       coalesce(paid.amount, 0) AS paid_amount,
       p.total_amount - coalesce(paid.amount, 0) AS due_amount
FROM inventory.purchases p
LEFT JOIN (
    SELECT purchase_id, sum(amount) AS amount
    FROM inventory.payments
    WHERE status = 'posted' AND purchase_id IS NOT NULL
    GROUP BY purchase_id) paid ON paid.purchase_id = p.purchase_id
WHERE p.status = 'posted';

CREATE OR REPLACE VIEW inventory.sale_dues AS
SELECT s.sale_id,
       s.sale_no,
       s.customer_id,
       s.sale_date,
       s.total_amount,
       coalesce(paid.amount, 0) AS paid_amount,
       s.total_amount - coalesce(paid.amount, 0) AS due_amount
FROM inventory.sales s
LEFT JOIN (
    SELECT sale_id, sum(amount) AS amount
    FROM inventory.payments
    WHERE status = 'posted' AND sale_id IS NOT NULL
    GROUP BY sale_id) paid ON paid.sale_id = s.sale_id
WHERE s.status = 'posted';

-- What we owe each supplier (negative: the supplier owes us).
CREATE OR REPLACE VIEW inventory.supplier_balances AS
SELECT s.supplier_id,
       s.code,
       s.name,
       s.opening_balance
       + coalesce((SELECT sum(p.total_amount) FROM inventory.purchases p
                   WHERE p.supplier_id = s.supplier_id AND p.status = 'posted'), 0)
       - coalesce((SELECT sum(r.total_amount) FROM inventory.purchase_returns r
                   WHERE r.supplier_id = s.supplier_id AND r.status = 'posted'), 0)
       - coalesce((SELECT sum(pay.amount) FROM inventory.payments pay
                   WHERE pay.supplier_id = s.supplier_id AND pay.status = 'posted' AND pay.direction = 'out'), 0)
       + coalesce((SELECT sum(pay.amount) FROM inventory.payments pay
                   WHERE pay.supplier_id = s.supplier_id AND pay.status = 'posted' AND pay.direction = 'in'), 0)
       AS balance_due
FROM inventory.suppliers s;

-- What each customer owes us (negative: we owe the customer). Walk-in sales are not included.
CREATE OR REPLACE VIEW inventory.customer_balances AS
SELECT c.customer_id,
       c.code,
       c.name,
       c.credit_limit,
       c.opening_balance
       + coalesce((SELECT sum(s.total_amount) FROM inventory.sales s
                   WHERE s.customer_id = c.customer_id AND s.status = 'posted'), 0)
       - coalesce((SELECT sum(r.total_amount) FROM inventory.sales_returns r
                   WHERE r.customer_id = c.customer_id AND r.status = 'posted'), 0)
       - coalesce((SELECT sum(pay.amount) FROM inventory.payments pay
                   WHERE pay.customer_id = c.customer_id AND pay.status = 'posted' AND pay.direction = 'in'), 0)
       + coalesce((SELECT sum(pay.amount) FROM inventory.payments pay
                   WHERE pay.customer_id = c.customer_id AND pay.status = 'posted' AND pay.direction = 'out'), 0)
       AS balance_due
FROM inventory.customers c;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA inventory FROM PUBLIC;

COMMIT;
