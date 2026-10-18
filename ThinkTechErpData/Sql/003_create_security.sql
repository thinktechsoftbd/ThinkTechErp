-- Users, roles and token revocation for the ERP's Security area.
-- Passwords are stored only as ASP.NET Core Identity PasswordHasher (PBKDF2) hashes.
-- SMS client users carry the SMS client identifier as text, matching sms.clients.client_id
-- without a cross-schema foreign key.
-- After applying this script, seed the first administrator with:
--   dotnet run --project ThinkTechErpAPI/ThinkTechErpAPI -- seed-admin <userName>

BEGIN;

CREATE SCHEMA IF NOT EXISTS security;

CREATE TABLE IF NOT EXISTS security.roles
(
    role_id     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name        text NOT NULL,
    description text,
    is_system   boolean NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT roles_name_format CHECK (name ~ '^[a-z][a-z0-9_]{1,63}$'),
    CONSTRAINT roles_description_length CHECK (description IS NULL OR length(description) <= 500)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_security_roles_name ON security.roles (name);

CREATE TABLE IF NOT EXISTS security.role_permissions
(
    role_id    uuid NOT NULL REFERENCES security.roles(role_id) ON DELETE CASCADE,
    permission text NOT NULL,
    PRIMARY KEY (role_id, permission),
    CONSTRAINT role_permissions_format CHECK (permission ~ '^[a-z0-9_]+(\.[a-z0-9_]+)*$' AND length(permission) <= 128)
);

CREATE TABLE IF NOT EXISTS security.users
(
    user_id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_name           text NOT NULL,
    email               text,
    display_name        text,
    password_hash       text NOT NULL,
    is_active           boolean NOT NULL DEFAULT true,
    client_id           text,
    failed_login_count  integer NOT NULL DEFAULT 0 CHECK (failed_login_count >= 0),
    lockout_end         timestamptz,
    password_changed_at timestamptz NOT NULL DEFAULT now(),
    last_login_at       timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT users_user_name_format CHECK (user_name ~ '^[A-Za-z0-9._@-]{3,64}$'),
    CONSTRAINT users_email_length CHECK (email IS NULL OR (length(email) BETWEEN 3 AND 256)),
    CONSTRAINT users_display_name_length CHECK (display_name IS NULL OR length(display_name) <= 200),
    CONSTRAINT users_client_id_not_blank CHECK (client_id IS NULL OR length(btrim(client_id)) > 0),
    CONSTRAINT users_password_hash_not_blank CHECK (length(password_hash) > 0)
);

-- User names and emails are unique regardless of case; sign-in matches the same way.
CREATE UNIQUE INDEX IF NOT EXISTS ux_security_users_user_name ON security.users (lower(user_name));
CREATE UNIQUE INDEX IF NOT EXISTS ux_security_users_email ON security.users (lower(email)) WHERE email IS NOT NULL;

CREATE TABLE IF NOT EXISTS security.user_roles
(
    user_id    uuid NOT NULL REFERENCES security.users(user_id) ON DELETE CASCADE,
    role_id    uuid NOT NULL REFERENCES security.roles(role_id),
    granted_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, role_id)
);

CREATE INDEX IF NOT EXISTS ix_security_user_roles_role ON security.user_roles (role_id);

-- Denylist of access tokens revoked before they expire (logout). Rows are only
-- needed until expires_at; the API deletes expired rows when it revokes tokens.
CREATE TABLE IF NOT EXISTS security.revoked_tokens
(
    jti        text PRIMARY KEY,
    subject    text NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_security_revoked_tokens_expires ON security.revoked_tokens (expires_at);

-- Roles the API's authorization policies depend on. They cannot be renamed or deleted.
INSERT INTO security.roles (name, description, is_system)
VALUES ('admin', 'Super administrator: full access to every module.', true),
       ('security_admin', 'Manages users, roles and permissions.', true),
       ('sms_admin', 'Manages SMS purchases, clients and allocations.', true),
       ('sms_client', 'SMS API client; needs a client ID and can only use its own SMS account.', true)
ON CONFLICT (name) DO UPDATE SET is_system = true;

INSERT INTO security.role_permissions (role_id, permission)
SELECT r.role_id, p.permission
FROM security.roles r
JOIN (VALUES
        ('security_admin', 'security.users.view'),
        ('security_admin', 'security.users.manage'),
        ('security_admin', 'security.roles.view'),
        ('security_admin', 'security.roles.manage'),
        ('sms_admin', 'sms.balance.view'),
        ('sms_admin', 'sms.clients.view'),
        ('sms_admin', 'sms.clients.manage'),
        ('sms_admin', 'sms.purchases.manage'),
        ('sms_admin', 'sms.allocations.manage'),
        ('sms_admin', 'sms.transactions.view')
     ) AS p(role_name, permission) ON p.role_name = r.name
ON CONFLICT DO NOTHING;

COMMIT;
