-- Deployment information per organization: what is deployed for it (by service type) and the details
-- (servers, URLs, versions, notes). Run after 038_thinkcart_storefront.sql; the script is idempotent.
--
--   * public.deployment_info_h: one row per deployed service of an organization (organization_object_id),
--     typed by the "Service Type" lookup list, with an active flag, remarks and who created / last changed it.
--   * public.deployment_info_d: the deployment's detail lines (deployment_description), deleted with it.
--   * public.organization.object_id is unique (ux_organization_object_id), so deployments can point at it.
--   * "Service Type" lookup list (Settings -> Lookup Data) with a starting set of values.
--   * DEPLOYMENT.V / DEPLOYMENT.M permissions, granted to Super Admin.
--   * Security -> Deployment menu (/deployment); each organization's deployments are at
--     /deployment/{organization object id}/{organization slug}.

BEGIN;

-- =====================================================================
-- Organizations are referenced by object_id
-- =====================================================================

-- Most databases already have this index; the foreign key below needs it.
CREATE UNIQUE INDEX IF NOT EXISTS ux_organization_object_id ON public.organization (object_id);

-- =====================================================================
-- Service Type lookup list
-- =====================================================================

INSERT INTO public.lookup_table (lookup_table_name)
VALUES ('Service Type')
ON CONFLICT DO NOTHING;

INSERT INTO public.lookup_data (lookup_table_id, lookup_data_name)
SELECT t.lookup_table_id, v.name
FROM public.lookup_table t
CROSS JOIN (VALUES (1, 'Web Application'), (2, 'API'), (3, 'Database'), (4, 'Website'), (5, 'Mobile App'),
                   (6, 'File Storage'), (7, 'Domain & DNS'), (8, 'Email')) AS v(position, name)
WHERE lower(t.lookup_table_name) = 'service type'
ORDER BY v.position
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Tables
-- =====================================================================

CREATE TABLE IF NOT EXISTS public.deployment_info_h
(
    id                     INT GENERATED ALWAYS AS IDENTITY,
    object_id              UUID NOT NULL DEFAULT gen_random_uuid(),
    organization_object_id UUID NOT NULL,
    lk_service_type_id     BIGINT NOT NULL,          -- lookup "Service Type"
    is_active              BOOLEAN NOT NULL DEFAULT TRUE,
    remarks                VARCHAR(1000) NULL,
    created_date           TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by_id          INT NULL,
    updated_date           TIMESTAMP NULL,
    updated_by_id          INT NULL,

    CONSTRAINT pk_deployment_info_h PRIMARY KEY (id),
    CONSTRAINT uq_deployment_info_h_object_id UNIQUE (object_id),
    CONSTRAINT fk_deployment_info_h_organization
        FOREIGN KEY (organization_object_id) REFERENCES public.organization (object_id),
    CONSTRAINT fk_deployment_info_h_service_type
        FOREIGN KEY (lk_service_type_id) REFERENCES public.lookup_data (lookup_data_id),
    CONSTRAINT fk_deployment_info_h_created_by FOREIGN KEY (created_by_id) REFERENCES public."User" (id),
    CONSTRAINT fk_deployment_info_h_updated_by FOREIGN KEY (updated_by_id) REFERENCES public."User" (id)
);

CREATE INDEX IF NOT EXISTS idx_deployment_info_h_organization
    ON public.deployment_info_h (organization_object_id);

CREATE TABLE IF NOT EXISTS public.deployment_info_d
(
    id                     INT GENERATED ALWAYS AS IDENTITY,
    deployment_info_h_id   INT NOT NULL,
    deployment_description TEXT NOT NULL,

    CONSTRAINT pk_deployment_info_d PRIMARY KEY (id),
    CONSTRAINT fk_deployment_info_d_header
        FOREIGN KEY (deployment_info_h_id) REFERENCES public.deployment_info_h (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_deployment_info_d_header
    ON public.deployment_info_d (deployment_info_h_id);

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO public."Permission" (code, name, description)
VALUES ('DEPLOYMENT.V', 'View Deployments', 'View what is deployed for each organization and its details'),
       ('DEPLOYMENT.M', 'Manage Deployments', 'Add, edit and delete organizations'' deployment information')
ON CONFLICT (code) DO NOTHING;

INSERT INTO public."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM public."Role" r
JOIN public."Permission" p ON p.code IN ('DEPLOYMENT.V', 'DEPLOYMENT.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menu: Security -> Deployment
-- =====================================================================

DO $$
DECLARE
    security_id INT;
BEGIN
    SELECT id INTO security_id
    FROM public."Menu"
    WHERE "parentId" IS NULL AND title = 'Security';
    IF security_id IS NULL THEN
        RAISE EXCEPTION 'There is no Security menu to add Deployment to.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public."Menu" WHERE path = '/deployment') THEN
        INSERT INTO public."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
        SELECT 'Deployment', '/deployment', 'server', coalesce(max("sortOrder"), 0) + 1, security_id, NULL, true
        FROM public."Menu"
        WHERE "parentId" = security_id;
    END IF;
END $$;

COMMIT;
