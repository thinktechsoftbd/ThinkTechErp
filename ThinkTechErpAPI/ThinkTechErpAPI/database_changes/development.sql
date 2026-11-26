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

--changes_date: 25-Nov-2026
--scripts
-- SMS provider per organization (Security -> Organizations) instead of per SMS allocation:
--   * public.sms_provider.sms_sender_id: the number or brand name the provider sends from. CDC (id 6, the provider
--     MediDesk sends through) gets 8809648909301, the sender MediDesk used to pass itself.
--   * public.organization.sms_provider_id (NULL: none chosen yet): the provider the organization sends SMS through.
--     The SMS gateway (api/sms/gateway) takes it from the caller's organization and the sender from the provider, so
--     callers pass neither. An organization's SMS allocations come from this provider's stock.
--   * public.sms_allocation.sms_provider_id is dropped, with fk_sms_allocation_provider and idx_sms_allocation_provider.
--     Before that, each organization without a provider gets the one of its latest allocation. An organization whose
--     allocations come from more than one provider stops the script: set its organization.sms_provider_id by hand
--     (its allocations then count against that provider) and run the script again.

BEGIN;

ALTER TABLE public.sms_provider ADD COLUMN IF NOT EXISTS sms_sender_id VARCHAR(50) NULL;

COMMENT ON COLUMN public.sms_provider.sms_sender_id IS 'The number or brand name SMS are sent from through this provider.';

UPDATE public.sms_provider
SET sms_sender_id = '8809648909301'
WHERE sms_provider_id = 6 AND sms_sender_id IS NULL;

ALTER TABLE public.organization ADD COLUMN IF NOT EXISTS sms_provider_id INT NULL;

COMMENT ON COLUMN public.organization.sms_provider_id IS 'The SMS provider the organization sends SMS through; NULL: none chosen yet.';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'public.organization'::regclass AND conname = 'fk_organization_sms_provider') THEN
        ALTER TABLE public.organization
            ADD CONSTRAINT fk_organization_sms_provider
            FOREIGN KEY (sms_provider_id) REFERENCES public.sms_provider (sms_provider_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_organization_sms_provider
    ON public.organization (sms_provider_id)
    WHERE sms_provider_id IS NOT NULL;

-- Each organization keeps the provider its allocations came from, while sms_allocation still says so.

DO $$
DECLARE
    mixed TEXT;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'public' AND table_name = 'sms_allocation' AND column_name = 'sms_provider_id') THEN
        RETURN;
    END IF;

    SELECT string_agg(format('%s (organization_id %s, providers %s)', name, id, providers), '; ' ORDER BY id)
    INTO mixed
    FROM (SELECT o.organization_id AS id, o.organization_name_en AS name,
                 string_agg(DISTINCT a.sms_provider_id::text, ', ') AS providers
          FROM public.sms_allocation a
          JOIN public.organization o ON o.object_id = a.organization_id
          WHERE o.sms_provider_id IS NULL
          GROUP BY o.organization_id, o.organization_name_en
          HAVING count(DISTINCT a.sms_provider_id) > 1) m;
    IF mixed IS NOT NULL THEN
        RAISE EXCEPTION 'These organizations have SMS allocations from more than one provider: %. Set their organization.sms_provider_id by hand, then run this script again.', mixed;
    END IF;

    UPDATE public.organization o
    SET sms_provider_id = latest.sms_provider_id
    FROM (SELECT DISTINCT ON (organization_id) organization_id, sms_provider_id
          FROM public.sms_allocation
          ORDER BY organization_id, allocation_date DESC, sms_allocation_id DESC) latest
    WHERE latest.organization_id = o.object_id AND o.sms_provider_id IS NULL;
END $$;

ALTER TABLE public.sms_allocation DROP CONSTRAINT IF EXISTS fk_sms_allocation_provider;
DROP INDEX IF EXISTS public.idx_sms_allocation_provider;
ALTER TABLE public.sms_allocation DROP COLUMN IF EXISTS sms_provider_id;

COMMIT;

--changes_date: 26-Nov-2026
--scripts
-- Finalizing a service invoice (Warranty -> Transactions -> Service Invoice), as a purchase or sales invoice: it is
-- posted (locked) and its Bill Voucher is posted from the ledger mappings.
--   * voucher.service_invoice_id: the service invoice a Bill Voucher was made from. A voucher comes from at most one
--     invoice (purchase, sales or service), and a service invoice has at most one live voucher (deleted and cancelled
--     vouchers are left out, so it can be finalized again once its voucher is gone).
--   * inventory_setting.service_charge_acc_sub_class_id: the ledger head a service invoice's service charge (labour)
--     posts to, chosen under Inventory -> Settings. The parts post to their categories' heads, as on a sale.

BEGIN;

ALTER TABLE public.voucher ADD COLUMN IF NOT EXISTS service_invoice_id INT8 NULL;

ALTER TABLE public.voucher DROP CONSTRAINT IF EXISTS fk_voucher_service_invoice;
ALTER TABLE public.voucher
    ADD CONSTRAINT fk_voucher_service_invoice
        FOREIGN KEY (service_invoice_id) REFERENCES public.service_invoice (service_invoice_id);

ALTER TABLE public.voucher DROP CONSTRAINT IF EXISTS ck_voucher_one_invoice;
ALTER TABLE public.voucher
    ADD CONSTRAINT ck_voucher_one_invoice
        CHECK (num_nonnulls(purchase_invoice_id, sales_invoice_id, service_invoice_id) <= 1);

CREATE UNIQUE INDEX IF NOT EXISTS ux_voucher_service_invoice
    ON public.voucher (service_invoice_id)
    WHERE service_invoice_id IS NOT NULL AND NOT is_deleted AND status <> 'Cancelled';

COMMENT ON COLUMN public.voucher.service_invoice_id IS 'The service invoice this bill voucher was made from (finalize).';

ALTER TABLE public.inventory_setting ADD COLUMN IF NOT EXISTS service_charge_acc_sub_class_id INT4 NULL;

ALTER TABLE public.inventory_setting DROP CONSTRAINT IF EXISTS fk_inventory_setting_service_charge_head;
ALTER TABLE public.inventory_setting
    ADD CONSTRAINT fk_inventory_setting_service_charge_head
        FOREIGN KEY (service_charge_acc_sub_class_id) REFERENCES public.acc_sub_class (acc_sub_class_id);

COMMENT ON COLUMN public.inventory_setting.service_charge_acc_sub_class_id IS
    'The ledger head a finalized service invoice''s service charge (labour) posts to.';

COMMIT;
