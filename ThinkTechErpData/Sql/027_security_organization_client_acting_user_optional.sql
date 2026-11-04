-- Acting users are optional metadata on an integration client and are not constrained to User.id.

BEGIN;

ALTER TABLE public.security_organization_client
    DROP CONSTRAINT IF EXISTS fk_security_organization_client_acting_user,
    ALTER COLUMN acting_user_id DROP NOT NULL;

COMMIT;
