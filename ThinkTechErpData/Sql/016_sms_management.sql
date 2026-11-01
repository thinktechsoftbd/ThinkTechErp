-- Replaces the SMS module (run after 011; replaces the old 008):
--   1. Drops the old sms schema objects (clients, wallets, ADN purchases, balance transactions, usage
--      records, their views and functions). The schema itself and its grants are kept.
--   2. Creates the multi-provider design (sms_management.sql): providers, purchases from them, and
--      provider-wise allocations to organizations, plus the organization's total SMS usage.
--   3. SMS.V / SMS.M permissions for Super Admin and sms_admin, and the SMS menus.
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE SCHEMA IF NOT EXISTS sms;

-- ---------------------------------------------------------------------
-- 1. The old design
-- ---------------------------------------------------------------------

DROP VIEW IF EXISTS sms.client_balance_summary;
DROP VIEW IF EXISTS sms.platform_balance_summary;

DROP FUNCTION IF EXISTS sms.allocate_sms(text, text, bigint, text);
DROP FUNCTION IF EXISTS sms.allocate_sms(text, text, bigint, text, boolean);
DROP FUNCTION IF EXISTS sms.consume_sms(text, bigint, text, text, text);
DROP FUNCTION IF EXISTS sms.finalize_allocation(bigint);
DROP FUNCTION IF EXISTS sms.record_adn_purchase(bigint, text, numeric, character, numeric, timestamp with time zone, text);
DROP FUNCTION IF EXISTS sms.record_adn_purchase(bigint, text, numeric, character, timestamp with time zone, text);
DROP FUNCTION IF EXISTS sms.register_client(text, text);
DROP FUNCTION IF EXISTS sms.update_adn_purchase(bigint, text, numeric, character, numeric, text);
DROP FUNCTION IF EXISTS sms.update_allocation_note(bigint, text);
DROP FUNCTION IF EXISTS sms.update_client(text, text, boolean);

DROP TABLE IF EXISTS sms.usage_records;
DROP TABLE IF EXISTS sms.client_allocations;
DROP TABLE IF EXISTS sms.client_wallets;
DROP TABLE IF EXISTS sms.adn_purchases;
DROP TABLE IF EXISTS sms.balance_transactions;
DROP TABLE IF EXISTS sms.clients;
DROP TABLE IF EXISTS sms.platform_wallet;

-- ---------------------------------------------------------------------
-- 2. Multi-provider SMS management (from sms_management.sql)
-- ---------------------------------------------------------------------
-- local_exchange_rate is the BDT conversion rate per unit of the purchase currency;
-- local_total_amount is a purchase-time snapshot. Balances are checked by the API inside
-- a transaction (provider and organization rows are locked first).

CREATE TABLE IF NOT EXISTS sms.sms_provider (
    sms_provider_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    object_id UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    provider_name VARCHAR(150) NOT NULL,
    provider_code VARCHAR(50) NOT NULL UNIQUE,

    api_base_url VARCHAR(500),
    api_username VARCHAR(150),
    api_password TEXT,
    api_key TEXT,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by INT NULL,
    updated_at TIMESTAMP(3) NULL,
    updated_by INT NULL
);

CREATE TABLE IF NOT EXISTS sms.sms_purchase (
    sms_purchase_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    object_id UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    sms_provider_id INT NOT NULL,

    purchase_date DATE NOT NULL DEFAULT CURRENT_DATE,
    invoice_number VARCHAR(100),
    reference_number VARCHAR(150),

    sms_quantity BIGINT NOT NULL,

    currency_id INT NOT NULL,
    unit_price NUMERIC(18,6) NOT NULL DEFAULT 0,
    total_amount NUMERIC(18,2) NOT NULL DEFAULT 0,
    local_exchange_rate NUMERIC(18,6) NOT NULL DEFAULT 1,
    local_total_amount NUMERIC(18,2) NOT NULL DEFAULT 0,

    remarks TEXT,

    created_at TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by INT NULL,
    updated_at TIMESTAMP(3) NULL,
    updated_by INT NULL,

    CONSTRAINT fk_sms_purchase_provider
        FOREIGN KEY (sms_provider_id)
        REFERENCES sms.sms_provider(sms_provider_id),

    CONSTRAINT fk_sms_purchase_currency
        FOREIGN KEY (currency_id)
        REFERENCES public.lookup_data(lookup_data_id),

    CONSTRAINT chk_sms_purchase_quantity
        CHECK (sms_quantity > 0),

    CONSTRAINT chk_sms_purchase_unit_price
        CHECK (unit_price >= 0),

    CONSTRAINT chk_sms_purchase_exchange_rate
        CHECK (local_exchange_rate > 0),

    CONSTRAINT chk_sms_purchase_amount
        CHECK (total_amount >= 0 AND local_total_amount >= 0)
);

