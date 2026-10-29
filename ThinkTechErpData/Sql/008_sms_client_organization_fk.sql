-- SMS clients are organizations: sms.client_wallets, sms.client_allocations and sms.usage_records
-- lose their client_id column (and its foreign key to sms.clients) and get organization_id instead,
-- a foreign key to security.organization(organization_id). One wallet per organization:
-- organization_id becomes the primary key of sms.client_wallets.
--
-- Existing rows keep their link: organization_id is looked up from client_id, which must be the
-- object_id of an existing organization. If client_id is already gone and an object_id column was
-- added by hand instead, object_id is used the same way. The first block stops with the values
-- that match no organization; the whole script runs in one transaction, so nothing changes until
-- every step succeeds.
--
-- Run it together with the matching changes to the SMS functions (register_client, allocate_sms,
-- consume_sms) and the API, which still read and write client_id in these tables.
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

-- =====================================================================
-- 1. Add organization_id and fill it from client_id (or object_id)
-- =====================================================================

ALTER TABLE sms.client_wallets     ADD COLUMN IF NOT EXISTS organization_id int4;
ALTER TABLE sms.client_allocations ADD COLUMN IF NOT EXISTS organization_id int4;
ALTER TABLE sms.usage_records      ADD COLUMN IF NOT EXISTS organization_id int4;

DO $fill$
DECLARE
    v_table  text;
    v_source text;
    v_bad    text;
BEGIN
    FOREACH v_table IN ARRAY ARRAY['client_wallets', 'client_allocations', 'usage_records']
    LOOP
        -- client_id is the original link. ::text below: it may already be uuid, changed by hand.
        SELECT c.column_name
          INTO v_source
          FROM information_schema.columns c
         WHERE c.table_schema = 'sms'
           AND c.table_name = v_table
           AND c.column_name IN ('client_id', 'object_id')
         ORDER BY c.column_name = 'client_id' DESC
         LIMIT 1;

        -- Neither column left: already migrated by an earlier run.
        CONTINUE WHEN v_source IS NULL;

        EXECUTE format($sql$
            SELECT string_agg(bad.value, ', ')
              FROM (SELECT DISTINCT t.%1$I::text AS value
                      FROM sms.%2$I t
                     WHERE NOT EXISTS (SELECT 1
                                         FROM security.organization o
                                        WHERE o.object_id::text = lower(t.%1$I::text))
                     ORDER BY 1
                     LIMIT 10) bad
            $sql$, v_source, v_table)
          INTO v_bad;

        IF v_bad IS NOT NULL THEN
            RAISE EXCEPTION 'sms.% has % values that are not an organization object_id: %', v_table, v_source, v_bad
                USING HINT = 'Set those rows to the organization''s object_id (or delete test rows), then run this script again.';
        END IF;

        EXECUTE format($sql$
            UPDATE sms.%2$I t
               SET organization_id = o.organization_id
              FROM security.organization o
             WHERE o.object_id::text = lower(t.%1$I::text)
               AND t.organization_id IS DISTINCT FROM o.organization_id
            $sql$, v_source, v_table);
    END LOOP;
END;
$fill$;

ALTER TABLE sms.client_wallets     ALTER COLUMN organization_id SET NOT NULL;
ALTER TABLE sms.client_allocations ALTER COLUMN organization_id SET NOT NULL;
ALTER TABLE sms.usage_records      ALTER COLUMN organization_id SET NOT NULL;

-- =====================================================================
-- 2. Remove client_id (and a hand-added object_id)
-- =====================================================================

-- client_balance_summary uses sms.client_wallets.client_id, and PostgreSQL cannot drop a column
-- a view uses. It is dropped here and recreated in step 5.
DROP VIEW IF EXISTS sms.client_balance_summary;

-- Dropping a column also drops what is built on it: the foreign keys to sms.clients, the
-- client_wallets primary key and ix_sms_client_allocations_client_date.
ALTER TABLE sms.client_wallets     DROP COLUMN IF EXISTS client_id, DROP COLUMN IF EXISTS object_id;
ALTER TABLE sms.client_allocations DROP COLUMN IF EXISTS client_id, DROP COLUMN IF EXISTS object_id;
ALTER TABLE sms.usage_records      DROP COLUMN IF EXISTS client_id, DROP COLUMN IF EXISTS object_id;

-- =====================================================================
-- 3. Primary key and indexes on organization_id
-- =====================================================================

DO $pk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = 'sms.client_wallets'::regclass
                      AND contype = 'p') THEN
        ALTER TABLE sms.client_wallets
            ADD CONSTRAINT client_wallets_pkey PRIMARY KEY (organization_id);
    END IF;
END;
$pk$;

CREATE INDEX IF NOT EXISTS ix_sms_client_allocations_organization_date
    ON sms.client_allocations (organization_id, allocated_at DESC);

CREATE INDEX IF NOT EXISTS ix_sms_usage_records_organization
    ON sms.usage_records (organization_id);

-- =====================================================================
-- 4. Foreign keys to security.organization(organization_id)
-- =====================================================================

-- ON DELETE RESTRICT: an organization with an SMS wallet or history cannot be deleted.
DO $fk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_client_wallets_organization'
                      AND conrelid = 'sms.client_wallets'::regclass) THEN
        ALTER TABLE sms.client_wallets
            ADD CONSTRAINT fk_client_wallets_organization
            FOREIGN KEY (organization_id)
            REFERENCES security.organization (organization_id)
            ON UPDATE CASCADE
            ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_client_allocations_organization'
                      AND conrelid = 'sms.client_allocations'::regclass) THEN
        ALTER TABLE sms.client_allocations
            ADD CONSTRAINT fk_client_allocations_organization
            FOREIGN KEY (organization_id)
            REFERENCES security.organization (organization_id)
            ON UPDATE CASCADE
            ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_usage_records_organization'
                      AND conrelid = 'sms.usage_records'::regclass) THEN
        ALTER TABLE sms.usage_records
            ADD CONSTRAINT fk_usage_records_organization
            FOREIGN KEY (organization_id)
            REFERENCES security.organization (organization_id)
            ON UPDATE CASCADE
            ON DELETE RESTRICT;
    END IF;
END;
$fk$;

-- =====================================================================
-- 5. Recreate client_balance_summary
-- =====================================================================

-- Same columns and types as before, so the API's client list keeps working until it moves to
-- organizations. sms.clients.client_id is still text and holds the organization's object_id.
CREATE VIEW sms.client_balance_summary AS
SELECT c.client_id,
       c.display_name,
       c.is_active,
       COALESCE(w.balance, 0) AS available_sms,
       COALESCE(w.total_allocated, 0) AS total_allocated_sms,
       COALESCE(w.total_consumed, 0) AS total_consumed_sms,
       c.created_at,
       c.updated_at
  FROM sms.clients c
  LEFT JOIN security.organization o ON o.object_id::text = lower(c.client_id)
  LEFT JOIN sms.client_wallets w ON w.organization_id = o.organization_id;

COMMIT;
