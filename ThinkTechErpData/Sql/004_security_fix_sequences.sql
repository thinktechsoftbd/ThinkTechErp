-- Moves every serial ID sequence in the security schema past the highest existing ID.
-- Needed when rows were inserted with explicit IDs (imports, seeds): until then, inserts
-- that rely on the default ID fail with a primary-key violation. Sequences are only ever
-- moved forward, so the script is safe to run again after any future import.

DO $fix$
DECLARE
    serial_column record;
    next_free bigint;
    next_planned bigint;
BEGIN
    FOR serial_column IN
        SELECT c.relname AS table_name,
               a.attname AS column_name,
               pg_get_serial_sequence(format('security.%I', c.relname), a.attname) AS sequence_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'security'
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        WHERE c.relkind = 'r'
          AND pg_get_serial_sequence(format('security.%I', c.relname), a.attname) IS NOT NULL
    LOOP
        EXECUTE format('SELECT coalesce(max(%I), 0) + 1 FROM security.%I',
                       serial_column.column_name, serial_column.table_name)
            INTO next_free;
        EXECUTE format('SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END FROM %s',
                       serial_column.sequence_name)
            INTO next_planned;

        IF next_free > next_planned THEN
            PERFORM setval(serial_column.sequence_name, next_free, false);
            RAISE NOTICE 'security.%: next id % -> %', serial_column.table_name, next_planned, next_free;
        END IF;
    END LOOP;
END;
$fix$;
