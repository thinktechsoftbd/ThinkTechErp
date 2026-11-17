-- Access to Warranty & Service (run after 036_warranty_service.sql):
--   * WARRANTY.V / WARRANTY.M, granted to the Super Admin role. WARRANTY.M opens and works service jobs, issues
--     manual warranty cards, sends claims to suppliers and bills a job (a draft sales invoice made from its items).
--   * A new top-level Warranty menu, after the other modules:
--       Warranty -> Settings     -> Warranty Policies, Product Warranty
--       Warranty -> Transactions -> Warranty (the service desk: jobs, warranty check and cards, supplier claims)
-- Menus are found by path and title, not id. The script is idempotent: running it again changes nothing.

BEGIN;

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO public."Permission" (code, name, description)
VALUES ('WARRANTY.V', 'View Warranty', 'View warranty policies, warranty cards, service jobs and supplier claims'),
       ('WARRANTY.M', 'Manage Warranty',
        'Set warranty policies; issue and void warranty cards; open, work, bill and deliver service jobs; send supplier claims')
ON CONFLICT (code) DO NOTHING;

INSERT INTO public."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM public."Role" r
JOIN public."Permission" p ON p.code IN ('WARRANTY.V', 'WARRANTY.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menus
-- =====================================================================

DO $$
DECLARE
    super_admin INT := (SELECT id FROM public."Role" WHERE name = 'Super Admin');
    warranty_id INT;
    group_id    INT;
    grp         RECORD;
    item        RECORD;
BEGIN
    SELECT id INTO warranty_id FROM public."Menu" WHERE "parentId" IS NULL AND title = 'Warranty';
    IF warranty_id IS NULL THEN
        INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        VALUES ('Warranty', '#', 'shield-check',
                (SELECT coalesce(max("sortOrder"), 0) + 1 FROM public."Menu" WHERE "parentId" IS NULL),
                NULL, super_admin, true)
        RETURNING id INTO warranty_id;
    END IF;

    FOR grp IN
        SELECT * FROM (VALUES
            (1, 'Settings', 'settings'),
            (2, 'Transactions', 'arrow-left-right')) AS g(position, title, icon)
        ORDER BY position
    LOOP
        SELECT id INTO group_id FROM public."Menu" WHERE "parentId" = warranty_id AND title = grp.title;
        IF group_id IS NULL THEN
            INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (grp.title, '#', grp.icon, grp.position, warranty_id, super_admin, true)
            RETURNING id INTO group_id;
        END IF;

        FOR item IN
            SELECT * FROM (VALUES
                ('Settings', 1, 'Warranty Policies', '/warranty/policies', 'scroll-text'),
                ('Settings', 2, 'Product Warranty', '/warranty/products', 'package-check'),
                ('Transactions', 1, 'Warranty', '/warranty/service', 'wrench'))
                AS i(grp, position, title, path, icon)
            WHERE i.grp = grp.title
        LOOP
            IF NOT EXISTS (SELECT 1 FROM public."Menu" WHERE path = item.path) THEN
                INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
                VALUES (item.title, item.path, item.icon, item.position, group_id, super_admin, true);
            END IF;
        END LOOP;
    END LOOP;
END $$;

COMMIT;
