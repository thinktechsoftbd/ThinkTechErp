-- Voucher types and payment methods from lookup lists, and a reshaped voucher header (run after 017).
--
--   * Two lookup lists, managed under Settings -> Lookup Data: "Voucher Type" (Cash Payment Voucher,
--     Bank Payment Voucher, Journal Voucher, ...) and "Payment Method" (DD/Payorder, Account Transfer,
--     Others, Cheque). A voucher type named "Bank ..." is a bank voucher: paid by cheque, it needs the
--     cheque no, cheque date and pay type (A/C Pay or Cash).
--   * accounting.voucher:
--       voucher_type   -> voucher_type_id   (lookup "Voucher Type")
--       payment_method -> payment_method_id (lookup "Payment Method")
--       voucher_no and reference_no -> voucher_ref_no, numbered 00000000001, 00000000002, ... per
--       organization (a cheque number moves to the new cheque_no column)
--       + cheque_no, pay_type, is_deleted (a deleted draft is kept, flagged)
--       - supplier_id, customer_id (the party is on the lines)
--   * accounting.voucher_line: supplier_id -> party_id (purchase.supplier), + created_at, created_by,
--     updated_at, updated_by. Lines keep their ids when a draft is edited, so the stamps stay true.
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

-- =====================================================================
-- Lookup lists
-- =====================================================================

INSERT INTO public.lookup_table (lookup_table_name)
VALUES ('Voucher Type'), ('Payment Method')
ON CONFLICT DO NOTHING;

INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name)
SELECT t.lookup_table_id, v.name
FROM public.lookup_table t
CROSS JOIN (VALUES (1, 'Cash Payment Voucher'), (2, 'Cash Receive Voucher'), (3, 'Bank Payment Voucher'),
                   (4, 'Bank Receive Voucher'), (5, 'Journal Voucher'), (6, 'Contra Voucher'),
                   (7, 'Accounts Receivable for Sales'), (8, 'Accounts Payable for Purchase'),
                   (9, 'LC Payment Voucher'), (10, 'Bill Voucher'), (11, 'Adjustment Voucher'), (12, 'Debit Note'),
                   (13, 'Credit Note'), (14, 'Cash Withdrawn Journal'), (15, 'Sales Realization Journal'),
                   (16, 'Cash Deposit Journal')) AS v(position, name)
WHERE lower(t.lookup_table_name) = 'voucher type'
ORDER BY v.position
ON CONFLICT DO NOTHING;

INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name)
SELECT t.lookup_table_id, v.name
FROM public.lookup_table t
CROSS JOIN (VALUES (1, 'DD/Payorder'), (2, 'Account Transfer'), (3, 'Others'), (4, 'Cheque')) AS v(position, name)
WHERE lower(t.lookup_table_name) = 'payment method'
ORDER BY v.position
ON CONFLICT DO NOTHING;

-- =====================================================================
-- accounting.voucher
-- =====================================================================

DROP VIEW IF EXISTS accounting.ledger_entry;

ALTER TABLE accounting.voucher
    DROP CONSTRAINT IF EXISTS ck_voucher_type,
    DROP CONSTRAINT IF EXISTS ck_voucher_payment_method,
    DROP CONSTRAINT IF EXISTS ck_voucher_party,
    DROP CONSTRAINT IF EXISTS ck_voucher_bill_payment,
    DROP CONSTRAINT IF EXISTS ck_voucher_bill_collection;

ALTER TABLE accounting.voucher
    ADD COLUMN IF NOT EXISTS voucher_type_id   BIGINT NULL,          -- lookup "Voucher Type"
    ADD COLUMN IF NOT EXISTS payment_method_id BIGINT NULL,          -- lookup "Payment Method"
    ADD COLUMN IF NOT EXISTS cheque_no         VARCHAR(50) NULL,
    -- How the cheque is crossed: AcPay (account payee only) or Cash.
    ADD COLUMN IF NOT EXISTS pay_type          VARCHAR(10) NULL,
    ADD COLUMN IF NOT EXISTS is_deleted        BOOLEAN NOT NULL DEFAULT FALSE;

-- Vouchers entered before the lookups: map the old type and method names onto the new lists.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'accounting' AND table_name = 'voucher' AND column_name = 'voucher_type') THEN
        RETURN;
    END IF;

    UPDATE accounting.voucher v
    SET voucher_type_id = d.lookup_data_id
    FROM public.lookup_data d
    JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id AND lower(t.lookup_table_name) = 'voucher type'
    WHERE v.voucher_type_id IS NULL
      AND d.lookup_data_name = CASE
            WHEN v.voucher_type = 'Journal' THEN 'Journal Voucher'
            WHEN v.voucher_type = 'Contra' THEN 'Contra Voucher'
            WHEN v.voucher_type IN ('Payment', 'BillPayment')
                THEN CASE WHEN v.payment_method IN ('Bank', 'Cheque') THEN 'Bank Payment Voucher' ELSE 'Cash Payment Voucher' END
            WHEN v.voucher_type IN ('Receipt', 'BillCollection')
                THEN CASE WHEN v.payment_method IN ('Bank', 'Cheque') THEN 'Bank Receive Voucher' ELSE 'Cash Receive Voucher' END
          END;

    UPDATE accounting.voucher v
    SET payment_method_id = d.lookup_data_id
    FROM public.lookup_data d
    JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id AND lower(t.lookup_table_name) = 'payment method'
    WHERE v.payment_method_id IS NULL
      AND d.lookup_data_name = CASE v.payment_method
            WHEN 'Cheque' THEN 'Cheque'
            WHEN 'Bank' THEN 'Account Transfer'
            WHEN 'MobileWallet' THEN 'Others'
            WHEN 'Card' THEN 'Others'
            WHEN 'Other' THEN 'Others'
          END;

    -- The reference of a cheque payment was its cheque number.
    UPDATE accounting.voucher
    SET cheque_no = left(reference_no, 50)
    WHERE payment_method = 'Cheque' AND cheque_no IS NULL AND reference_no IS NOT NULL;

    ALTER TABLE accounting.voucher DROP COLUMN voucher_type;
