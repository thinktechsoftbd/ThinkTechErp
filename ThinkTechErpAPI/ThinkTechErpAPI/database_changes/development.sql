-- Database changes waiting to go to production, newest last. Each entry is a --changes_date line, then --scripts
-- and its SQL. Every script is idempotent: running it again changes nothing.

--changes_date: 24-Nov-2026
--scripts
-- Service Invoice (Warranty -> Transactions -> Service Invoice, client route /warranty/service-invoices):
--   * public.service_invoice: a copy of public.sales_invoice (same columns, defaults, checks, indexes and keys, named
--     service_*), plus service_note and service_amount. service_amount (the service / labour charge) is part of
--     total_amount: total = lines + charge_amount + service_amount - extra_discount_amount + round_off_amount.
--     The Remarks field uses the copied remarks column.
--   * public.service_invoice_line: a copy of public.sales_invoice_line (the parts used), deleted with its invoice.
--   * SERVICE_INVOICE.V / SERVICE_INVOICE.M, granted to the Super Admin role.
--   * The menu Warranty -> Transactions -> Service Invoice, seen by the same roles as its Transactions group.

BEGIN;

CREATE TABLE IF NOT EXISTS public.service_invoice
(
    service_invoice_id      BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id               UUID           NOT NULL DEFAULT gen_random_uuid(),
    organization_id         INT            NOT NULL,
    invoice_no              VARCHAR(40)    NOT NULL,
    invoice_date            DATE           NOT NULL,
    sale_type               VARCHAR(10)    NOT NULL DEFAULT 'Cash',
    due_date                DATE           NULL,
    customer_id             INT            NULL,
    customer_name           VARCHAR(250)   NULL,
    customer_mobile         VARCHAR(30)    NULL,
    warehouse_id            INT            NOT NULL,
    currency_lookup_data_id BIGINT         NOT NULL,
    exchange_rate           NUMERIC(18, 6) NOT NULL DEFAULT 1,
    subtotal_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    discount_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    extra_discount_amount   NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    charge_amount           NUMERIC(18, 4) NOT NULL DEFAULT 0,
    service_amount          NUMERIC(18, 4) NOT NULL DEFAULT 0,
    round_off_amount        NUMERIC(18, 4) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    paid_amount             NUMERIC(18, 4) NOT NULL DEFAULT 0,
    payment_method          VARCHAR(20)    NULL,
    payment_provider        VARCHAR(100)   NULL,
    payment_reference_no    VARCHAR(100)   NULL,
    settled_amount          NUMERIC(18, 4) NOT NULL DEFAULT 0,
    status                  VARCHAR(20)    NOT NULL DEFAULT 'Draft',
    payment_status          VARCHAR(20)    NOT NULL DEFAULT 'Unpaid',
    posted_at               TIMESTAMP(3)   NULL,
    posted_by               INT            NULL,
    service_note            VARCHAR(2000)  NULL,
    remarks                 VARCHAR(1000)  NULL,
    created_at              TIMESTAMP(3)   NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by              INT            NULL,
    updated_at              TIMESTAMP(3)   NULL,
    updated_by              INT            NULL,
    CONSTRAINT pk_service_invoice PRIMARY KEY (service_invoice_id),
    CONSTRAINT ux_service_invoice_no UNIQUE (organization_id, invoice_no),
    CONSTRAINT fk_service_invoice_organization
        FOREIGN KEY (organization_id) REFERENCES public.organization (organization_id),
    CONSTRAINT fk_service_invoice_customer
        FOREIGN KEY (customer_id) REFERENCES public.supplier (supplier_id),
    CONSTRAINT fk_service_invoice_warehouse
        FOREIGN KEY (warehouse_id) REFERENCES public.warehouse (warehouse_id),
    CONSTRAINT fk_service_invoice_currency
        FOREIGN KEY (currency_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT ck_service_invoice_type CHECK (sale_type IN ('Cash', 'Credit')),
    CONSTRAINT ck_service_invoice_credit_customer CHECK (sale_type = 'Cash' OR customer_id IS NOT NULL),
    CONSTRAINT ck_service_invoice_rate CHECK (exchange_rate > 0),
    CONSTRAINT ck_service_invoice_extra_discount CHECK (extra_discount_amount >= 0),
    CONSTRAINT ck_service_invoice_service_amount CHECK (service_amount >= 0),
    CONSTRAINT ck_service_invoice_cash_paid CHECK (sale_type = 'Credit' OR paid_amount = total_amount),
    CONSTRAINT ck_service_invoice_paid
        CHECK (paid_amount >= 0 AND paid_amount <= total_amount AND (paid_amount = 0 OR payment_method IS NOT NULL)),
    CONSTRAINT ck_service_invoice_payment_method
        CHECK (payment_method IN ('Cash', 'Bank', 'MobileWallet', 'Card', 'Other')),
    CONSTRAINT ck_service_invoice_settled CHECK (settled_amount >= paid_amount AND settled_amount <= total_amount),
    CONSTRAINT ck_service_invoice_status CHECK (status IN ('Draft', 'Posted', 'Cancelled')),
    CONSTRAINT ck_service_invoice_payment_status CHECK (payment_status IN ('Unpaid', 'PartiallyPaid', 'Paid'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_service_invoice_object_id ON public.service_invoice (object_id);
CREATE INDEX IF NOT EXISTS idx_service_invoice_date ON public.service_invoice (organization_id, invoice_date);
CREATE INDEX IF NOT EXISTS idx_service_invoice_customer
    ON public.service_invoice (customer_id, invoice_date)
    WHERE customer_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_service_invoice_due
    ON public.service_invoice (organization_id, due_date)
    WHERE status = 'Posted' AND payment_status <> 'Paid';

COMMENT ON TABLE public.service_invoice IS 'Service invoices (Warranty -> Transactions): parts used and the service charge, cash or credit.';
COMMENT ON COLUMN public.service_invoice.service_note IS 'What was serviced or repaired, shown on the invoice.';
COMMENT ON COLUMN public.service_invoice.service_amount IS 'The service / labour charge; part of total_amount.';

CREATE TABLE IF NOT EXISTS public.service_invoice_line
(
    service_invoice_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    service_invoice_id      BIGINT         NOT NULL,
    line_no                 INT            NOT NULL,
    product_id              INT            NOT NULL,
    description             VARCHAR(500)   NULL,
    unit_id                 INT            NOT NULL,
    conversion_factor       NUMERIC(18, 6) NOT NULL,
    quantity                NUMERIC(18, 4) NOT NULL,
    base_quantity           NUMERIC(18, 4) GENERATED ALWAYS AS (quantity * conversion_factor) STORED,
    batch_id                BIGINT         NULL,
    unit_price              NUMERIC(18, 6) NOT NULL DEFAULT 0,
    discount_percent        NUMERIC(9, 4)  NOT NULL DEFAULT 0,
    discount_amount         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    tax_group_id            INT            NULL,
    tax_amount              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_total              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    unit_cost               NUMERIC(18, 6) NOT NULL DEFAULT 0,
    remarks                 VARCHAR(500)   NULL,
    returned_quantity       NUMERIC(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_service_invoice_line PRIMARY KEY (service_invoice_line_id),
    CONSTRAINT ux_service_invoice_line UNIQUE (service_invoice_id, line_no),
    CONSTRAINT fk_service_invoice_line_invoice
        FOREIGN KEY (service_invoice_id) REFERENCES public.service_invoice (service_invoice_id) ON DELETE CASCADE,
    CONSTRAINT fk_service_invoice_line_product FOREIGN KEY (product_id) REFERENCES public.product (product_id),
    CONSTRAINT fk_service_invoice_line_unit FOREIGN KEY (unit_id) REFERENCES public.unit (unit_id),
    CONSTRAINT fk_service_invoice_line_batch FOREIGN KEY (batch_id) REFERENCES public.batch (batch_id),
    CONSTRAINT fk_service_invoice_line_tax_group FOREIGN KEY (tax_group_id) REFERENCES public.tax_group (tax_group_id),
    CONSTRAINT ck_service_invoice_line_quantity CHECK (quantity > 0 AND conversion_factor > 0),
    CONSTRAINT ck_service_invoice_line_amounts
        CHECK (unit_price >= 0 AND discount_amount >= 0 AND tax_amount >= 0 AND unit_cost >= 0
               AND discount_percent BETWEEN 0 AND 100),
    CONSTRAINT ck_service_invoice_line_returned CHECK (returned_quantity >= 0)
);

CREATE INDEX IF NOT EXISTS idx_service_invoice_line_product ON public.service_invoice_line (product_id);

-- Permissions

INSERT INTO public."Permission" (code, name, description)
VALUES ('SERVICE_INVOICE.V', 'View Service Invoices', 'View service invoices'),
       ('SERVICE_INVOICE.M', 'Manage Service Invoices', 'Create, edit and delete service invoices')
ON CONFLICT (code) DO NOTHING;

INSERT INTO public."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM public."Role" r
JOIN public."Permission" p ON p.code IN ('SERVICE_INVOICE.V', 'SERVICE_INVOICE.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- Menu: Warranty -> Transactions -> Service Invoice, after Warranty (the service desk). Found by path, not id.

DO $$
DECLARE
    group_id INT;
    menu_id  INT;
BEGIN
    SELECT g.id INTO group_id
    FROM public."Menu" g
    JOIN public."Menu" w ON w.id = g."parentId"
    WHERE w."parentId" IS NULL AND w.title = 'Warranty' AND g.title = 'Transactions';
    IF group_id IS NULL THEN
        RAISE NOTICE 'Warranty -> Transactions is missing (run 037_warranty_access.sql first); no Service Invoice menu added.';
        RETURN;
    END IF;

    SELECT id INTO menu_id FROM public."Menu" WHERE path = '/warranty/service-invoices';
    IF menu_id IS NULL THEN
        INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        VALUES ('Service Invoice', '/warranty/service-invoices', 'receipt',
                (SELECT coalesce(max("sortOrder"), 0) + 1 FROM public."Menu" WHERE "parentId" = group_id),
                group_id, NULL, true)
        RETURNING id INTO menu_id;

        -- Seen by the roles that see the Transactions group (042_menu_roles.sql).
        IF to_regclass('public."MenuRole"') IS NOT NULL THEN
            INSERT INTO public."MenuRole" ("menuId", "roleId")
            SELECT menu_id, "roleId" FROM public."MenuRole" WHERE "menuId" = group_id
            ON CONFLICT DO NOTHING;
        END IF;
    END IF;
END $$;

COMMIT;
