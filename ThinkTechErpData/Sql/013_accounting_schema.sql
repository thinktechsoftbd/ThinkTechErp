-- Moves the accounting tables (chart of account classes and vouchers) from the security schema
-- to the accounting schema. ALTER TABLE ... SET SCHEMA keeps the rows, primary keys, indexes,
-- foreign keys (including acc_voucher -> security."Payment") and the id sequences the columns own,
-- whose current values carry over.
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE SCHEMA IF NOT EXISTS accounting;

DO $$
DECLARE
    table_name TEXT;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'acc_ac_parent_class',
        'acc_ac_class',
        'acc_main_class',
        'acc_map_class',
        'acc_sub_class',
        'acc_voucher',
        'acc_voucher_detail']
    LOOP
        IF to_regclass(format('security.%I', table_name)) IS NULL THEN
            CONTINUE;   -- already moved
        END IF;
        IF to_regclass(format('accounting.%I', table_name)) IS NOT NULL THEN
            RAISE EXCEPTION 'Both security.% and accounting.% exist; merge them by hand first.', table_name, table_name;
        END IF;
        EXECUTE format('ALTER TABLE security.%I SET SCHEMA accounting', table_name);
    END LOOP;
END $$;

COMMIT;
