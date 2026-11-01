-- Suppliers and customers in one table, and voucher lines on the acc_* chart (run after 015, once
-- accounting.chart_account has been dropped and the acc_* tables copied in).
--
--   * purchase.supplier holds every party. is_supplier and is_customer say which sides it is on: the
--     purchase screens list suppliers and the sales screens list customers, and one person can be both.
--     The supplier_* / unprefixed money columns (payment terms, credit limit, opening balance) are the
--     supplier side; the customer_* columns are the customer side, so each side keeps its own dues.
--   * sales.customer is dropped: each customer is copied into purchase.supplier (is_customer only), and
--     every foreign key that pointed at it (sales invoices, returns, receipts, vouchers) now points at
--     purchase.supplier with the new id. The sales columns keep their customer_id names.
--   * accounting.voucher_line: account_id becomes acc_sub_class_id (a sub class of the acc_* chart),
--     customer_id is merged into supplier_id (the party), and each line records its currency
--     (lk_currency_id, BDT by default) and local_exchange_rate. Amounts stay in BDT.
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

-- =====================================================================
-- Parties: supplier and customer sides
-- =====================================================================

ALTER TABLE purchase.supplier
    ADD COLUMN IF NOT EXISTS is_supplier                   BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS is_customer                   BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS customer_group_lookup_data_id BIGINT NULL,          -- lookup "Customer Group"
    -- Identity document of a credit customer (Bangladesh: NID; elsewhere national ID or passport).
    ADD COLUMN IF NOT EXISTS national_id_no                VARCHAR(30) NULL,
    ADD COLUMN IF NOT EXISTS customer_payment_terms_days   INT NOT NULL DEFAULT 0,
    -- The most the customer may owe at once, in the base currency. NULL = no limit, 0 = cash only.
    ADD COLUMN IF NOT EXISTS customer_credit_limit         NUMERIC(18, 4) NULL,
    -- Owed by the customer (+) or paid in advance by them (-) at go-live, in the base currency.
    ADD COLUMN IF NOT EXISTS customer_opening_balance      NUMERIC(18, 4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS customer_opening_balance_date DATE NULL;

ALTER TABLE purchase.supplier
    DROP CONSTRAINT IF EXISTS fk_supplier_customer_group,
    DROP CONSTRAINT IF EXISTS ck_supplier_customer_terms,
    DROP CONSTRAINT IF EXISTS ck_supplier_customer_credit_limit,
    DROP CONSTRAINT IF EXISTS ck_supplier_side;
ALTER TABLE purchase.supplier
    ADD CONSTRAINT fk_supplier_customer_group
        FOREIGN KEY (customer_group_lookup_data_id) REFERENCES public.lookup_data (lookup_data_id),
    ADD CONSTRAINT ck_supplier_customer_terms CHECK (customer_payment_terms_days >= 0),
    ADD CONSTRAINT ck_supplier_customer_credit_limit CHECK (customer_credit_limit IS NULL OR customer_credit_limit >= 0),
    ADD CONSTRAINT ck_supplier_side CHECK (is_supplier OR is_customer);

-- Shops look customers up by mobile number.
CREATE INDEX IF NOT EXISTS idx_supplier_mobile
    ON purchase.supplier (organization_id, mobile) WHERE mobile IS NOT NULL;

-- =====================================================================
-- Move sales.customer into purchase.supplier
-- =====================================================================

DROP VIEW IF EXISTS sales.customer_balance;
DROP VIEW IF EXISTS sales.customer_ledger;

DO $$
DECLARE
    fk record;
BEGIN
    IF to_regclass('sales.customer') IS NULL THEN
        RETURN;
    END IF;

    -- A customer code already used by a supplier of the same organization gets a -C<id> suffix.
    INSERT INTO purchase.supplier (
        object_id, organization_id, supplier_code, supplier_name, supplier_name_local, contact_person, mobile, phone,
        email, address_line1, address_line2, city, state, postal_code, country_code, tax_registration_no, remarks,
        is_active, is_supplier, is_customer, customer_group_lookup_data_id, national_id_no, customer_payment_terms_days,
        customer_credit_limit, customer_opening_balance, customer_opening_balance_date,
        created_at, created_by, updated_at, updated_by)
    SELECT c.object_id, c.organization_id,
           CASE WHEN EXISTS (SELECT 1 FROM purchase.supplier s
                             WHERE s.organization_id = c.organization_id AND lower(s.supplier_code) = lower(c.customer_code))
                THEN left(c.customer_code, 18) || '-C' || c.customer_id
                ELSE c.customer_code END,
           c.customer_name, c.customer_name_local, c.contact_person, c.mobile, c.phone, c.email, c.address_line1,
           c.address_line2, c.city, c.state, c.postal_code, c.country_code, c.tax_registration_no, c.remarks,
           c.is_active, FALSE, TRUE, c.customer_group_lookup_data_id, c.national_id_no, c.payment_terms_days,
           c.credit_limit, c.opening_balance, c.opening_balance_date,
           c.created_at, c.created_by, c.updated_at, c.updated_by
    FROM sales.customer c
    ORDER BY c.customer_id;

    -- The object_id came along, so it pairs each customer with its new row.
    CREATE TEMP TABLE customer_map ON COMMIT DROP AS
    SELECT c.customer_id, s.supplier_id
    FROM sales.customer c
    JOIN purchase.supplier s ON s.object_id = c.object_id;

    -- Every foreign key to sales.customer: renumber its column, then point it at purchase.supplier.
    FOR fk IN
        SELECT con.conname, con.conrelid::regclass AS table_name, a.attname AS column_name
        FROM pg_constraint con
        JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = con.conkey[1]
        WHERE con.contype = 'f' AND con.confrelid = 'sales.customer'::regclass
    LOOP
        EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', fk.table_name, fk.conname);
        EXECUTE format('UPDATE %s t SET %I = m.supplier_id FROM customer_map m WHERE t.%I = m.customer_id',
                       fk.table_name, fk.column_name, fk.column_name);
        EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I FOREIGN KEY (%I) REFERENCES purchase.supplier (supplier_id)',
                       fk.table_name, fk.conname, fk.column_name);
    END LOOP;

    DROP TABLE sales.customer;
