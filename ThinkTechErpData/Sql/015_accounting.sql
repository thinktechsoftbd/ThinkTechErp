-- Accounting module (run after 009, 010 and 011): the chart of accounts, double-entry vouchers and
-- the menus and permissions to reach them.
--
--   * accounting.chart_account: the chart of accounts, five levels deep, per organization:
--       1 Parent Class   (Assets, Liabilities, Equity, Income, Expenses)
--       2 Account Class  (Current Assets, ...)
--       3 Main Class     (Cash & Cash Equivalents, ...)
--       4 Map Class      (Cash at Bank, ...)
--       5 Sub Class      the ledger heads vouchers post to (Dutch-Bangla Bank A/C 1234, ...)
--     Every level keeps the nature of its parent class, so reports never climb the tree to find it.
--     A sub class is General, Cash or Bank (Cash and Bank heads are what bills are paid from), and
--     may hold a subsidiary ledger of suppliers or customers (Accounts Payable, Accounts Receivable):
--     lines posted to it then name the supplier or customer.
--   * accounting.voucher / voucher_line: Journal, Payment, Receipt and Contra vouchers, plus the
--     Supplier Bill Payment and Customer Bill Collection vouchers. A voucher is saved as a draft,
--     posted when debits equal credits, and cancelled (never edited or deleted) once posted.
--   * accounting.ledger_entry: the posted lines, which every accounting report reads.
--
-- The legacy acc_* tables 013 moved into this schema are left as they are.
-- Conventions as in 009: UTC timestamps, NUMERIC(18, 4) money, codes unique per organization.
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE SCHEMA IF NOT EXISTS accounting;

-- =====================================================================
-- Chart of accounts
-- =====================================================================

