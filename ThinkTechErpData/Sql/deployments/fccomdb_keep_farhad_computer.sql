-- Farhad Computer (fc.thinktechsoft.com, database fccomdb): keep only the Farhad Computer organization.
--
-- fccomdb was copied from thinktecherp, so it held five other organizations (M/S Akhi Enterprise, ThinkTech, Medi
-- Chamber, Dental Test Care, Rangpur Business Mart) with their test data. This deletes them and everything that
-- belongs to them: products, stock, suppliers and customers, purchases, sales, vouchers, warranty policies, API
-- clients, SMS allocations, subscriptions... found through the foreign keys, children before parents. Shared data
-- (users, roles, permissions, menus, lookups, the chart of accounts, areas) is not touched. Every user is then
-- linked to Farhad Computer as their default organization.
--
-- Run on fccomdb only, after a backup. One transaction: it deletes everything or nothing. Cannot be undone except
-- by restoring the backup.

BEGIN;

-- Deletes the rows of p_table matching p_where, after (recursively) the rows that must go first: those referencing
-- them through a NO ACTION, RESTRICT or CASCADE foreign key. SET NULL / SET DEFAULT references are left to the
-- database. Prints how many rows each step deleted.
CREATE FUNCTION pg_temp.delete_with_dependents(p_table regclass, p_where text, p_depth int DEFAULT 0)
RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    fk       record;
    has_rows boolean;
    deleted  bigint;
BEGIN
    IF p_depth > 25 THEN
        RAISE EXCEPTION 'The foreign keys of % go too deep (a cycle?); nothing was deleted.', p_table;
    END IF;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %s WHERE %s)', p_table, p_where) INTO has_rows;
    IF NOT has_rows THEN
        RETURN;
    END IF;

    FOR fk IN
        SELECT c.conrelid::regclass AS child,
               (SELECT string_agg(quote_ident(a.attname), ', ' ORDER BY k.ord)
                FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum) AS child_columns,
               (SELECT string_agg(quote_ident(a.attname), ', ' ORDER BY k.ord)
                FROM unnest(c.confkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = k.attnum) AS parent_columns
        FROM pg_constraint c
        WHERE c.contype = 'f' AND c.confrelid = p_table AND c.confdeltype IN ('a', 'r', 'c')
    LOOP
        PERFORM pg_temp.delete_with_dependents(
            fk.child,
            format('(%s) IN (SELECT %s FROM %s WHERE %s)', fk.child_columns, fk.parent_columns, p_table, p_where),
            p_depth + 1);
    END LOOP;

    EXECUTE format('DELETE FROM %s WHERE %s', p_table, p_where);
    GET DIAGNOSTICS deleted = ROW_COUNT;
    IF deleted > 0 THEN
        RAISE NOTICE '%: % row(s)', p_table, deleted;
    END IF;
END;
$$;

DO $$
DECLARE
    farhad int;
BEGIN
    SELECT organization_id INTO farhad FROM public.organization WHERE organization_slug = 'farhadcomputer';
    IF farhad IS NULL THEN
        RAISE EXCEPTION 'There is no Farhad Computer organization (slug farhadcomputer); nothing was deleted.';
    END IF;

    PERFORM pg_temp.delete_with_dependents('public.organization', format('organization_id <> %s', farhad));

    -- Every user works in Farhad Computer, by default.
    INSERT INTO public.user_organization (user_id, organization_id, is_default)
    SELECT u.id, farhad, TRUE
    FROM public."User" u
    WHERE NOT EXISTS (SELECT 1 FROM public.user_organization uo WHERE uo.user_id = u.id AND uo.organization_id = farhad);
END;
$$;

-- What is left.
SELECT organization_id, organization_name_en, organization_slug, is_active FROM public.organization ORDER BY organization_id;
SELECT uo.user_id, u.name, uo.organization_id, uo.is_default
FROM public.user_organization uo JOIN public."User" u ON u.id = uo.user_id
ORDER BY uo.user_id;

COMMIT;
