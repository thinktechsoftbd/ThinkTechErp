-- Sales: customers, sales invoices (cash or credit), sales returns and money received from
-- customers. Builds on 009 (products, warehouses, the stock ledger, taxes, document numbers);
-- 014 adds the permissions and menus.
--
-- Cash or credit, as in Purchase:
--   Cash:   paid in full at the counter (paid_amount = total_amount). The customer may be a walk-in
--           one, named on the memo only (customer_name, customer_mobile).
--   Credit: all or part is paid later (baki), so the customer must be on file: posting adds the
--           unpaid part to their dues (customer_ledger), settled later by customer receipts. The API
--           also refuses a credit sale that takes the customer past credit_limit; it locks the
--           customer row (SELECT ... FOR UPDATE) first, so two counters can't both use the last credit.
-- Posting an invoice takes the stock out (stock_movement 'Sale', at the average cost, which is also
-- kept on the line as unit_cost for profit reports); posting a return puts it back ('SaleReturn').
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE SCHEMA IF NOT EXISTS sales;

-- Customers are grouped with a lookup list, managed under Settings -> Lookup Data.
INSERT INTO public.lookup_table (lookup_table_name)
VALUES ('Customer Group')
ON CONFLICT DO NOTHING;

INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name)
SELECT t.lookup_table_id, v.name
FROM public.lookup_table t
CROSS JOIN (VALUES ('Retail'), ('Wholesale'), ('Dealer / Reseller'), ('Corporate'),
                   ('Institution')) AS v(name)
WHERE lower(t.lookup_table_name) = 'customer group'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Customers
-- =====================================================================