END $$;

-- The customer side of the same views, now over purchase.supplier (customer_id = supplier_id).

-- Everything that changed what each customer on file owes, in the base currency, with the running
-- balance (+ = the customer owes us, - = advance). A cash sale to a customer on file shows as
-- Invoice and PaidOnInvoice, netting to zero.
CREATE VIEW sales.customer_ledger AS
WITH entries AS
(
    SELECT c.organization_id, c.supplier_id AS customer_id, c.customer_opening_balance_date AS entry_date,
           0 AS entry_order, 'Opening' AS entry_type, NULL::bigint AS document_id, NULL::varchar AS document_no,
           greatest(c.customer_opening_balance, 0)  AS debit_amount,
           greatest(-c.customer_opening_balance, 0) AS credit_amount
    FROM purchase.supplier c
    WHERE c.customer_opening_balance <> 0

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
CREATE VIEW sales.customer_balance AS
SELECT c.organization_id,
       c.supplier_id AS customer_id,
       coalesce(sum(l.debit_amount - l.credit_amount), 0) AS balance,
       c.customer_credit_limit AS credit_limit,
       c.customer_credit_limit - coalesce(sum(l.debit_amount - l.credit_amount), 0) AS available_credit
FROM purchase.supplier c
LEFT JOIN sales.customer_ledger l ON l.customer_id = c.supplier_id
GROUP BY c.organization_id, c.supplier_id, c.customer_credit_limit;

-- =====================================================================
-- Voucher lines on the acc_* chart, with a currency
-- =====================================================================

DROP VIEW IF EXISTS accounting.ledger_entry;

ALTER TABLE accounting.voucher_line DROP CONSTRAINT IF EXISTS ck_voucher_line_party;

DO $$
DECLARE
    bdt bigint := (SELECT d.lookup_data_id
                   FROM public.lookup_data d
                   JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
                   WHERE lower(t.lookup_table_name) = 'currency' AND upper(d.lookup_data_name) = 'BDT'
                   ORDER BY d.lookup_data_id
                   LIMIT 1);
    orphans int;
BEGIN
    IF bdt IS NULL THEN
        RAISE EXCEPTION 'The Currency lookup list has no BDT value. Add it under Settings -> Lookup Data and run this again.';
    END IF;

    -- One party column: a customer named on a line moves to supplier_id (customer_map renumbered it above).
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'accounting' AND table_name = 'voucher_line' AND column_name = 'customer_id') THEN
        UPDATE accounting.voucher_line SET supplier_id = customer_id WHERE customer_id IS NOT NULL;
        ALTER TABLE accounting.voucher_line DROP COLUMN customer_id;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'accounting' AND table_name = 'voucher_line' AND column_name = 'account_id') THEN
        ALTER TABLE accounting.voucher_line RENAME COLUMN account_id TO acc_sub_class_id;
    END IF;

    -- Lines saved against the dropped chart_account point at ids acc_sub_class doesn't have.
    SELECT count(*) INTO orphans
    FROM accounting.voucher_line l
    WHERE NOT EXISTS (SELECT 1 FROM accounting.acc_sub_class s WHERE s.acc_sub_class_id = l.acc_sub_class_id);
    IF orphans > 0 THEN
        RAISE EXCEPTION '% voucher line(s) point at ledger heads that are not in accounting.acc_sub_class (the old chart_account ids). Delete those vouchers and run this again: DELETE FROM accounting.voucher v WHERE EXISTS (SELECT 1 FROM accounting.voucher_line l WHERE l.voucher_id = v.voucher_id AND NOT EXISTS (SELECT 1 FROM accounting.acc_sub_class s WHERE s.acc_sub_class_id = l.acc_sub_class_id));', orphans;
    END IF;

    ALTER TABLE accounting.voucher_line
        ADD COLUMN IF NOT EXISTS lk_currency_id      BIGINT NULL,
        ADD COLUMN IF NOT EXISTS local_exchange_rate NUMERIC(18, 6) NOT NULL DEFAULT 1;
    UPDATE accounting.voucher_line SET lk_currency_id = bdt WHERE lk_currency_id IS NULL;
    EXECUTE format('ALTER TABLE accounting.voucher_line ALTER COLUMN lk_currency_id SET DEFAULT %s', bdt);
    ALTER TABLE accounting.voucher_line ALTER COLUMN lk_currency_id SET NOT NULL;
