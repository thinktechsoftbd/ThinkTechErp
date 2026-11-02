-- Posting and unposting vouchers gets its own permission (run after 018).
--
--   * ACCOUNTING.P "Post & Unpost Vouchers": post a draft voucher to the ledgers, and unpost a posted one
--     back to a draft (POST /api/accounting/vouchers/{id}/post and /unpost). ACCOUNTING.M keeps entering,
--     editing, deleting and cancelling vouchers, and recording bill payments and collections.
--   * Super Admin gets it. Users see it after signing in again (permissions are in the token).
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

INSERT INTO security."Permission" (code, name, description)
VALUES ('ACCOUNTING.P', 'Post & Unpost Vouchers', 'Post draft vouchers to the ledgers and unpost posted vouchers back to drafts')
ON CONFLICT (code) DO NOTHING;

UPDATE security."Permission"
SET description = 'Change the chart of accounts; enter, edit, delete and cancel vouchers; record bill payments and collections'
WHERE code = 'ACCOUNTING.M';

INSERT INTO security."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM security."Role" r
JOIN security."Permission" p ON p.code = 'ACCOUNTING.P'
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

COMMIT;
