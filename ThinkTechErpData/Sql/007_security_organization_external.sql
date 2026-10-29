-- Organizations: is_external flag, ORGANIZATION.V / ORGANIZATION.M permissions for the Super Admin
-- role, and the Security -> Organization menu (shown to Super Admin).
--
-- The API from this change on reads is_external and checks ORGANIZATION.* permissions, so run
-- this before deploying it. The script is idempotent: running it again changes nothing.

BEGIN;

-- Organizations outside this company (e.g. customers), as opposed to its own branches.
ALTER TABLE security.organization
    ADD COLUMN IF NOT EXISTS is_external boolean NOT NULL DEFAULT false;

-- object_id ("Organization Id") stays unique; ux_organization_object_id comes from 006.
CREATE UNIQUE INDEX IF NOT EXISTS ux_organization_object_id
    ON security.organization (object_id);

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO security."Permission" (code, name, description)
VALUES ('ORGANIZATION.V', 'View Organizations', 'View organizations and their Organization Id'),
       ('ORGANIZATION.M', 'Manage Organizations', 'Add and edit organizations, including the Organization Id and the external flag')
ON CONFLICT (code) DO NOTHING;

INSERT INTO security."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM security."Role" r
JOIN security."Permission" p ON p.code IN ('ORGANIZATION.V', 'ORGANIZATION.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menu: Security -> Organization, for the Super Admin role
-- =====================================================================

INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
SELECT 'Organization',
       '/security/organizations',
       'building-2',
       (SELECT coalesce(max(c."sortOrder"), 0) + 1 FROM security."Menu" c WHERE c."parentId" = parent.id),
       parent.id,
       (SELECT r.id FROM security."Role" r WHERE r.name = 'Super Admin'),
       true
FROM security."Menu" parent
WHERE parent.title = 'Security' AND parent."parentId" IS NULL
  AND NOT EXISTS (SELECT 1 FROM security."Menu" m WHERE m.path = '/security/organizations');

COMMIT;