END $$;

ALTER TABLE accounting.voucher_line
    DROP CONSTRAINT IF EXISTS fk_voucher_line_account,
    DROP CONSTRAINT IF EXISTS fk_voucher_line_sub_class,
    DROP CONSTRAINT IF EXISTS fk_voucher_line_currency,
    DROP CONSTRAINT IF EXISTS ck_voucher_line_rate;
ALTER TABLE accounting.voucher_line
    ADD CONSTRAINT fk_voucher_line_sub_class
        FOREIGN KEY (acc_sub_class_id) REFERENCES accounting.acc_sub_class (acc_sub_class_id),
    ADD CONSTRAINT fk_voucher_line_currency
        FOREIGN KEY (lk_currency_id) REFERENCES public.lookup_data (lookup_data_id),
    ADD CONSTRAINT ck_voucher_line_rate CHECK (local_exchange_rate > 0);

ALTER INDEX IF EXISTS accounting.idx_voucher_line_account RENAME TO idx_voucher_line_sub_class;
CREATE INDEX IF NOT EXISTS idx_voucher_line_sub_class ON accounting.voucher_line (acc_sub_class_id);

-- The general ledger: every line of every posted voucher.
CREATE VIEW accounting.ledger_entry AS
SELECT v.organization_id, v.voucher_id, v.voucher_no, v.voucher_type, v.voucher_date, v.reference_no,
       v.narration, l.voucher_line_id, l.line_no, l.acc_sub_class_id, l.supplier_id,
       l.debit_amount, l.credit_amount, l.lk_currency_id, l.local_exchange_rate, l.line_narration
FROM accounting.voucher v
JOIN accounting.voucher_line l ON l.voucher_id = v.voucher_id
WHERE v.status = 'Posted';

-- Each sub class (ledger head) with its place in the chart. The parent class code gives the nature:
-- 1 Assets, 2 Liabilities, 3 Equity, 4 Income, 5 Expenses.
CREATE OR REPLACE VIEW accounting.ledger_head AS
SELECT s.acc_sub_class_id, trim(s.sub_code) AS sub_code, s.sub_name,
       mp.acc_map_class_id, trim(mp.map_code) AS map_code, mp.map_name,
       mc.acc_main_class_id, trim(mc.main_code) AS main_code, mc.main_name, mc.mc_display_order,
       ac.acc_ac_class_id, trim(ac.ac_code) AS ac_code, ac.class_name,
       p.acc_ac_parent_class_id, trim(p.parent_code) AS parent_code, p.name AS parent_name,
       CASE trim(p.parent_code)
           WHEN '1' THEN 'Asset'
           WHEN '2' THEN 'Liability'
           WHEN '3' THEN 'Equity'
           WHEN '4' THEN 'Income'
           WHEN '5' THEN 'Expense'
       END AS nature
FROM accounting.acc_sub_class s
JOIN accounting.acc_map_class mp ON mp.acc_map_class_id = s.acc_map_class_id
JOIN accounting.acc_main_class mc ON mc.acc_main_class_id = mp.acc_main_class_id
JOIN accounting.acc_ac_class ac ON ac.acc_ac_class_id = mc.acc_ac_class_id
JOIN accounting.acc_ac_parent_class p ON p.acc_ac_parent_class_id = ac.acc_ac_parent_class_id;

COMMIT;
