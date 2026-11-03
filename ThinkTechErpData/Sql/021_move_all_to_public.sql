-- One schema: every table (with its data), view, sequence, type and function in the accounting,
-- inventory, purchase, sales, security, sms and website schemas moves into public, and the emptied
-- schemas are dropped. Run after 020.
--
--   * ALTER ... SET SCHEMA moves objects in place: no data is copied, and each table takes its
--     indexes, constraints, triggers, identity/serial sequences and row type along. Foreign keys and
--     views refer to tables by id, so they keep working.
--   * Two tables share a name: purchase.invoice_allocation and sales.invoice_allocation. They become
--     public.purchase_invoice_allocation and public.sales_invoice_allocation (with their id sequences).
--     Every other object keeps its name.
--   * Before anything moves, the script stops (and changes nothing) if:
--       - another name would clash in public (say, a table that exists both in public and in security);
--       - a SQL or PL/pgSQL function names one of the old schemas in its body, since that text would not
--         follow the move.
--     The error lists what to fix.
--   * The API names public in all its SQL from this release on, so deploy it together with this script.
--
-- The script is idempotent: once the schemas are gone, running it again changes nothing.

BEGIN;

-- =====================================================================
-- 1. The one name two schemas share
-- =====================================================================

DO $$
DECLARE
    side text;
    seq text;
BEGIN
    FOREACH side IN ARRAY ARRAY['purchase', 'sales']
    LOOP
        IF to_regclass(side || '.invoice_allocation') IS NOT NULL THEN
            seq := pg_get_serial_sequence(side || '.invoice_allocation', 'invoice_allocation_id');
            EXECUTE format('ALTER TABLE %I.invoice_allocation RENAME TO %I', side, side || '_invoice_allocation');
            IF seq IS NOT NULL THEN
                EXECUTE format('ALTER SEQUENCE %s RENAME TO %I', seq, side || '_invoice_allocation_invoice_allocation_id_seq');
            END IF;
        END IF;
    END LOOP;
END $$;

-- =====================================================================
-- 2. Checks: nothing has moved yet
-- =====================================================================

DO $$
DECLARE
    schemas text[] := ARRAY['accounting', 'inventory', 'purchase', 'sales', 'security', 'sms', 'website'];
    clashes text;
    routines text;
BEGIN
    -- Tables, views, indexes, sequences and composite types share one namespace per schema; types and
    -- functions have their own. Array types are left out: Postgres renames those itself.
    SELECT string_agg(format('%s (in %s)', name, places), '; ' ORDER BY name) INTO clashes
    FROM (
        SELECT c.relname AS name, string_agg(n.nspname, ', ' ORDER BY n.nspname) AS places
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = ANY (schemas || 'public'::text)
        GROUP BY c.relname
        HAVING count(*) > 1
        UNION ALL
        SELECT t.typname, string_agg(n.nspname, ', ' ORDER BY n.nspname)
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = ANY (schemas || 'public'::text) AND t.typtype IN ('e', 'd', 'r', 'm')
        GROUP BY t.typname
        HAVING count(*) > 1
        UNION ALL
        SELECT format('%s(%s)', p.proname, pg_get_function_identity_arguments(p.oid)),
               string_agg(n.nspname, ', ' ORDER BY n.nspname)
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = ANY (schemas || 'public'::text)
        GROUP BY 1
        HAVING count(*) > 1
    ) x;
    IF clashes IS NOT NULL THEN
        RAISE EXCEPTION 'These names exist in more than one schema, so they can''t all move into public. Rename or drop one of each and run this again: %', clashes;
    END IF;

    SELECT string_agg(format('%s.%s(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)), '; ') INTO routines
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    JOIN pg_language l ON l.oid = p.prolang
    WHERE l.lanname IN ('sql', 'plpgsql')
      AND n.nspname NOT IN ('pg_catalog', 'information_schema')
      AND p.prosrc ~* ('\m(' || array_to_string(schemas, '|') || ')\s*\.');
    IF routines IS NOT NULL THEN
        RAISE EXCEPTION 'These functions name an old schema in their code, which won''t follow the move. Change them to public and run this again: %', routines;
    END IF;
END $$;

-- =====================================================================
-- 3. Move everything into public
-- =====================================================================

DO $$
DECLARE
    schemas text[] := ARRAY['accounting', 'inventory', 'purchase', 'sales', 'security', 'sms', 'website'];
    obj record;
    moved int := 0;
BEGIN
    -- Tables (partitions and foreign tables too) first: each brings its indexes, constraints, triggers,
    -- owned sequences and row type.
    FOR obj IN
        SELECT n.nspname, c.relname, c.relkind
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = ANY (schemas) AND c.relkind IN ('r', 'p', 'f')
        ORDER BY n.nspname, c.relname
    LOOP
        EXECUTE format('ALTER %s %I.%I SET SCHEMA public',
                       CASE obj.relkind WHEN 'f' THEN 'FOREIGN TABLE' ELSE 'TABLE' END, obj.nspname, obj.relname);
        moved := moved + 1;
    END LOOP;

    -- Views, materialized views, sequences no column owns, and composite types.
    FOR obj IN
        SELECT n.nspname, c.relname, c.relkind
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = ANY (schemas) AND c.relkind IN ('v', 'm', 'S', 'c')
        ORDER BY n.nspname, c.relname
    LOOP
        EXECUTE format('ALTER %s %I.%I SET SCHEMA public',
                       CASE obj.relkind
                           WHEN 'v' THEN 'VIEW'
                           WHEN 'm' THEN 'MATERIALIZED VIEW'
                           WHEN 'S' THEN 'SEQUENCE'
                           ELSE 'TYPE'
                       END, obj.nspname, obj.relname);
        moved := moved + 1;
    END LOOP;

    -- Enums (e.g. Prisma's "UserStatus"), domains and ranges (a range brings its multirange).
    FOR obj IN
        SELECT n.nspname, t.typname, t.typtype
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = ANY (schemas) AND t.typtype IN ('e', 'd', 'r')
        ORDER BY n.nspname, t.typname
    LOOP
        EXECUTE format('ALTER %s %I.%I SET SCHEMA public',
                       CASE obj.typtype WHEN 'd' THEN 'DOMAIN' ELSE 'TYPE' END, obj.nspname, obj.typname);
        moved := moved + 1;
    END LOOP;

    -- Functions and procedures (their bodies were checked above).
    FOR obj IN
        SELECT p.oid::regprocedure AS routine, p.prokind
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = ANY (schemas)
    LOOP
        EXECUTE format('ALTER %s %s SET SCHEMA public',
                       CASE obj.prokind WHEN 'a' THEN 'AGGREGATE' ELSE 'ROUTINE' END, obj.routine);
        moved := moved + 1;
    END LOOP;

    RAISE NOTICE 'Moved % objects into public.', moved;
END $$;

-- =====================================================================
-- 4. Drop the emptied schemas
-- =====================================================================

-- Without CASCADE: if anything is still in one of them, this fails and the whole script rolls back.
DROP SCHEMA IF EXISTS accounting;
DROP SCHEMA IF EXISTS inventory;
DROP SCHEMA IF EXISTS purchase;
DROP SCHEMA IF EXISTS sales;
DROP SCHEMA IF EXISTS security;
DROP SCHEMA IF EXISTS sms;
DROP SCHEMA IF EXISTS website;

COMMIT;
