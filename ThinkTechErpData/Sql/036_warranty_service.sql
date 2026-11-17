-- Warranty & Service: one module for customers who bring a device to the shop, whether it is still under
-- warranty, out of warranty, or bought somewhere else.
--
--   * warranty_policy          Terms a product is sold with, e.g. "3 Years (1 Year Replacement)".
--                              product.warranty_policy_id picks a product's policy.
--   * warranty_registration    The warranty card of one sold unit (or of a non-serial invoice line): created when a
--                              sales invoice posts, with the policy's terms copied so later policy edits don't change
--                              what was sold. Also for units sold before go-live (Manual) and replacement units.
--   * service_job              The job card, the module's main screen: the customer, the device, the complaint,
--                              warranty or paid, diagnosis, approval, status, resolution and delivery.
--   * service_job_item         Parts and service charges on a job. Both are products (a Stock product for a part, a
--                              Service product for labor such as "OS installation"), so the job is billed through a
--                              normal sales invoice (service_job.sales_invoice_id): stock, payments and accounting
--                              work as for any sale. Warranty-covered lines are invoiced at 0.
--   * service_job_receipt      Advance payments for a job: customer receipts (Sales), later allocated to its invoice.
--   * service_supplier_claim   A faulty unit sent to the supplier, distributor or brand service center (RMA).
--   * service_job_status_history, service_job_attachment   The job's timeline and photos.
--
-- Document numbers come from public.document_sequence: ServiceJob (SJ-), WarrantyCard (WC-), SupplierClaim (RMA-).
-- Customers are public.supplier rows with is_customer (as on sales invoices); a walk-in customer can be recorded by
-- name and mobile instead. Timestamps are UTC. Run after 035. The script is idempotent.

BEGIN;

-- =====================================================================
-- Device types (lookup)
-- =====================================================================

INSERT INTO public.lookup_table (lookup_table_name)
VALUES ('Service Device Type')
ON CONFLICT DO NOTHING;

INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name)
SELECT t.lookup_table_id, v.name
FROM public.lookup_table t
CROSS JOIN (VALUES ('Laptop'), ('Desktop PC'), ('CC Camera'), ('Printer'), ('Mobile'), ('Other')) AS v(name)
WHERE lower(t.lookup_table_name) = 'service device type'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Warranty policies
-- =====================================================================

