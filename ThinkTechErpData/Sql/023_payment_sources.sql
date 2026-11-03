-- Payment sources (run after 021, which moved every schema into public): where money is paid from or
-- received into (Cash, a bank, bKash, ...), each with the ledger head every kind of payment posts to.
--
--   * public.inv_payment_source_h / inv_payment_source_d: shared by every organization, like the chart
--     of accounts. The live database has them already, so CREATE ... IF NOT EXISTS leaves them as they are;
--     it only sets them up on a new database.
--   * An index on the lines' foreign key, for loading a source's lines.
--   * The Payment Sources menu under Purchase -> Settings. The screen reads with PURCHASE.V (the API takes
--     any inventory, purchase or sales permission) and changes with PURCHASE.M or INVENTORY.M.
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE TABLE IF NOT EXISTS public.inv_payment_source_h
(
    id              INT4 GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    object_id       INT4 GENERATED ALWAYS AS IDENTITY UNIQUE,

    source_name_en  VARCHAR(150) NOT NULL,
    source_name_loc VARCHAR(150) NULL,

    is_active       BOOLEAN DEFAULT TRUE NOT NULL,

    created_at      TIMESTAMP(3) DEFAULT (NOW() AT TIME ZONE 'UTC') NOT NULL,
    created_by      INT4 NULL,

    updated_at      TIMESTAMP(3) NULL,
    updated_by      INT4 NULL
);

CREATE TABLE IF NOT EXISTS public.inv_payment_source_d
(
    id                      INT4 GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    inv_payment_source_h_id INT4 NOT NULL,
    payment_for             VARCHAR(150) NOT NULL,
    acc_sub_class_id        INT4 NOT NULL,

    CONSTRAINT fk_payment_source_h
        FOREIGN KEY (inv_payment_source_h_id)
        REFERENCES public.inv_payment_source_h (id),

    CONSTRAINT fk_payment_source_acc_sub_class
        FOREIGN KEY (acc_sub_class_id)
        REFERENCES public.acc_sub_class (acc_sub_class_id)
);

CREATE INDEX IF NOT EXISTS idx_inv_payment_source_d_h
    ON public.inv_payment_source_d (inv_payment_source_h_id);

-- =====================================================================
-- Menu: Purchase -> Settings -> Payment Sources
-- =====================================================================

DO $$
DECLARE
    super_admin int := (SELECT id FROM public."Role" WHERE name = 'Super Admin');
    settings_id int := (SELECT s.id
                        FROM public."Menu" s
                        JOIN public."Menu" m ON m.id = s."parentId" AND m."parentId" IS NULL AND m.title = 'Purchase'
                        WHERE s.title = 'Settings');
BEGIN
    IF settings_id IS NULL THEN
        RAISE EXCEPTION 'There is no Purchase -> Settings menu to add Payment Sources to. Run 011 first.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public."Menu" WHERE path = '/purchase/payment-sources') THEN
        INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        VALUES ('Payment Sources', '/purchase/payment-sources', 'wallet-cards',
                (SELECT coalesce(max("sortOrder"), 0) + 1 FROM public."Menu" WHERE "parentId" = settings_id),
                settings_id, super_admin, true);
    END IF;
END $$;

COMMIT;