CREATE INDEX IF NOT EXISTS idx_sms_purchase_provider
    ON sms.sms_purchase(sms_provider_id);

CREATE INDEX IF NOT EXISTS idx_sms_purchase_date
    ON sms.sms_purchase(purchase_date);

CREATE TABLE IF NOT EXISTS sms.sms_allocation (
    sms_allocation_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    object_id UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),

    sms_provider_id INT NOT NULL,
    organization_id UUID NOT NULL,

    allocated_sms BIGINT NOT NULL,

    allocation_date DATE NOT NULL DEFAULT CURRENT_DATE,
    remarks TEXT,

    created_at TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by INT NULL,
    updated_at TIMESTAMP(3) NULL,
    updated_by INT NULL,

    CONSTRAINT fk_sms_allocation_provider
        FOREIGN KEY (sms_provider_id)
        REFERENCES sms.sms_provider(sms_provider_id),

    CONSTRAINT fk_sms_allocation_organization
        FOREIGN KEY (organization_id)
        REFERENCES security.organization(object_id),

    CONSTRAINT chk_sms_allocation_quantity
        CHECK (allocated_sms > 0)
);

CREATE INDEX IF NOT EXISTS idx_sms_allocation_provider
    ON sms.sms_allocation(sms_provider_id);

CREATE INDEX IF NOT EXISTS idx_sms_allocation_organization
    ON sms.sms_allocation(organization_id);

-- The organization's total SMS sent, against everything allocated to it.
ALTER TABLE security.organization
    ADD COLUMN IF NOT EXISTS sms_usage BIGINT NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'chk_organization_sms_usage'
          AND conrelid = 'security.organization'::regclass
    ) THEN
        ALTER TABLE security.organization
            ADD CONSTRAINT chk_organization_sms_usage
            CHECK (sms_usage >= 0);
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 3. Access: permissions and menus
-- ---------------------------------------------------------------------

INSERT INTO security."Permission" (code, name, description)
VALUES ('SMS.V', 'View SMS', 'View SMS providers, purchases, allocations and organization balances'),
       ('SMS.M', 'Manage SMS', 'Add and edit SMS providers, purchases and allocations; record organization usage')
ON CONFLICT (code) DO NOTHING;

INSERT INTO security."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM security."Role" r
JOIN security."Permission" p ON p.code IN ('SMS.V', 'SMS.M')
WHERE r.name IN ('Super Admin', 'sms_admin')
ON CONFLICT DO NOTHING;

UPDATE security."Role"
SET description = 'SMS administration: providers, purchases, allocations and organization balances'
WHERE name = 'sms_admin';

DO $$
DECLARE
    sms_menu int := (SELECT id FROM security."Menu" WHERE title = 'SMS' AND "parentId" IS NULL);
    sms_role int := coalesce((SELECT id FROM security."Role" WHERE name = 'sms_admin'),
                             (SELECT id FROM security."Role" WHERE name = 'Super Admin'));
    item record;
BEGIN
    IF sms_menu IS NULL THEN
        INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        VALUES ('SMS', '#', 'message-square-text',
                (SELECT coalesce(max("sortOrder"), 0) + 1 FROM security."Menu" WHERE "parentId" IS NULL),
                NULL, sms_role, true)
        RETURNING id INTO sms_menu;
    END IF;

    -- Screens of the old design.
    DELETE FROM security."Menu" WHERE path IN ('/sms/clients', '/sms/usage', '/sms/transactions');

    FOR item IN
        SELECT * FROM (VALUES
            (1, 'Overview', '/sms', 'layout-dashboard'),
            (2, 'Providers', '/sms/providers', 'radio-tower'),
            (3, 'Purchases', '/sms/purchases', 'shopping-cart'),
            (4, 'Allocations', '/sms/allocations', 'arrow-left-right'),
            (5, 'Organizations', '/sms/organizations', 'building-2'))
            AS i(position, title, path, icon)
    LOOP
        IF EXISTS (SELECT 1 FROM security."Menu" WHERE path = item.path) THEN
            UPDATE security."Menu"
            SET title = item.title, icon = item.icon, "sortOrder" = item.position, "parentId" = sms_menu
            WHERE path = item.path
              AND (title, icon, "sortOrder", "parentId") IS DISTINCT FROM (item.title, item.icon, item.position, sms_menu);
        ELSE
            INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (item.title, item.path, item.icon, item.position, sms_menu, sms_role, true);
        END IF;
    END LOOP;
END $$;

COMMIT;