CREATE TABLE IF NOT EXISTS public.warranty_policy
(
    warranty_policy_id  INT GENERATED ALWAYS AS IDENTITY,
    organization_id     INT NOT NULL,
    policy_name         VARCHAR(150) NOT NULL,
    -- Total warranty: the shop repairs faults free of charge for this long after the sale.
    warranty_months     INT NOT NULL,
    -- The first part of it in which a faulty unit is replaced rather than repaired (0: no replacement).
    replacement_days    INT NOT NULL DEFAULT 0,
    covers_parts        BOOLEAN NOT NULL DEFAULT TRUE,
    covers_labor        BOOLEAN NOT NULL DEFAULT TRUE,
    -- Printed on the warranty card (e.g. "Physical, burn and liquid damage are not covered").
    terms               VARCHAR(2000) NULL,
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,

    created_at          TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by          INT NULL,
    updated_at          TIMESTAMP NULL,
    updated_by          INT NULL,

    CONSTRAINT pk_warranty_policy PRIMARY KEY (warranty_policy_id),
    CONSTRAINT fk_warranty_policy_organization
        FOREIGN KEY (organization_id) REFERENCES public.organization (organization_id),
    CONSTRAINT ck_warranty_policy_name CHECK (length(btrim(policy_name)) > 0),
    CONSTRAINT ck_warranty_policy_period
        CHECK (warranty_months > 0 AND replacement_days >= 0 AND replacement_days <= warranty_months * 31)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_policy_name
    ON public.warranty_policy (organization_id, lower(policy_name));

-- The policy a product is sold with (none: no warranty).
ALTER TABLE public.product ADD COLUMN IF NOT EXISTS warranty_policy_id INT NULL;

ALTER TABLE public.product DROP CONSTRAINT IF EXISTS fk_product_warranty_policy;
ALTER TABLE public.product
    ADD CONSTRAINT fk_product_warranty_policy
        FOREIGN KEY (warranty_policy_id) REFERENCES public.warranty_policy (warranty_policy_id);

CREATE INDEX IF NOT EXISTS ix_product_warranty_policy
    ON public.product (warranty_policy_id)
    WHERE warranty_policy_id IS NOT NULL;

-- =====================================================================
-- Warranty cards
-- =====================================================================

CREATE TABLE IF NOT EXISTS public.warranty_registration
(
    warranty_registration_id   BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id                  UUID NOT NULL DEFAULT gen_random_uuid(),
    organization_id            INT NOT NULL,
    warranty_no                VARCHAR(40) NOT NULL,
    -- Sale: from a posted sales invoice. Replacement: the unit given in place of a faulty one.
    -- Manual: a unit sold before the system went live (no invoice here).
    source                     VARCHAR(20) NOT NULL DEFAULT 'Sale',
    sales_invoice_id           BIGINT NULL,
    sales_invoice_line_id      BIGINT NULL,
    product_id                 INT NOT NULL,
    -- The unit, for a serial-tracked product.
    serial_number_id           BIGINT NULL,
    -- The serial as printed on the unit; for units not in serial_number (e.g. sold before go-live) the only one.
    serial_no                  VARCHAR(100) NULL,
    -- For a product without serials: how many units of the invoice line this card covers.
    quantity                   NUMERIC(18,4) NOT NULL DEFAULT 1,

    customer_id                INT NULL,
    customer_name              VARCHAR(250) NULL,
    customer_mobile            VARCHAR(30) NULL,

    -- Terms copied from the policy at the time of sale.
    warranty_policy_id         INT NULL,
    start_date                 DATE NOT NULL,
    replacement_end_date       DATE NULL,
    end_date                   DATE NOT NULL,
    covers_parts               BOOLEAN NOT NULL DEFAULT TRUE,
    covers_labor               BOOLEAN NOT NULL DEFAULT TRUE,
    terms                      VARCHAR(2000) NULL,

    -- Where the unit came from, for claims back to the supplier (copied from serial_number when known).
    supplier_id                INT NULL,
    supplier_warranty_end_date DATE NULL,

    -- For source = Replacement: the card of the unit it replaced (that card becomes Replaced).
    replaces_registration_id   BIGINT NULL,
    -- Active; Replaced (a replacement unit took over); Void (sale returned or cancelled, or warranty voided).
    status                     VARCHAR(20) NOT NULL DEFAULT 'Active',
    void_reason                VARCHAR(500) NULL,
    remarks                    VARCHAR(1000) NULL,

    created_at                 TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                 INT NULL,
    updated_at                 TIMESTAMP NULL,
    updated_by                 INT NULL,

    CONSTRAINT pk_warranty_registration PRIMARY KEY (warranty_registration_id),
    CONSTRAINT fk_warranty_registration_organization
        FOREIGN KEY (organization_id) REFERENCES public.organization (organization_id),
    CONSTRAINT fk_warranty_registration_invoice
        FOREIGN KEY (sales_invoice_id) REFERENCES public.sales_invoice (sales_invoice_id),
    CONSTRAINT fk_warranty_registration_invoice_line
        FOREIGN KEY (sales_invoice_line_id) REFERENCES public.sales_invoice_line (sales_invoice_line_id),
    CONSTRAINT fk_warranty_registration_product
        FOREIGN KEY (product_id) REFERENCES public.product (product_id),
    CONSTRAINT fk_warranty_registration_serial
        FOREIGN KEY (serial_number_id) REFERENCES public.serial_number (serial_number_id),
    CONSTRAINT fk_warranty_registration_customer
        FOREIGN KEY (customer_id) REFERENCES public.supplier (supplier_id),
    CONSTRAINT fk_warranty_registration_policy
        FOREIGN KEY (warranty_policy_id) REFERENCES public.warranty_policy (warranty_policy_id),
    CONSTRAINT fk_warranty_registration_supplier
        FOREIGN KEY (supplier_id) REFERENCES public.supplier (supplier_id),
    CONSTRAINT fk_warranty_registration_replaces
        FOREIGN KEY (replaces_registration_id) REFERENCES public.warranty_registration (warranty_registration_id),
    CONSTRAINT ck_warranty_registration_source CHECK (source IN ('Sale', 'Replacement', 'Manual')),
    CONSTRAINT ck_warranty_registration_status CHECK (status IN ('Active', 'Replaced', 'Void')),
    CONSTRAINT ck_warranty_registration_sale
        CHECK (source <> 'Sale' OR (sales_invoice_id IS NOT NULL AND sales_invoice_line_id IS NOT NULL)),
    CONSTRAINT ck_warranty_registration_replacement
        CHECK ((source = 'Replacement') = (replaces_registration_id IS NOT NULL)),
    CONSTRAINT ck_warranty_registration_dates
        CHECK (end_date >= start_date
               AND (replacement_end_date IS NULL OR replacement_end_date BETWEEN start_date AND end_date)),
    CONSTRAINT ck_warranty_registration_quantity
        CHECK (quantity > 0 AND (serial_number_id IS NULL OR quantity = 1)),
    CONSTRAINT ck_warranty_registration_void CHECK (status <> 'Void' OR void_reason IS NOT NULL)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_registration_no
    ON public.warranty_registration (organization_id, warranty_no);

-- A unit has at most one card in force.
CREATE UNIQUE INDEX IF NOT EXISTS ux_warranty_registration_active_serial
    ON public.warranty_registration (serial_number_id)
    WHERE status = 'Active' AND serial_number_id IS NOT NULL;

-- Counter look-ups: by serial, by the customer's mobile, by invoice.
CREATE INDEX IF NOT EXISTS ix_warranty_registration_serial_no
    ON public.warranty_registration (organization_id, upper(serial_no))
    WHERE serial_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_warranty_registration_mobile
    ON public.warranty_registration (organization_id, customer_mobile)
    WHERE customer_mobile IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_warranty_registration_invoice
    ON public.warranty_registration (sales_invoice_id)
    WHERE sales_invoice_id IS NOT NULL;

-- =====================================================================
-- Service jobs
-- =====================================================================

CREATE TABLE IF NOT EXISTS public.service_job
(
    service_job_id             BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id                  UUID NOT NULL DEFAULT gen_random_uuid(),
    organization_id            INT NOT NULL,
    job_no                     VARCHAR(40) NOT NULL,
    received_at                TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    -- Warranty: claimed under a warranty card. Paid: out of warranty, bought elsewhere, or warranty voided.
    job_type                   VARCHAR(20) NOT NULL,
    service_location           VARCHAR(10) NOT NULL DEFAULT 'InShop',
    priority                   VARCHAR(10) NOT NULL DEFAULT 'Normal',

    -- A registered customer, or a walk-in by name and mobile (as on sales invoices).
    customer_id                INT NULL,
    customer_name              VARCHAR(250) NULL,
    customer_mobile            VARCHAR(30) NULL,
    -- Where the work is done, for on-site service (e.g. a CC camera installation).
    service_address            VARCHAR(500) NULL,

    -- The device. product_id/serial_number_id when it is one of the shop's own; brand/model/serial as written
    -- on the device otherwise (or as well).
    device_type_lookup_data_id BIGINT NOT NULL,
    product_id                 INT NULL,
    serial_number_id           BIGINT NULL,
    brand_name                 VARCHAR(100) NULL,
    model_name                 VARCHAR(150) NULL,
    serial_no                  VARCHAR(100) NULL,
    accessories_received       VARCHAR(500) NULL,
    physical_condition         VARCHAR(1000) NULL,
    problem_description        VARCHAR(2000) NOT NULL,

    -- The card a warranty claim is made under, and why the warranty doesn't apply when a covered unit is charged
    -- (e.g. physical or liquid damage).
    warranty_registration_id   BIGINT NULL,
    warranty_void_reason       VARCHAR(500) NULL,

    technician_user_id         INT NULL,
    diagnosis                  VARCHAR(2000) NULL,
    -- Quotation for a paid job; the customer approves it before the repair.
    estimated_amount           NUMERIC(18,4) NOT NULL DEFAULT 0,
    approval_status            VARCHAR(20) NOT NULL DEFAULT 'NotRequired',
    approved_at                TIMESTAMP NULL,
    promised_date              DATE NULL,

    status                     VARCHAR(20) NOT NULL DEFAULT 'Received',
    resolution                 VARCHAR(20) NULL,
    resolution_note            VARCHAR(2000) NULL,
    completed_at               TIMESTAMP NULL,
    delivered_at               TIMESTAMP NULL,
    delivered_by               INT NULL,
    delivered_to_name          VARCHAR(150) NULL,
    cancel_reason              VARCHAR(500) NULL,

    -- Parts and charges are billed through Sales: the invoice made from this job's items.
    sales_invoice_id           BIGINT NULL,
    remarks                    VARCHAR(1000) NULL,

    created_at                 TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                 INT NULL,
    updated_at                 TIMESTAMP NULL,
    updated_by                 INT NULL,

    CONSTRAINT pk_service_job PRIMARY KEY (service_job_id),
    CONSTRAINT fk_service_job_organization
        FOREIGN KEY (organization_id) REFERENCES public.organization (organization_id),
    CONSTRAINT fk_service_job_customer
        FOREIGN KEY (customer_id) REFERENCES public.supplier (supplier_id),
    CONSTRAINT fk_service_job_device_type
        FOREIGN KEY (device_type_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT fk_service_job_product
        FOREIGN KEY (product_id) REFERENCES public.product (product_id),
    CONSTRAINT fk_service_job_serial
        FOREIGN KEY (serial_number_id) REFERENCES public.serial_number (serial_number_id),
    CONSTRAINT fk_service_job_warranty
        FOREIGN KEY (warranty_registration_id) REFERENCES public.warranty_registration (warranty_registration_id),
    CONSTRAINT fk_service_job_technician
        FOREIGN KEY (technician_user_id) REFERENCES public."User" (id),
    CONSTRAINT fk_service_job_invoice
        FOREIGN KEY (sales_invoice_id) REFERENCES public.sales_invoice (sales_invoice_id),
    CONSTRAINT ck_service_job_type CHECK (job_type IN ('Warranty', 'Paid')),
    CONSTRAINT ck_service_job_location CHECK (service_location IN ('InShop', 'OnSite')),
    CONSTRAINT ck_service_job_priority CHECK (priority IN ('Normal', 'Urgent')),
    CONSTRAINT ck_service_job_status
        CHECK (status IN ('Received', 'Diagnosing', 'AwaitingApproval', 'InRepair', 'WaitingForParts',
                          'AtSupplier', 'Ready', 'Delivered', 'Cancelled')),
    CONSTRAINT ck_service_job_resolution
        CHECK (resolution IS NULL
               OR resolution IN ('Repaired', 'Replaced', 'NoFaultFound', 'NotRepairable', 'CustomerDeclined')),
    CONSTRAINT ck_service_job_approval
        CHECK (approval_status IN ('NotRequired', 'Pending', 'Approved', 'Rejected')),
    CONSTRAINT ck_service_job_customer CHECK (customer_id IS NOT NULL OR customer_mobile IS NOT NULL),
    CONSTRAINT ck_service_job_warranty CHECK (job_type <> 'Warranty' OR warranty_registration_id IS NOT NULL),
    CONSTRAINT ck_service_job_on_site CHECK (service_location <> 'OnSite' OR service_address IS NOT NULL),
    CONSTRAINT ck_service_job_resolved CHECK (status NOT IN ('Ready', 'Delivered') OR resolution IS NOT NULL),
    CONSTRAINT ck_service_job_delivered CHECK ((status = 'Delivered') = (delivered_at IS NOT NULL)),
    CONSTRAINT ck_service_job_cancelled CHECK (status <> 'Cancelled' OR cancel_reason IS NOT NULL),
    CONSTRAINT ck_service_job_estimate CHECK (estimated_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_service_job_no
    ON public.service_job (organization_id, job_no);

-- One job per invoice.
CREATE UNIQUE INDEX IF NOT EXISTS ux_service_job_invoice
    ON public.service_job (sales_invoice_id)
    WHERE sales_invoice_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_service_job_status
    ON public.service_job (organization_id, status, received_at DESC);
CREATE INDEX IF NOT EXISTS ix_service_job_mobile
    ON public.service_job (organization_id, customer_mobile)
    WHERE customer_mobile IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_service_job_serial_no
    ON public.service_job (organization_id, upper(serial_no))
    WHERE serial_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_service_job_customer
    ON public.service_job (customer_id)
    WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_service_job_technician
    ON public.service_job (technician_user_id, status)
    WHERE technician_user_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_service_job_warranty
    ON public.service_job (warranty_registration_id)
    WHERE warranty_registration_id IS NOT NULL;

-- Parts used and services done. A part is a Stock product, a service charge a Service product (product_type).
CREATE TABLE IF NOT EXISTS public.service_job_item
(
    service_job_item_id    BIGINT GENERATED ALWAYS AS IDENTITY,
    service_job_id         BIGINT NOT NULL,
    line_no                INT NOT NULL,
    product_id             INT NOT NULL,
    description            VARCHAR(500) NULL,
    unit_id                INT NOT NULL,
    quantity               NUMERIC(18,4) NOT NULL,
    batch_id               BIGINT NULL,
    -- A serial-tracked part, e.g. a replacement SSD or the replacement unit itself.
    serial_number_id       BIGINT NULL,
    -- What the customer pays per unit; 0 when the line is covered by warranty.
    unit_price             NUMERIC(18,6) NOT NULL DEFAULT 0,
    discount_amount        NUMERIC(18,4) NOT NULL DEFAULT 0,
    line_total             NUMERIC(18,4) GENERATED ALWAYS AS (round(quantity * unit_price, 4) - discount_amount) STORED,
    is_warranty_covered    BOOLEAN NOT NULL DEFAULT FALSE,
    -- The sales invoice line this item became when the job was invoiced.
    sales_invoice_line_id  BIGINT NULL,
    remarks                VARCHAR(500) NULL,

    created_at             TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by             INT NULL,
    updated_at             TIMESTAMP NULL,
    updated_by             INT NULL,

    CONSTRAINT pk_service_job_item PRIMARY KEY (service_job_item_id),
    CONSTRAINT fk_service_job_item_job
        FOREIGN KEY (service_job_id) REFERENCES public.service_job (service_job_id) ON DELETE CASCADE,
    CONSTRAINT fk_service_job_item_product
        FOREIGN KEY (product_id) REFERENCES public.product (product_id),
    CONSTRAINT fk_service_job_item_unit
        FOREIGN KEY (unit_id) REFERENCES public.unit (unit_id),
    CONSTRAINT fk_service_job_item_batch
        FOREIGN KEY (batch_id) REFERENCES public.batch (batch_id),
    CONSTRAINT fk_service_job_item_serial
        FOREIGN KEY (serial_number_id) REFERENCES public.serial_number (serial_number_id),
    CONSTRAINT fk_service_job_item_invoice_line
        FOREIGN KEY (sales_invoice_line_id) REFERENCES public.sales_invoice_line (sales_invoice_line_id),
    CONSTRAINT ux_service_job_item_line UNIQUE (service_job_id, line_no),
    CONSTRAINT ck_service_job_item_quantity CHECK (quantity > 0 AND (serial_number_id IS NULL OR quantity = 1)),
    CONSTRAINT ck_service_job_item_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND discount_amount <= round(quantity * unit_price, 4)),
    CONSTRAINT ck_service_job_item_covered
        CHECK (NOT is_warranty_covered OR (unit_price = 0 AND discount_amount = 0))
);

CREATE INDEX IF NOT EXISTS ix_service_job_item_product ON public.service_job_item (product_id);

-- Advance payments taken for a job (customer receipts, Sales module), allocated to the job's invoice later.
CREATE TABLE IF NOT EXISTS public.service_job_receipt
(
    service_job_id       BIGINT NOT NULL,
    customer_receipt_id  BIGINT NOT NULL,
    created_at           TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by           INT NULL,

    CONSTRAINT pk_service_job_receipt PRIMARY KEY (service_job_id, customer_receipt_id),
    CONSTRAINT fk_service_job_receipt_job
        FOREIGN KEY (service_job_id) REFERENCES public.service_job (service_job_id) ON DELETE CASCADE,
    CONSTRAINT fk_service_job_receipt_receipt
        FOREIGN KEY (customer_receipt_id) REFERENCES public.customer_receipt (customer_receipt_id),
    -- A receipt is an advance for one job only.
    CONSTRAINT ux_service_job_receipt_receipt UNIQUE (customer_receipt_id)
);

CREATE TABLE IF NOT EXISTS public.service_job_status_history
(
    service_job_status_history_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    service_job_id                 BIGINT NOT NULL,
    from_status                    VARCHAR(20) NULL,
    to_status                      VARCHAR(20) NOT NULL,
    note                           VARCHAR(1000) NULL,
    -- The customer was told (e.g. an SMS that the device is ready).
    customer_notified              BOOLEAN NOT NULL DEFAULT FALSE,
    changed_at                     TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    changed_by                     INT NULL,

    CONSTRAINT pk_service_job_status_history PRIMARY KEY (service_job_status_history_id),
    CONSTRAINT fk_service_job_status_history_job
        FOREIGN KEY (service_job_id) REFERENCES public.service_job (service_job_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_service_job_status_history_job
    ON public.service_job_status_history (service_job_id, changed_at);

-- Photos of the device at intake, test reports and similar files (file storage URLs).
CREATE TABLE IF NOT EXISTS public.service_job_attachment
(
    service_job_attachment_id  BIGINT GENERATED ALWAYS AS IDENTITY,
    service_job_id             BIGINT NOT NULL,
    file_url                   VARCHAR(500) NOT NULL,
    file_name                  VARCHAR(255) NULL,
    caption                    VARCHAR(250) NULL,
    created_at                 TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                 INT NULL,

    CONSTRAINT pk_service_job_attachment PRIMARY KEY (service_job_attachment_id),
    CONSTRAINT fk_service_job_attachment_job
        FOREIGN KEY (service_job_id) REFERENCES public.service_job (service_job_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_service_job_attachment_job ON public.service_job_attachment (service_job_id);

-- =====================================================================
-- Claims to suppliers (RMA)
-- =====================================================================

-- A faulty unit sent to the supplier, distributor or brand service center: for a customer's job, or stock that
-- failed before it was sold (no job).
CREATE TABLE IF NOT EXISTS public.service_supplier_claim
(
    service_supplier_claim_id     BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id                     UUID NOT NULL DEFAULT gen_random_uuid(),
    organization_id               INT NOT NULL,
    claim_no                      VARCHAR(40) NOT NULL,
    service_job_id                BIGINT NULL,
    supplier_id                   INT NOT NULL,
    product_id                    INT NOT NULL,
    serial_number_id              BIGINT NULL,
    serial_no                     VARCHAR(100) NULL,
    problem_description           VARCHAR(2000) NOT NULL,
    sent_date                     DATE NOT NULL,
    -- The supplier's own RMA or ticket number.
    supplier_reference_no         VARCHAR(100) NULL,
    expected_return_date          DATE NULL,
    status                        VARCHAR(20) NOT NULL DEFAULT 'Sent',
    returned_date                 DATE NULL,
    outcome                       VARCHAR(20) NULL,
    replacement_serial_number_id  BIGINT NULL,
    replacement_serial_no         VARCHAR(100) NULL,
    -- What the supplier charges the shop (e.g. outside the supplier's warranty).
    service_charge_amount         NUMERIC(18,4) NOT NULL DEFAULT 0,
    remarks                       VARCHAR(1000) NULL,

    created_at                    TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by                    INT NULL,
    updated_at                    TIMESTAMP NULL,
    updated_by                    INT NULL,

    CONSTRAINT pk_service_supplier_claim PRIMARY KEY (service_supplier_claim_id),
    CONSTRAINT fk_service_supplier_claim_organization
        FOREIGN KEY (organization_id) REFERENCES public.organization (organization_id),
    CONSTRAINT fk_service_supplier_claim_job
        FOREIGN KEY (service_job_id) REFERENCES public.service_job (service_job_id),
    CONSTRAINT fk_service_supplier_claim_supplier
        FOREIGN KEY (supplier_id) REFERENCES public.supplier (supplier_id),
    CONSTRAINT fk_service_supplier_claim_product
        FOREIGN KEY (product_id) REFERENCES public.product (product_id),
    CONSTRAINT fk_service_supplier_claim_serial
        FOREIGN KEY (serial_number_id) REFERENCES public.serial_number (serial_number_id),
    CONSTRAINT fk_service_supplier_claim_replacement
        FOREIGN KEY (replacement_serial_number_id) REFERENCES public.serial_number (serial_number_id),
    CONSTRAINT ck_service_supplier_claim_status CHECK (status IN ('Sent', 'Returned', 'Cancelled')),
    CONSTRAINT ck_service_supplier_claim_outcome
        CHECK (outcome IS NULL OR outcome IN ('Repaired', 'Replaced', 'Rejected', 'Credited')),
    CONSTRAINT ck_service_supplier_claim_returned
        CHECK (status <> 'Returned' OR (returned_date IS NOT NULL AND outcome IS NOT NULL)),
    CONSTRAINT ck_service_supplier_claim_dates CHECK (returned_date IS NULL OR returned_date >= sent_date),
    CONSTRAINT ck_service_supplier_claim_charge CHECK (service_charge_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_service_supplier_claim_no
    ON public.service_supplier_claim (organization_id, claim_no);
CREATE INDEX IF NOT EXISTS ix_service_supplier_claim_job
    ON public.service_supplier_claim (service_job_id)
    WHERE service_job_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_service_supplier_claim_supplier
    ON public.service_supplier_claim (supplier_id, status);

-- =====================================================================
-- Warranty coverage today (for the counter's warranty check)
-- =====================================================================

-- Each card with what it covers today (UTC date): InReplacement (replace the unit), InWarranty (repair free),
-- Expired, or its status when Replaced or Void.
CREATE OR REPLACE VIEW public.warranty_coverage AS
SELECT w.warranty_registration_id,
       w.organization_id,
       w.warranty_no,
       w.product_id,
       w.serial_number_id,
       w.serial_no,
       w.customer_id,
       w.customer_name,
       w.customer_mobile,
       w.sales_invoice_id,
       w.start_date,
       w.replacement_end_date,
       w.end_date,
       w.covers_parts,
       w.covers_labor,
       CASE
           WHEN w.status <> 'Active' THEN w.status
           WHEN w.replacement_end_date >= (now() AT TIME ZONE 'utc')::date THEN 'InReplacement'
           WHEN w.end_date >= (now() AT TIME ZONE 'utc')::date THEN 'InWarranty'
           ELSE 'Expired'
       END AS coverage,
       GREATEST(w.end_date - (now() AT TIME ZONE 'utc')::date, 0) AS days_remaining
FROM public.warranty_registration w;

COMMIT;