CREATE TABLE IF NOT EXISTS accounting.chart_account
(
    account_id        INT GENERATED ALWAYS AS IDENTITY,
    organization_id   INT NOT NULL,
    parent_account_id INT NULL,
    -- 1 Parent Class, 2 Account Class, 3 Main Class, 4 Map Class, 5 Sub Class (ledger head)
    account_level     SMALLINT NOT NULL,
    account_code      VARCHAR(30) NOT NULL,
    account_name      VARCHAR(150) NOT NULL,
    -- Set on the parent class and copied to every level below it.
    nature            VARCHAR(20) NOT NULL,
    -- Sub classes only: General, Cash or Bank.
    ledger_type       VARCHAR(20) NULL,
    -- Sub classes only: Supplier or Customer when the head keeps a subsidiary ledger of them.
    party_type        VARCHAR(20) NULL,
    sort_order        INT NOT NULL DEFAULT 0,
    description       VARCHAR(500) NULL,
    is_active         BOOLEAN NOT NULL DEFAULT TRUE,

    created_at        TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by        INT NULL,
    updated_at        TIMESTAMP(3) NULL,
    updated_by        INT NULL,

    CONSTRAINT pk_chart_account PRIMARY KEY (account_id),
    CONSTRAINT fk_chart_account_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_chart_account_parent
        FOREIGN KEY (parent_account_id) REFERENCES accounting.chart_account (account_id),
    CONSTRAINT ck_chart_account_level CHECK (account_level BETWEEN 1 AND 5),
    CONSTRAINT ck_chart_account_parent CHECK ((account_level = 1) = (parent_account_id IS NULL)),
    CONSTRAINT ck_chart_account_nature
        CHECK (nature IN ('Asset', 'Liability', 'Equity', 'Income', 'Expense')),
    CONSTRAINT ck_chart_account_ledger_type
        CHECK ((account_level = 5) = (ledger_type IS NOT NULL) AND (ledger_type IS NULL OR ledger_type IN ('General', 'Cash', 'Bank'))),
    CONSTRAINT ck_chart_account_party_type
        CHECK (party_type IS NULL OR (account_level = 5 AND ledger_type = 'General' AND party_type IN ('Supplier', 'Customer')))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_chart_account_code
    ON accounting.chart_account (organization_id, lower(account_code));
CREATE INDEX IF NOT EXISTS idx_chart_account_parent
    ON accounting.chart_account (parent_account_id);
CREATE INDEX IF NOT EXISTS idx_chart_account_level
    ON accounting.chart_account (organization_id, account_level);

-- =====================================================================
-- Vouchers
-- =====================================================================

CREATE TABLE IF NOT EXISTS accounting.voucher
(
    voucher_id      BIGINT GENERATED ALWAYS AS IDENTITY,
    object_id       uuid NOT NULL DEFAULT gen_random_uuid(),
    organization_id INT NOT NULL,
    voucher_no      VARCHAR(30) NOT NULL,
    -- Journal, Payment, Receipt, Contra, BillPayment (to a supplier), BillCollection (from a customer)
    voucher_type    VARCHAR(20) NOT NULL,
    voucher_date    DATE NOT NULL,
    -- The supplier paid (BillPayment) or the customer collected from (BillCollection).
    supplier_id     INT NULL,
    customer_id     INT NULL,
    -- Bill payments and collections: Cash, Bank, Cheque, MobileWallet, Card or Other.
    payment_method  VARCHAR(20) NULL,
    -- Cheque, bank transfer or wallet transaction number; the supplier's bill number; ...
    reference_no    VARCHAR(100) NULL,
    cheque_date     DATE NULL,
    narration       VARCHAR(500) NULL,
    -- The sum of the debits (= the sum of the credits once posted).
    total_amount    NUMERIC(18, 4) NOT NULL DEFAULT 0,
    status          VARCHAR(20) NOT NULL DEFAULT 'Draft',
    posted_at       TIMESTAMP(3) NULL,
    posted_by       INT NULL,
    cancelled_at    TIMESTAMP(3) NULL,
    cancelled_by    INT NULL,

    created_at      TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by      INT NULL,
    updated_at      TIMESTAMP(3) NULL,
    updated_by      INT NULL,

    CONSTRAINT pk_voucher PRIMARY KEY (voucher_id),
    CONSTRAINT fk_voucher_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id),
    CONSTRAINT fk_voucher_supplier FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_voucher_customer FOREIGN KEY (customer_id) REFERENCES sales.customer (customer_id),
    CONSTRAINT ck_voucher_type
        CHECK (voucher_type IN ('Journal', 'Payment', 'Receipt', 'Contra', 'BillPayment', 'BillCollection')),
    CONSTRAINT ck_voucher_status CHECK (status IN ('Draft', 'Posted', 'Cancelled')),
    CONSTRAINT ck_voucher_payment_method
        CHECK (payment_method IS NULL OR payment_method IN ('Cash', 'Bank', 'Cheque', 'MobileWallet', 'Card', 'Other')),
    CONSTRAINT ck_voucher_party CHECK (supplier_id IS NULL OR customer_id IS NULL),
    CONSTRAINT ck_voucher_bill_payment CHECK (voucher_type <> 'BillPayment' OR supplier_id IS NOT NULL),
    CONSTRAINT ck_voucher_bill_collection CHECK (voucher_type <> 'BillCollection' OR customer_id IS NOT NULL),
    CONSTRAINT ck_voucher_total CHECK (total_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_voucher_no
    ON accounting.voucher (organization_id, lower(voucher_no));
CREATE INDEX IF NOT EXISTS idx_voucher_date
    ON accounting.voucher (organization_id, voucher_date);
CREATE INDEX IF NOT EXISTS idx_voucher_supplier
    ON accounting.voucher (supplier_id) WHERE supplier_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_voucher_customer
    ON accounting.voucher (customer_id) WHERE customer_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS accounting.voucher_line
(
    voucher_line_id BIGINT GENERATED ALWAYS AS IDENTITY,
    voucher_id      BIGINT NOT NULL,
    line_no         INT NOT NULL,
    -- A sub class (ledger head).
    account_id      INT NOT NULL,
    -- Set when the ledger head keeps a subsidiary ledger of suppliers or customers.
    supplier_id     INT NULL,
    customer_id     INT NULL,
    debit_amount    NUMERIC(18, 4) NOT NULL DEFAULT 0,
    credit_amount   NUMERIC(18, 4) NOT NULL DEFAULT 0,
    line_narration  VARCHAR(300) NULL,

    CONSTRAINT pk_voucher_line PRIMARY KEY (voucher_line_id),
    CONSTRAINT fk_voucher_line_voucher
        FOREIGN KEY (voucher_id) REFERENCES accounting.voucher (voucher_id) ON DELETE CASCADE,
    CONSTRAINT fk_voucher_line_account FOREIGN KEY (account_id) REFERENCES accounting.chart_account (account_id),
    CONSTRAINT fk_voucher_line_supplier FOREIGN KEY (supplier_id) REFERENCES purchase.supplier (supplier_id),
    CONSTRAINT fk_voucher_line_customer FOREIGN KEY (customer_id) REFERENCES sales.customer (customer_id),
    CONSTRAINT ux_voucher_line UNIQUE (voucher_id, line_no),
    -- Each line is either a debit or a credit.
    CONSTRAINT ck_voucher_line_amount
        CHECK (debit_amount >= 0 AND credit_amount >= 0 AND (debit_amount = 0) <> (credit_amount = 0)),
    CONSTRAINT ck_voucher_line_party CHECK (supplier_id IS NULL OR customer_id IS NULL)
);

CREATE INDEX IF NOT EXISTS idx_voucher_line_account ON accounting.voucher_line (account_id);
CREATE INDEX IF NOT EXISTS idx_voucher_line_supplier
    ON accounting.voucher_line (supplier_id) WHERE supplier_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_voucher_line_customer
    ON accounting.voucher_line (customer_id) WHERE customer_id IS NOT NULL;

-- The general ledger: every line of every posted voucher.
CREATE OR REPLACE VIEW accounting.ledger_entry AS
SELECT v.organization_id, v.voucher_id, v.voucher_no, v.voucher_type, v.voucher_date, v.reference_no,
       v.narration, l.voucher_line_id, l.line_no, l.account_id, l.supplier_id, l.customer_id,
       l.debit_amount, l.credit_amount, l.line_narration
FROM accounting.voucher v
JOIN accounting.voucher_line l ON l.voucher_id = v.voucher_id
WHERE v.status = 'Posted';

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO security."Permission" (code, name, description)
VALUES ('ACCOUNTING.V', 'View Accounting', 'View the chart of accounts, vouchers and accounting reports'),
       ('ACCOUNTING.M', 'Manage Accounting', 'Change the chart of accounts; enter, post and cancel vouchers, bill payments and collections')
ON CONFLICT (code) DO NOTHING;

INSERT INTO security."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM security."Role" r
JOIN security."Permission" p ON p.code IN ('ACCOUNTING.V', 'ACCOUNTING.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menus: Accounting, with Settings, Transactions and Reports
-- =====================================================================

DO $$
DECLARE
    super_admin int := (SELECT id FROM security."Role" WHERE name = 'Super Admin');
    next_top int := (SELECT coalesce(max("sortOrder"), 0) FROM security."Menu" WHERE "parentId" IS NULL);
    module_id int := (SELECT id FROM security."Menu" WHERE title = 'Accounting' AND "parentId" IS NULL);
    group_menu record;
    group_id int;
    item_order int;
    item text[];
BEGIN
    IF module_id IS NULL THEN
        INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        VALUES ('Accounting', '#', 'calculator', next_top + 1, NULL, super_admin, true)
        RETURNING id INTO module_id;
    END IF;

    FOR group_menu IN
        SELECT * FROM (VALUES
            (1, 'Settings', 'settings'),
            (2, 'Transactions', 'arrow-left-right'),
            (3, 'Reports', 'chart-column')) AS g(position, title, icon)
        ORDER BY position
    LOOP
        SELECT id INTO group_id FROM security."Menu" WHERE title = group_menu.title AND "parentId" = module_id;
        IF group_id IS NULL THEN
            INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (group_menu.title, '#', group_menu.icon, group_menu.position, module_id, super_admin, true)
            RETURNING id INTO group_id;
        END IF;

        item_order := 0;
        FOR item IN
            SELECT ARRAY[i.title, i.path, i.icon] FROM (VALUES
                ('Settings', 1, 'Chart of Accounts', '/accounting/chart-of-accounts', 'list-tree'),
                ('Transactions', 1, 'Vouchers', '/accounting/vouchers', 'notebook-pen'),
                ('Transactions', 2, 'Bill Payment', '/accounting/bill-payments', 'banknote-arrow-up'),
                ('Transactions', 3, 'Bill Collection', '/accounting/bill-collections', 'hand-coins'),
                ('Reports', 1, 'Subsidiary Ledger', '/accounting/reports/subsidiary-ledger', 'book-open'),
                ('Reports', 2, 'Trial Balance', '/accounting/reports/trial-balance', 'scale'),
                ('Reports', 3, 'Balance Sheet', '/accounting/reports/balance-sheet', 'landmark'))
                AS i(grp, position, title, path, icon)
            WHERE i.grp = group_menu.title
            ORDER BY i.position
        LOOP
            item_order := item_order + 1;
            IF NOT EXISTS (SELECT 1 FROM security."Menu" WHERE path = item[2]) THEN
                INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
                VALUES (item[1], item[2], item[3], item_order, group_id, super_admin, true);
            END IF;
        END LOOP;
        group_id := NULL;
    END LOOP;
END $$;

COMMIT;
