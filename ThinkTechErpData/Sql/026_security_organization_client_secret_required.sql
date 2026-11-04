-- Store the client secret and bind every integration client to an organization.
-- Existing seeded clients are backfilled using the values and organization codes supplied at setup.

BEGIN;

ALTER TABLE public.security_organization_client
    ADD COLUMN IF NOT EXISTS client_secret TEXT NULL;

UPDATE public.security_organization_client c
SET client_secret = COALESCE(NULLIF(c.client_secret, ''), CASE c.client_name
        WHEN 'medidesk_mahanta_dental_care' THEN 'medidesk_mahanta_dental_care_secret'
        WHEN 'think_tech_official_site' THEN 'think_tech_official_site_secret'
    END),
    org_object_id = COALESCE(c.org_object_id, o.object_id)
FROM public.organization o
WHERE (c.client_name = 'medidesk_mahanta_dental_care' AND o.organization_code = 'MDC-003')
   OR (c.client_name = 'think_tech_official_site' AND o.organization_code = 'MDC-0007');

UPDATE public.security_organization_client
SET secret_sha256_hash = CASE client_name
        WHEN 'medidesk_mahanta_dental_care' THEN '60e3476283b046309e2fff8291ec3bd96746106a926f40549d3ac8099a1c9361'
        WHEN 'think_tech_official_site' THEN 'c4ae39fb470518dde2150668b065df47b7a05ef282ea9577f4c775aee5b92cd1'
    END
WHERE client_name IN ('medidesk_mahanta_dental_care', 'think_tech_official_site');

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM public.security_organization_client
        WHERE client_secret IS NULL OR org_object_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Every API client needs a client_secret and organization. Assign organizations and secrets before applying migration 026.';
    END IF;
END;
$$;

ALTER TABLE public.security_organization_client
    ALTER COLUMN client_secret SET NOT NULL,
    ALTER COLUMN org_object_id SET NOT NULL;

COMMIT;
