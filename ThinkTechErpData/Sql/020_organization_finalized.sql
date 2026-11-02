-- An organization can be finalized, which locks its SMS allocations (run after 016).
--
--   * security.organization.is_finalized: set in Security -> Organizations (add/edit). While it is true,
--     the organization's SMS allocations can't be added, changed (moved to or from it) or deleted. SMS usage
--     can still be recorded. Clearing it unlocks the allocations again.
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

ALTER TABLE security.organization
    ADD COLUMN IF NOT EXISTS is_finalized boolean NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN security.organization.is_finalized IS
    'Finalized: the organization''s SMS allocations are locked (no new, changed or deleted allocations).';

COMMIT;
