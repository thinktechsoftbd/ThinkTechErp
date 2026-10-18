-- Refresh tokens for the API's token-based sign-in, stored next to
-- security.password_reset_token. Only SHA-256 hashes of the tokens are kept.
-- Like the other security tables, timestamps are UTC "timestamp(3)" values; the
-- API writes them, so the database session time zone never matters.

BEGIN;

CREATE TABLE IF NOT EXISTS security.refresh_token
(
    id                     serial4 PRIMARY KEY,
    user_id                int4 NOT NULL REFERENCES security."User"(id) ON DELETE CASCADE ON UPDATE CASCADE,
    token_hash             varchar(64) NOT NULL,
    expires_at             timestamp(3) NOT NULL,
    created_at             timestamp(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by_ip          varchar(45) NULL,
    revoked_at             timestamp(3) NULL,
    replaced_by_token_hash varchar(64) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS refresh_token_token_hash_key ON security.refresh_token (token_hash);
CREATE INDEX IF NOT EXISTS refresh_token_user_id_idx ON security.refresh_token (user_id);

COMMIT;