END $$;

ALTER TABLE accounting.voucher
    DROP COLUMN IF EXISTS payment_method,
    DROP COLUMN IF EXISTS reference_no,
    DROP COLUMN IF EXISTS supplier_id,
    DROP COLUMN IF EXISTS customer_id;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'accounting' AND table_name = 'voucher' AND column_name = 'voucher_no') THEN
        ALTER TABLE accounting.voucher RENAME COLUMN voucher_no TO voucher_ref_no;
    END IF;
END $$;

ALTER TABLE accounting.voucher ALTER COLUMN voucher_type_id SET NOT NULL;

ALTER TABLE accounting.voucher
    DROP CONSTRAINT IF EXISTS fk_voucher_type,
    DROP CONSTRAINT IF EXISTS fk_voucher_payment_method,
    DROP CONSTRAINT IF EXISTS ck_voucher_pay_type;
ALTER TABLE accounting.voucher
    ADD CONSTRAINT fk_voucher_type FOREIGN KEY (voucher_type_id) REFERENCES public.lookup_data (lookup_data_id),
    ADD CONSTRAINT fk_voucher_payment_method FOREIGN KEY (payment_method_id) REFERENCES public.lookup_data (lookup_data_id),
    ADD CONSTRAINT ck_voucher_pay_type CHECK (pay_type IS NULL OR pay_type IN ('AcPay', 'Cash'));

DROP INDEX IF EXISTS accounting.ux_voucher_no;
CREATE UNIQUE INDEX IF NOT EXISTS ux_voucher_ref_no
    ON accounting.voucher (organization_id, lower(voucher_ref_no));
CREATE INDEX IF NOT EXISTS idx_voucher_type ON accounting.voucher (voucher_type_id);

-- New vouchers are numbered 00000000001, 00000000002, ... (one series per organization).
UPDATE public.document_sequence
SET prefix = '', number_padding = 11
WHERE document_type = 'Voucher' AND (prefix <> '' OR number_padding <> 11);

-- =====================================================================
-- accounting.voucher_line
-- =====================================================================

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'accounting' AND table_name = 'voucher_line' AND column_name = 'supplier_id') THEN
        ALTER TABLE accounting.voucher_line RENAME COLUMN supplier_id TO party_id;
    END IF;
END $$;

ALTER TABLE accounting.voucher_line DROP CONSTRAINT IF EXISTS fk_voucher_line_supplier;
ALTER TABLE accounting.voucher_line DROP CONSTRAINT IF EXISTS fk_voucher_line_party;
ALTER TABLE accounting.voucher_line
    ADD CONSTRAINT fk_voucher_line_party FOREIGN KEY (party_id) REFERENCES purchase.supplier (supplier_id);

DROP INDEX IF EXISTS accounting.idx_voucher_line_supplier;
CREATE INDEX IF NOT EXISTS idx_voucher_line_party
    ON accounting.voucher_line (party_id) WHERE party_id IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'accounting' AND table_name = 'voucher_line' AND column_name = 'created_at') THEN
        ALTER TABLE accounting.voucher_line
            ADD COLUMN created_at TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            ADD COLUMN created_by INT NULL,
            ADD COLUMN updated_at TIMESTAMP NULL,
            ADD COLUMN updated_by INT NULL;

        -- Lines entered so far were saved with their voucher.
        UPDATE accounting.voucher_line l
        SET created_at = v.created_at, created_by = v.created_by, updated_at = v.updated_at, updated_by = v.updated_by
        FROM accounting.voucher v
        WHERE v.voucher_id = l.voucher_id;
    END IF;
END $$;

-- Renumbering the lines of an edited draft swaps line numbers, so the check waits for the commit.
ALTER TABLE accounting.voucher_line DROP CONSTRAINT IF EXISTS ux_voucher_line;
ALTER TABLE accounting.voucher_line
    ADD CONSTRAINT ux_voucher_line UNIQUE (voucher_id, line_no) DEFERRABLE INITIALLY DEFERRED;

-- =====================================================================
-- The general ledger: every line of every posted voucher.
-- =====================================================================

CREATE VIEW accounting.ledger_entry AS
SELECT v.organization_id, v.voucher_id, v.voucher_ref_no, v.voucher_type_id, vt.lookup_data_name AS voucher_type,
       v.voucher_date, v.cheque_no, v.narration, l.voucher_line_id, l.line_no, l.acc_sub_class_id, l.party_id,
       l.debit_amount, l.credit_amount, l.lk_currency_id, l.local_exchange_rate, l.line_narration
FROM accounting.voucher v
JOIN accounting.voucher_line l ON l.voucher_id = v.voucher_id
JOIN public.lookup_data vt ON vt.lookup_data_id = v.voucher_type_id
WHERE v.status = 'Posted' AND NOT v.is_deleted;

COMMIT;