CREATE TABLE IF NOT EXISTS sales.customer
(
    customer_id                   INT GENERATED ALWAYS AS IDENTITY,
    object_id                     uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id               INT NOT NULL,
    customer_code                 VARCHAR(30) NOT NULL,
    customer_name                 VARCHAR(250) NOT NULL,
    customer_name_local           VARCHAR(250) NULL,
    customer_group_lookup_data_id BIGINT NULL,   -- lookup "Customer Group"
    contact_person                VARCHAR(150) NULL,
    -- Shops look customers up by mobile number.
    mobile                        VARCHAR(30) NULL,
    phone                         VARCHAR(30) NULL,
    email                         VARCHAR(150) NULL,
    address_line1                 VARCHAR(250) NULL,
    address_line2                 VARCHAR(250) NULL,
    city                          VARCHAR(100) NULL,
    state                         VARCHAR(100) NULL,
    postal_code                   VARCHAR(20) NULL,
    country_code                  CHAR(2) NULL,  -- ISO 3166-1 alpha-2
    -- Identity document of a credit customer (Bangladesh: NID; elsewhere national ID or passport).
    national_id_no                VARCHAR(30) NULL,
    -- VAT/GST registration of a business customer (Bangladesh: BIN).
    tax_registration_no           VARCHAR(50) NULL,
    payment_terms_days            INT NOT NULL DEFAULT 0,
    -- The most the customer may owe at once, in the base currency. NULL = no limit, 0 = cash only.
    credit_limit                  NUMERIC(18, 4) NULL,
    -- Owed by the customer (+) or paid in advance by them (-) at go-live, in the base currency.
    opening_balance               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    opening_balance_date          DATE NULL,
    remarks                       VARCHAR(1000) NULL,
    is_active                     BOOLEAN NOT NULL DEFAULT TRUE,

    created_at                    TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                    INT NULL,
    updated_at                    TIMESTAMP(3) NULL,
    updated_by                    INT NULL,

    CONSTRAINT pk_customer PRIMARY KEY (customer_id),
    CONSTRAINT fk_customer_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_customer_group
        FOREIGN KEY (customer_group_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ck_customer_terms CHECK (payment_terms_days >= 0),
    CONSTRAINT ck_customer_credit_limit CHECK (credit_limit IS NULL OR credit_limit >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_object_id ON sales.customer (object_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_code
    ON sales.customer (organization_id, lower(customer_code));
CREATE INDEX IF NOT EXISTS idx_customer_name
    ON sales.customer (organization_id, lower(customer_name));
CREATE INDEX IF NOT EXISTS idx_customer_mobile
    ON sales.customer (organization_id, mobile) WHERE mobile IS NOT NULL;

-- =====================================================================
-- Sales invoice
-- =====================================================================

CREATE TABLE IF NOT EXISTS sales.sales_invoice
(
    sales_invoice_id        BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    invoice_no              VARCHAR(40) NOT NULL,
    invoice_date            DATE NOT NULL,
    sale_type               VARCHAR(10) NOT NULL DEFAULT 'Cash',
    due_date                DATE NULL,           -- credit: invoice_date + the customer's payment terms
    customer_id             INT NULL,            -- required for Credit
    -- A walk-in customer on a cash sale, for the memo; otherwise taken from the customer.
    customer_name           VARCHAR(250) NULL,
    customer_mobile         VARCHAR(30) NULL,
    warehouse_id            INT NOT NULL,
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    subtotal_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    discount_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,   -- line discounts + extra_discount_amount
    -- A discount on the whole bill, taken off the total after tax ("less" on a memo).
    extra_discount_amount   NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    charge_amount           NUMERIC(18, 4) NOT NULL DEFAULT 0,  -- delivery, labour, ...
    round_off_amount        NUMERIC(18, 4) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Paid at the counter with the sale: all of it for Cash, any part (or nothing) for Credit.
    -- A cheque is recorded as a customer receipt instead, because it can bounce.
    paid_amount             NUMERIC(18, 4) NOT NULL DEFAULT 0,
    payment_method          VARCHAR(20) NULL,
    payment_provider        VARCHAR(100) NULL,   -- bank or wallet name
    payment_reference_no    VARCHAR(100) NULL,   -- transaction ID
    -- paid_amount plus what later receipts and returns settled (invoice_allocation), document currency.
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

    CONSTRAINT pk_sales_invoice PRIMARY KEY (sales_invoice_id),
    CONSTRAINT fk_sales_invoice_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_sales_invoice_customer
        FOREIGN KEY (customer_id) REFERENCES sales.customer (customer_id),
    CONSTRAINT fk_sales_invoice_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_sales_invoice_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_sales_invoice_no UNIQUE (organization_id, invoice_no),
    CONSTRAINT ck_sales_invoice_type CHECK (sale_type IN ('Cash', 'Credit')),
    CONSTRAINT ck_sales_invoice_credit_customer CHECK (sale_type = 'Cash' OR customer_id IS NOT NULL),
    CONSTRAINT ck_sales_invoice_cash_paid CHECK (sale_type = 'Credit' OR paid_amount = total_amount),
    CONSTRAINT ck_sales_invoice_paid
        CHECK (paid_amount BETWEEN 0 AND total_amount AND (paid_amount = 0 OR payment_method IS NOT NULL)),
    CONSTRAINT ck_sales_invoice_payment_method
        CHECK (payment_method IN ('Cash', 'Bank', 'MobileWallet', 'Card', 'Other')),
    CONSTRAINT ck_sales_invoice_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_sales_invoice_settled
        CHECK (settled_amount >= paid_amount AND settled_amount <= total_amount),
    CONSTRAINT ck_sales_invoice_status CHECK (status IN ('Draft', 'Posted', 'Cancelled')),
    CONSTRAINT ck_sales_invoice_payment_status
        CHECK (payment_status IN ('Unpaid', 'PartiallyPaid', 'Paid'))
);

-- Added after the first release of this script.
ALTER TABLE sales.sales_invoice
    ADD COLUMN IF NOT EXISTS extra_discount_amount NUMERIC(18, 4) NOT NULL DEFAULT 0;
ALTER TABLE sales.sales_invoice DROP CONSTRAINT IF EXISTS ck_sales_invoice_extra_discount;
ALTER TABLE sales.sales_invoice
    ADD CONSTRAINT ck_sales_invoice_extra_discount CHECK (extra_discount_amount >= 0);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_invoice_object_id ON sales.sales_invoice (object_id);
CREATE INDEX IF NOT EXISTS idx_sales_invoice_date
    ON sales.sales_invoice (organization_id, invoice_date);
CREATE INDEX IF NOT EXISTS idx_sales_invoice_customer
    ON sales.sales_invoice (customer_id, invoice_date) WHERE customer_id IS NOT NULL;
-- Receivables due: unpaid credit invoices by due date.
CREATE INDEX IF NOT EXISTS idx_sales_invoice_due
    ON sales.sales_invoice (organization_id, due_date)
    WHERE status = 'Posted' AND payment_status <> 'Paid';

CREATE TABLE IF NOT EXISTS sales.sales_invoice_line
(
    sales_invoice_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    sales_invoice_id      BIGINT NOT NULL,
    line_no               INT NOT NULL,
    product_id            INT NOT NULL,
    description           VARCHAR(500) NULL,
    unit_id               INT NOT NULL,
    conversion_factor     NUMERIC(18, 6) NOT NULL,
    quantity              NUMERIC(18, 4) NOT NULL,
    base_quantity         NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    -- The lot sold from (first to expire first, for batch-tracked products).
    batch_id              BIGINT NULL,
    unit_price            NUMERIC(18, 6) NOT NULL DEFAULT 0,   -- per unit_id
    discount_percent      NUMERIC(9, 4) NOT NULL DEFAULT 0,
    discount_amount       NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_group_id          INT NULL,
    tax_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_total            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Average cost per base unit when posted, base currency: the cost of goods sold.
    unit_cost             NUMERIC(18, 6) NOT NULL DEFAULT 0,
    remarks               VARCHAR(500) NULL,

    CONSTRAINT pk_sales_invoice_line PRIMARY KEY (sales_invoice_line_id),
    CONSTRAINT fk_sales_invoice_line_invoice
        FOREIGN KEY (sales_invoice_id) REFERENCES sales.sales_invoice (sales_invoice_id) ON DELETE CASCADE,
    CONSTRAINT fk_sales_invoice_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_sales_invoice_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_sales_invoice_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT fk_sales_invoice_line_tax_group
        FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ux_sales_invoice_line UNIQUE (sales_invoice_id, line_no),
    CONSTRAINT ck_sales_invoice_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_sales_invoice_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0 AND unit_cost >= 0
               AND discount_percent BETWEEN 0 AND 100)
);

CREATE INDEX IF NOT EXISTS idx_sales_invoice_line_product ON sales.sales_invoice_line (product_id);

-- Added after the first release: units (of unit_id) brought back on posted sales returns, so a line
-- can't be returned twice.
ALTER TABLE sales.sales_invoice_line
    ADD COLUMN IF NOT EXISTS returned_quantity NUMERIC(18, 4) NOT NULL DEFAULT 0;
ALTER TABLE sales.sales_invoice_line DROP CONSTRAINT IF EXISTS ck_sales_invoice_line_returned;
ALTER TABLE sales.sales_invoice_line
    ADD CONSTRAINT ck_sales_invoice_line_returned CHECK (returned_quantity >= 0);

-- Each tax on an invoice line, for the sales VAT register (Bangladesh: Mushak-6.2) and tax returns.
CREATE TABLE IF NOT EXISTS sales.sales_invoice_line_tax
(
    sales_invoice_line_id BIGINT NOT NULL,
    tax_rate_id           INT NOT NULL,
    taxable_amount        NUMERIC(18, 4) NOT NULL,
    tax_amount            NUMERIC(18, 4) NOT NULL,

    CONSTRAINT pk_sales_invoice_line_tax PRIMARY KEY (sales_invoice_line_id, tax_rate_id),
    CONSTRAINT fk_sales_invoice_line_tax_line
        FOREIGN KEY (sales_invoice_line_id) REFERENCES sales.sales_invoice_line (sales_invoice_line_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_sales_invoice_line_tax_rate
        FOREIGN KEY (tax_rate_id) REFERENCES public.tax_rate (tax_rate_id)
);

-- The serial-tracked items an invoice line sold (for warranty claims).
CREATE TABLE IF NOT EXISTS sales.sales_invoice_serial
(
    sales_invoice_line_id BIGINT NOT NULL,
    serial_number_id      BIGINT NOT NULL,

    CONSTRAINT pk_sales_invoice_serial PRIMARY KEY (sales_invoice_line_id, serial_number_id),
    CONSTRAINT fk_sales_invoice_serial_line
        FOREIGN KEY (sales_invoice_line_id) REFERENCES sales.sales_invoice_line (sales_invoice_line_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_sales_invoice_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

-- =====================================================================
-- Sales return
-- =====================================================================

-- Goods a customer brings back (credit note). Posting puts the stock back, and either:
--   AdjustDue: the value is deducted from what the customer owes (on credit), so the customer must
--              be on file;
--   Refund:    the money is given back at the counter (refund_* columns), e.g. for a cash sale.
CREATE TABLE IF NOT EXISTS sales.sales_return
(
    sales_return_id         BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    return_no               VARCHAR(40) NOT NULL,
    return_date             DATE NOT NULL,
    customer_id             INT NULL,            -- required for AdjustDue
    customer_name           VARCHAR(250) NULL,   -- a walk-in customer, for the memo
    customer_mobile         VARCHAR(30) NULL,
    warehouse_id            INT NOT NULL,
    sales_invoice_id        BIGINT NULL,
    reason                  VARCHAR(20) NOT NULL,
    settlement_type         VARCHAR(10) NOT NULL DEFAULT 'Refund',
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

    CONSTRAINT pk_sales_return PRIMARY KEY (sales_return_id),
    CONSTRAINT fk_sales_return_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_sales_return_customer
        FOREIGN KEY (customer_id) REFERENCES sales.customer (customer_id),
    CONSTRAINT fk_sales_return_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES inventory.warehouse (warehouse_id),
    CONSTRAINT fk_sales_return_invoice
        FOREIGN KEY (sales_invoice_id) REFERENCES sales.sales_invoice (sales_invoice_id),
    CONSTRAINT fk_sales_return_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_sales_return_no UNIQUE (organization_id, return_no),
    CONSTRAINT ck_sales_return_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_sales_return_reason
        CHECK (reason IN ('Damaged', 'Expired', 'WrongItem', 'NotNeeded', 'QualityIssue', 'Other')),
    CONSTRAINT ck_sales_return_settlement CHECK (settlement_type IN ('AdjustDue', 'Refund')),
    CONSTRAINT ck_sales_return_due_customer CHECK (settlement_type = 'Refund' OR customer_id IS NOT NULL),
    CONSTRAINT ck_sales_return_refund_method
        CHECK (refund_method IS NULL OR (settlement_type = 'Refund'
                                         AND refund_method IN ('Cash', 'Bank', 'MobileWallet', 'Card', 'Other'))),
    CONSTRAINT ck_sales_return_status CHECK (status IN ('Draft', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_return_object_id ON sales.sales_return (object_id);
CREATE INDEX IF NOT EXISTS idx_sales_return_customer
    ON sales.sales_return (customer_id, return_date) WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_sales_return_invoice
    ON sales.sales_return (sales_invoice_id) WHERE sales_invoice_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS sales.sales_return_line
(
    sales_return_line_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    sales_return_id       BIGINT NOT NULL,
    line_no               INT NOT NULL,
    sales_invoice_line_id BIGINT NULL,
    product_id            INT NOT NULL,
    unit_id               INT NOT NULL,
    conversion_factor     NUMERIC(18, 6) NOT NULL,
    quantity              NUMERIC(18, 4) NOT NULL,
    base_quantity         NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    batch_id              BIGINT NULL,
    unit_price            NUMERIC(18, 6) NOT NULL DEFAULT 0,
    discount_amount       NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_group_id          INT NULL,
    tax_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_total            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    -- Cost the stock comes back at: the original sale line's unit_cost, else the current average.
    unit_cost             NUMERIC(18, 6) NOT NULL DEFAULT 0,
    -- False for damaged goods that can't be sold again (posted straight to a Damage adjustment).
    is_restockable        BOOLEAN NOT NULL DEFAULT TRUE,
    remarks               VARCHAR(500) NULL,

    CONSTRAINT pk_sales_return_line PRIMARY KEY (sales_return_line_id),
    CONSTRAINT fk_sales_return_line_return
        FOREIGN KEY (sales_return_id) REFERENCES sales.sales_return (sales_return_id) ON DELETE CASCADE,
    CONSTRAINT fk_sales_return_line_invoice_line
        FOREIGN KEY (sales_invoice_line_id) REFERENCES sales.sales_invoice_line (sales_invoice_line_id),
    CONSTRAINT fk_sales_return_line_product
        FOREIGN KEY (product_id) REFERENCES inventory.product (product_id),
    CONSTRAINT fk_sales_return_line_unit
        FOREIGN KEY (unit_id) REFERENCES inventory.unit (unit_id),
    CONSTRAINT fk_sales_return_line_batch
        FOREIGN KEY (batch_id) REFERENCES inventory.batch (batch_id),
    CONSTRAINT fk_sales_return_line_tax_group
        FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ux_sales_return_line UNIQUE (sales_return_id, line_no),
    CONSTRAINT ck_sales_return_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_sales_return_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0 AND unit_cost >= 0)
);

CREATE TABLE IF NOT EXISTS sales.sales_return_serial
(
    sales_return_line_id BIGINT NOT NULL,
    serial_number_id     BIGINT NOT NULL,

    CONSTRAINT pk_sales_return_serial PRIMARY KEY (sales_return_line_id, serial_number_id),
    CONSTRAINT fk_sales_return_serial_line
        FOREIGN KEY (sales_return_line_id) REFERENCES sales.sales_return_line (sales_return_line_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_sales_return_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

-- =====================================================================
-- Money received from customers
-- =====================================================================

-- Collection of dues after the sale, or a refund to the customer (e.g. an unused advance). A receipt
-- not allocated to invoices is an advance.
CREATE TABLE IF NOT EXISTS sales.customer_receipt
(
    customer_receipt_id     BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT NOT NULL,
    receipt_no              VARCHAR(40) NOT NULL,
    receipt_date            DATE NOT NULL,
    customer_id             INT NOT NULL,
    receipt_type            VARCHAR(10) NOT NULL DEFAULT 'Receipt',
    -- MobileWallet: bKash, Nagad, Rocket, PayPal, ... (payment_provider says which).
    payment_method          VARCHAR(20) NOT NULL,
    payment_provider        VARCHAR(100) NULL,
    reference_no            VARCHAR(100) NULL,   -- transaction ID, cheque or transfer number
    cheque_date             DATE NULL,
    cheque_status           VARCHAR(10) NULL,
    currency_lookup_data_id BIGINT NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    amount                  NUMERIC(18, 4) NOT NULL,
    -- Tax the customer withheld and paid to the government for us (Bangladesh: VDS / AIT; elsewhere
    -- TDS or withholding tax). It settles the customer's dues just like cash.
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

    CONSTRAINT pk_customer_receipt PRIMARY KEY (customer_receipt_id),
    CONSTRAINT fk_customer_receipt_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_customer_receipt_customer
        FOREIGN KEY (customer_id) REFERENCES sales.customer (customer_id),
    CONSTRAINT fk_customer_receipt_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ux_customer_receipt_no UNIQUE (organization_id, receipt_no),
    CONSTRAINT ck_customer_receipt_type CHECK (receipt_type IN ('Receipt', 'Refund')),
    CONSTRAINT ck_customer_receipt_method
        CHECK (payment_method IN ('Cash', 'Bank', 'Cheque', 'MobileWallet', 'Card', 'Other')),
    CONSTRAINT ck_customer_receipt_cheque
        CHECK (cheque_status IS NULL OR (payment_method = 'Cheque'
                                         AND cheque_status IN ('Pending', 'Cleared', 'Bounced'))),
    CONSTRAINT ck_customer_receipt_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_customer_receipt_amount
        CHECK (amount > 0 AND tax_deducted_amount >= 0
               AND allocated_amount BETWEEN 0 AND amount + tax_deducted_amount),
    CONSTRAINT ck_customer_receipt_status CHECK (status IN ('Draft', 'Posted', 'Cancelled'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_receipt_object_id ON sales.customer_receipt (object_id);
CREATE INDEX IF NOT EXISTS idx_customer_receipt_customer
    ON sales.customer_receipt (customer_id, receipt_date);

-- Which invoices a receipt or a return settles, in the invoice's currency (allocate only between
-- documents in the same currency).
CREATE TABLE IF NOT EXISTS sales.invoice_allocation
(
    invoice_allocation_id BIGINT GENERATED ALWAYS AS IDENTITY,
    organization_id       INT NOT NULL,
    sales_invoice_id      BIGINT NOT NULL,
    customer_receipt_id   BIGINT NULL,
    sales_return_id       BIGINT NULL,
    allocation_date       DATE NOT NULL,
    amount                NUMERIC(18, 4) NOT NULL,

    created_at            TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by            INT NULL,

    CONSTRAINT pk_sales_invoice_allocation PRIMARY KEY (invoice_allocation_id),
    CONSTRAINT fk_sales_invoice_allocation_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_sales_invoice_allocation_invoice
        FOREIGN KEY (sales_invoice_id) REFERENCES sales.sales_invoice (sales_invoice_id),
    CONSTRAINT fk_sales_invoice_allocation_receipt
        FOREIGN KEY (customer_receipt_id) REFERENCES sales.customer_receipt (customer_receipt_id),
    CONSTRAINT fk_sales_invoice_allocation_return
        FOREIGN KEY (sales_return_id) REFERENCES sales.sales_return (sales_return_id),
    CONSTRAINT ck_sales_invoice_allocation_source
        CHECK (num_nonnulls(customer_receipt_id, sales_return_id) = 1),
    CONSTRAINT ck_sales_invoice_allocation_amount CHECK (amount > 0)
);

CREATE INDEX IF NOT EXISTS idx_sales_invoice_allocation_invoice
    ON sales.invoice_allocation (sales_invoice_id);
CREATE INDEX IF NOT EXISTS idx_sales_invoice_allocation_receipt
    ON sales.invoice_allocation (customer_receipt_id) WHERE customer_receipt_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_sales_invoice_allocation_return
    ON sales.invoice_allocation (sales_return_id) WHERE sales_return_id IS NOT NULL;

-- =====================================================================
-- Views
-- =====================================================================

-- Everything that changed what each customer on file owes, in the base currency, with the running
-- balance (+ = the customer owes us, - = advance). A cash sale to a customer on file shows as
-- Invoice and PaidOnInvoice, netting to zero.
CREATE OR REPLACE VIEW sales.customer_ledger AS
WITH entries AS
(
    SELECT c.organization_id, c.customer_id, c.opening_balance_date AS entry_date, 0 AS entry_order,
           'Opening' AS entry_type, NULL::bigint AS document_id, NULL::varchar AS document_no,
           greatest(c.opening_balance, 0)  AS debit_amount,
           greatest(-c.opening_balance, 0) AS credit_amount
    FROM sales.customer c
    WHERE c.opening_balance <> 0

    UNION ALL
    SELECT i.organization_id, i.customer_id, i.invoice_date, 1, 'Invoice', i.sales_invoice_id,
           i.invoice_no, round(i.total_amount * i.exchange_rate, 4), 0
    FROM sales.sales_invoice i
    WHERE i.status = 'Posted' AND i.customer_id IS NOT NULL

    UNION ALL
    SELECT i.organization_id, i.customer_id, i.invoice_date, 2, 'PaidOnInvoice', i.sales_invoice_id,
           i.invoice_no, 0, round(i.paid_amount * i.exchange_rate, 4)
    FROM sales.sales_invoice i
    WHERE i.status = 'Posted' AND i.customer_id IS NOT NULL AND i.paid_amount > 0

    UNION ALL
    SELECT r.organization_id, r.customer_id, r.return_date, 3, 'Return', r.sales_return_id,
           r.return_no, 0, round(r.total_amount * r.exchange_rate, 4)
    FROM sales.sales_return r
    WHERE r.status = 'Posted' AND r.customer_id IS NOT NULL

    UNION ALL
    SELECT r.organization_id, r.customer_id, r.return_date, 4, 'ReturnRefund', r.sales_return_id,
           r.return_no, round(r.total_amount * r.exchange_rate, 4), 0
    FROM sales.sales_return r
    WHERE r.status = 'Posted' AND r.customer_id IS NOT NULL AND r.settlement_type = 'Refund'

    UNION ALL
    SELECT p.organization_id, p.customer_id, p.receipt_date, 5, p.receipt_type, p.customer_receipt_id,
           p.receipt_no,
           round(CASE WHEN p.receipt_type = 'Refund' THEN p.amount * p.exchange_rate ELSE 0 END, 4),
           round(CASE WHEN p.receipt_type = 'Receipt' THEN (p.amount + p.tax_deducted_amount) * p.exchange_rate ELSE 0 END, 4)
    FROM sales.customer_receipt p
    WHERE p.status = 'Posted'
      AND NOT (p.payment_method = 'Cheque' AND p.cheque_status = 'Bounced')
)
SELECT e.organization_id,
       e.customer_id,
       e.entry_date,
       e.entry_type,
       e.document_id,
       e.document_no,
       e.debit_amount,
       e.credit_amount,
       sum(e.debit_amount - e.credit_amount) OVER (
           PARTITION BY e.customer_id
           ORDER BY e.entry_date NULLS FIRST, e.entry_order, e.document_id
           ROWS UNBOUNDED PRECEDING) AS balance
FROM entries e;

-- What each customer owes now and how much credit is left, for the credit check on a new sale.
CREATE OR REPLACE VIEW sales.customer_balance AS
SELECT c.organization_id,
       c.customer_id,
       coalesce(sum(l.debit_amount - l.credit_amount), 0) AS balance,
       c.credit_limit,
       c.credit_limit - coalesce(sum(l.debit_amount - l.credit_amount), 0) AS available_credit
FROM sales.customer c
LEFT JOIN sales.customer_ledger l ON l.customer_id = c.customer_id
GROUP BY c.organization_id, c.customer_id, c.credit_limit;

COMMIT;
