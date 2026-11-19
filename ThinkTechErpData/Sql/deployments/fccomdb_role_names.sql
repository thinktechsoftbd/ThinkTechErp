-- Farhad Computer (fc.thinktechsoft.com, database fccomdb): role names and descriptions for a computer sales
-- and service shop, replacing the clinic names the database was copied with.
--
--   Doctor       -> System Admin
--   Receptionist -> Technician
--   sms_admin    -> General
--
-- Only the names and descriptions change: each role keeps its permissions, menus and users (they link by id),
-- and no code refers to role names. Signed-in users see the new names after their next sign-in or token
-- refresh. Run on fccomdb only. Idempotent: a role already renamed just gets its description again.

BEGIN;

UPDATE public."Role" r
SET name = v.new_name,
    description = v.description
FROM (VALUES
    ('Doctor',       'System Admin', 'Runs the shop''s system: users, roles, settings and every module.'),
    ('Receptionist', 'Technician',   'Service desk: warranty checks, service jobs, repairs and replacements.'),
    ('sms_admin',    'General',      'General staff: everyday sales, purchases and stock work.')
) AS v(old_name, new_name, description)
WHERE r.name IN (v.old_name, v.new_name);

SELECT r.id, r.name, r.description,
       (SELECT count(*) FROM public."RolePermission" rp WHERE rp."roleId" = r.id) AS permissions,
       (SELECT count(*) FROM public."UserRole" ur WHERE ur."roleId" = r.id) AS users
FROM public."Role" r
ORDER BY r.id;

COMMIT;
