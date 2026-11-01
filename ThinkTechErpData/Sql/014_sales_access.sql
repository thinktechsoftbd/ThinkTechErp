-- Access to Sales (run after 010 and 011):
--   * SALES.V / SALES.M, granted to the Super Admin role. A counter user needs only SALES.M to sell
--     and collect dues; the product, warehouse and tax pickers accept it too.
--   * The sales menus, inside the Inventory module: Sales and Customer Receipts first under
--     Transactions (the most used screens), Customers under Settings, sales reports under Reports.
-- The script is idempotent: running it again changes nothing.

BEGIN;

INSERT INTO security."Permission" (code, name, description)
VALUES ('SALES.V', 'View Sales', 'View customers, sales invoices, customer receipts and sales reports'),
       ('SALES.M', 'Manage Sales', 'Add and edit customers; enter and post sales invoices and customer receipts')
ON CONFLICT (code) DO NOTHING;

INSERT INTO security."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM security."Role" r
JOIN security."Permission" p ON p.code IN ('SALES.V', 'SALES.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

DO $$
DECLARE
    super_admin int := (SELECT id FROM security."Role" WHERE name = 'Super Admin');
    inventory_id int := (SELECT id FROM security."Menu" WHERE title = 'Inventory' AND "parentId" IS NULL);
    group_id int;
    item record;
BEGIN
    IF inventory_id IS NULL THEN
        RAISE EXCEPTION 'The Inventory menu is missing; run 011 first.';
    END IF;

    FOR item IN
        SELECT * FROM (VALUES
            ('Transactions', 1, 'Sales', '/sales/invoices', 'shopping-bag'),
            ('Transactions', 2, 'Customer Receipts', '/sales/receipts', 'wallet'),
            ('Transactions', 3, 'Sales Returns', '/sales/returns', 'undo-2'),
            ('Transactions', 4, 'Opening Stock', '/inventory/opening-stock', 'package-plus'),
            ('Transactions', 5, 'Stock Adjustments', '/inventory/adjustments', 'package-minus'),
            ('Transactions', 6, 'Stock Transfers', '/inventory/transfers', 'truck'),
            ('Transactions', 7, 'Stock Counts', '/inventory/counts', 'clipboard-check'),
            ('Settings', 7, 'Customers', '/sales/customers', 'users'),
            ('Reports', 7, 'Sales Register', '/sales/reports/register', 'file-spreadsheet'),
            ('Reports', 8, 'Sales by Product', '/sales/reports/by-product', 'chart-bar'),
            ('Reports', 9, 'Customer Dues', '/sales/reports/customer-dues', 'hand-coins'),
            ('Reports', 10, 'Customer Ledger', '/sales/reports/customer-ledger', 'book-open'))
            AS i(grp, position, title, path, icon)
    LOOP
        SELECT id INTO group_id FROM security."Menu" WHERE title = item.grp AND "parentId" = inventory_id;
        IF group_id IS NULL THEN
            RAISE EXCEPTION 'The Inventory > % menu is missing; run 011 first.', item.grp;
        END IF;

        IF EXISTS (SELECT 1 FROM security."Menu" WHERE path = item.path) THEN
            -- Existing items only move, so the sales screens come first.
            UPDATE security."Menu" SET "sortOrder" = item.position
            WHERE path = item.path AND "parentId" = group_id AND "sortOrder" IS DISTINCT FROM item.position;
        ELSE
            INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (item.title, item.path, item.icon, item.position, group_id, super_admin, true);
        END IF;
    END LOOP;
END $$;

COMMIT;
