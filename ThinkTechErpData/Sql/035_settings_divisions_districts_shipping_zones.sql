-- Divisions, districts and shipping zones: three new Settings screens for public.division,
-- public.district and public.shipping_zone. Run after 034_settings_areas.sql; the script is idempotent.
--
--   * DIVISION.V / DIVISION.M, DISTRICT.V / DISTRICT.M and SHIPPING_ZONE.V / SHIPPING_ZONE.M
--     permissions, granted to Super Admin like every other manage pair.
--   * Settings -> Divisions, Districts and Shipping Zones menus, after Areas.
--
-- The three tables already exist, so this script only adds permission and menu rows.

BEGIN;

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO public."Permission" (code, name, description)
VALUES ('DIVISION.V', 'View Divisions', 'View divisions and the number of their districts'),
       ('DIVISION.M', 'Manage Divisions', 'Add, edit and delete divisions'),
       ('DISTRICT.V', 'View Districts', 'View districts, their divisions and shipping zones'),
       ('DISTRICT.M', 'Manage Districts', 'Add, edit and delete districts'),
       ('SHIPPING_ZONE.V', 'View Shipping Zones', 'View shipping zones and their delivery days'),
       ('SHIPPING_ZONE.M', 'Manage Shipping Zones', 'Add, edit and delete shipping zones')
ON CONFLICT (code) DO NOTHING;

INSERT INTO public."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM public."Role" r
JOIN public."Permission" p ON p.code IN ('DIVISION.V', 'DIVISION.M', 'DISTRICT.V', 'DISTRICT.M',
                                         'SHIPPING_ZONE.V', 'SHIPPING_ZONE.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menus: Settings -> Divisions, Districts, Shipping Zones
-- =====================================================================

DO $$
DECLARE
    settings_id INT;
    item        RECORD;
BEGIN
    SELECT id INTO settings_id
    FROM public."Menu"
    WHERE "parentId" IS NULL AND title = 'Settings';
    IF settings_id IS NULL THEN
        RAISE EXCEPTION 'There is no Settings menu to add the screens to.';
    END IF;

    FOR item IN
        SELECT *
        FROM (VALUES (1, 'Divisions', '/settings/divisions', 'map'),
                     (2, 'Districts', '/settings/districts', 'map-pinned'),
                     (3, 'Shipping Zones', '/settings/shipping-zones', 'truck')) AS v(position, title, path, icon)
        ORDER BY position
    LOOP
        IF NOT EXISTS (SELECT 1 FROM public."Menu" WHERE path = item.path) THEN
            INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            SELECT item.title, item.path, item.icon, coalesce(max("sortOrder"), 0) + 1, settings_id, NULL, true
            FROM public."Menu"
            WHERE "parentId" = settings_id;
        END IF;
    END LOOP;
END $$;

COMMIT;
