-- Least-privilege database role for the SMS API.
-- After applying this script, create a LOGIN role with a strong password and
-- grant it membership in sms_api_runtime. Keep that login secret outside source control.

BEGIN;

DO $role$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'sms_api_runtime') THEN
        CREATE ROLE sms_api_runtime NOLOGIN;
    END IF;
END;
$role$;

GRANT USAGE ON SCHEMA sms TO sms_api_runtime;
GRANT SELECT ON ALL TABLES IN SCHEMA sms TO sms_api_runtime;
ALTER DEFAULT PRIVILEGES IN SCHEMA sms
    GRANT SELECT ON TABLES TO sms_api_runtime;

-- The API can manage client profile fields through functions and cannot edit
-- wallet balances or audit rows directly.
REVOKE ALL ON FUNCTION sms.record_adn_purchase(bigint, text, numeric, character, timestamptz, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION sms.allocate_sms(text, text, bigint, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION sms.consume_sms(text, bigint, text, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION sms.register_client(text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION sms.update_client(text, text, boolean) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION sms.record_adn_purchase(bigint, text, numeric, character, timestamptz, text) TO sms_api_runtime;
GRANT EXECUTE ON FUNCTION sms.allocate_sms(text, text, bigint, text) TO sms_api_runtime;
GRANT EXECUTE ON FUNCTION sms.consume_sms(text, bigint, text, text, text) TO sms_api_runtime;
GRANT EXECUTE ON FUNCTION sms.register_client(text, text) TO sms_api_runtime;
GRANT EXECUTE ON FUNCTION sms.update_client(text, text, boolean) TO sms_api_runtime;

COMMIT;
