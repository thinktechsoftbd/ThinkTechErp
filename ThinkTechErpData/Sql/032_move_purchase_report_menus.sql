-- Moves the purchase reports into Inventory -> Reports, after Customer Ledger:
--   ..., Customer Dues, Customer Ledger, Supplier Ledger, Supplier Dues, Purchase Register, Purchase by Product
-- Purchase -> Reports is left empty, so it is hidden (isActive = false) rather than deleted.
-- Menus are found by path and title, not id. Run after 031_move_purchase_settings_menus.sql; the script is idempotent.

BEGIN;

DO $$
DECLARE
    inventory_reports_id INT;
    purchase_reports_id  INT;
    next_order           INT;
    menu_path            TEXT;
BEGIN
    SELECT r.id INTO inventory_reports_id
    FROM public."Menu" r
    JOIN public."Menu" m ON m.id = r."parentId" AND m."parentId" IS NULL AND m.title = 'Inventory'
    WHERE r.title = 'Reports';
    IF inventory_reports_id IS NULL THEN
        RAISE EXCEPTION 'There is no Inventory -> Reports menu to move the purchase reports into.';
    END IF;

    SELECT r.id INTO purchase_reports_id
    FROM public."Menu" r
    JOIN public."Menu" m ON m.id = r."parentId" AND m."parentId" IS NULL AND m.title = 'Purchase'
    WHERE r.title = 'Reports';

    -- In this order, after what Inventory -> Reports already has; a menu already there keeps its place.
    FOREACH menu_path IN ARRAY ARRAY['/purchase/reports/supplier-ledger', '/purchase/reports/supplier-dues',
                                     '/purchase/reports/register', '/purchase/reports/by-product']
    LOOP
        IF EXISTS (SELECT 1 FROM public."Menu" WHERE path = menu_path AND "parentId" IS DISTINCT FROM inventory_reports_id) THEN
            SELECT coalesce(max("sortOrder"), 0) + 1 INTO next_order FROM public."Menu" WHERE "parentId" = inventory_reports_id;
            UPDATE public."Menu"
            SET "parentId" = inventory_reports_id, "sortOrder" = next_order
            WHERE path = menu_path;
        END IF;
    END LOOP;

    -- Nothing is left under Purchase -> Reports: hide it.
    IF purchase_reports_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM public."Menu" WHERE "parentId" = purchase_reports_id AND "isActive") THEN
        UPDATE public."Menu" SET "isActive" = false WHERE id = purchase_reports_id;
    END IF;
END;
$$;

COMMIT;
