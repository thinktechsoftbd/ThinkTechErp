-- Moves the purchase setup screens into Inventory -> Settings, after Customers:
--   Inventory -> Settings: Products, Categories, Brands, Units, Warehouses, Inventory Settings, Customers,
--                          Suppliers, Tax Rates, Tax Groups, Payment Sources
-- Purchase -> Settings is left empty, so it is hidden (isActive = false) rather than deleted.
-- Menus are found by path and title, not id. Run after 030_voucher_invoice_links.sql; the script is idempotent.

BEGIN;

DO $$
DECLARE
    inventory_settings_id INT;
    purchase_settings_id  INT;
    next_order            INT;
    menu_path             TEXT;
BEGIN
    SELECT s.id INTO inventory_settings_id
    FROM public."Menu" s
    JOIN public."Menu" m ON m.id = s."parentId" AND m."parentId" IS NULL AND m.title = 'Inventory'
    WHERE s.title = 'Settings';
    IF inventory_settings_id IS NULL THEN
        RAISE EXCEPTION 'There is no Inventory -> Settings menu to move the purchase screens into.';
    END IF;

    SELECT s.id INTO purchase_settings_id
    FROM public."Menu" s
    JOIN public."Menu" m ON m.id = s."parentId" AND m."parentId" IS NULL AND m.title = 'Purchase'
    WHERE s.title = 'Settings';

    -- In this order, after what Inventory -> Settings already has; a menu already there keeps its place.
    FOREACH menu_path IN ARRAY ARRAY['/purchase/suppliers', '/purchase/tax-rates', '/purchase/tax-groups', '/purchase/payment-sources']
    LOOP
        IF EXISTS (SELECT 1 FROM public."Menu" WHERE path = menu_path AND "parentId" IS DISTINCT FROM inventory_settings_id) THEN
            SELECT coalesce(max("sortOrder"), 0) + 1 INTO next_order FROM public."Menu" WHERE "parentId" = inventory_settings_id;
            UPDATE public."Menu"
            SET "parentId" = inventory_settings_id, "sortOrder" = next_order
            WHERE path = menu_path;
        END IF;
    END LOOP;

    -- Nothing is left under Purchase -> Settings: hide it.
    IF purchase_settings_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM public."Menu" WHERE "parentId" = purchase_settings_id AND "isActive") THEN
        UPDATE public."Menu" SET "isActive" = false WHERE id = purchase_settings_id;
    END IF;
END;
$$;

COMMIT;
