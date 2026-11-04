-- Add the optional organization binding to integration clients created by 024.
-- Nullable so existing clients continue working until an organization is selected.

BEGIN;

ALTER TABLE public.security_organization_client
    ADD COLUMN IF NOT EXISTS org_object_id UUID NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_security_organization_client_organization'
          AND conrelid = 'public.security_organization_client'::regclass
    ) THEN
        ALTER TABLE public.security_organization_client
            ADD CONSTRAINT fk_security_organization_client_organization
            FOREIGN KEY (org_object_id) REFERENCES public.organization (object_id);
    END IF;
END;
$$;

CREATE INDEX IF NOT EXISTS ix_security_organization_client_org_object_id
    ON public.security_organization_client (org_object_id);

COMMIT;
