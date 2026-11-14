-- Areas: a new Settings -> Areas screen for public.area (upazilas, thanas and city areas).
-- Run after 033_inventory_transaction_menus.sql; the script is idempotent.
--
--   * AREA.V / AREA.M permissions, granted to Super Admin like every other manage pair.
--   * Settings -> Areas menu, after Lookup Data.
--
-- public.area already exists (district -> area, with shipping_zone overrides and courier codes),
-- so this script only adds the permission and menu rows; it creates no tables.

BEGIN;

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO public."Permission" (code, name, description)
VALUES ('AREA.V', 'View Areas', 'View delivery areas, their districts and shipping zones'),
       ('AREA.M', 'Manage Areas', 'Add, edit and delete delivery areas')
ON CONFLICT (code) DO NOTHING;

INSERT INTO public."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM public."Role" r
JOIN public."Permission" p ON p.code IN ('AREA.V', 'AREA.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menu: Settings -> Areas
-- =====================================================================

DO $$
DECLARE
    settings_id INT;
    next_order  INT;
BEGIN
    SELECT id INTO settings_id
    FROM public."Menu"
    WHERE "parentId" IS NULL AND title = 'Settings';
    IF settings_id IS NULL THEN
        RAISE EXCEPTION 'There is no Settings menu to add Areas to.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public."Menu" WHERE path = '/settings/areas') THEN
        SELECT coalesce(max("sortOrder"), 0) + 1 INTO next_order
        FROM public."Menu" WHERE "parentId" = settings_id;

        INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        VALUES ('Areas', '/settings/areas', 'map-pin', next_order, settings_id, NULL, true);
    END IF;
END $$;

COMMIT;
