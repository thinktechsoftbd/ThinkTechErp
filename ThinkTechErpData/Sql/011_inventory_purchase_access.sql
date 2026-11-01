-- Access to the Inventory and Purchase modules (run after 009):
--   * security.user_organization: the organizations each user works in. Inventory and Purchase
--     data belongs to one organization; the API takes it from the X-Organization-Id header (or the
--     user's default) and checks it against this table. Holders of ORGANIZATION.M (Super Admin)
--     may work in any organization without a row here.
--   * INVENTORY.V / INVENTORY.M and PURCHASE.V / PURCHASE.M, granted to the Super Admin role.
--   * The Inventory and Purchase menus (Settings, Transactions, Reports), shown to Super Admin.
-- Timestamps are UTC, as in 006. The script is idempotent: running it again changes nothing.

BEGIN;

CREATE TABLE IF NOT EXISTS security.user_organization
(
    user_id         INT NOT NULL,
    organization_id INT NOT NULL,
    -- The organization used when a request doesn't name one.
    is_default      BOOLEAN NOT NULL DEFAULT FALSE,

    created_at      TIMESTAMP(3) NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by      INT NULL,

    CONSTRAINT pk_user_organization PRIMARY KEY (user_id, organization_id),
    CONSTRAINT fk_user_organization_user
        FOREIGN KEY (user_id) REFERENCES security."User" (id) ON DELETE CASCADE,
    CONSTRAINT fk_user_organization_organization
        FOREIGN KEY (organization_id) REFERENCES security.organization (organization_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_user_organization_default
    ON security.user_organization (user_id) WHERE is_default;
CREATE INDEX IF NOT EXISTS idx_user_organization_organization
    ON security.user_organization (organization_id);

-- =====================================================================
-- Permissions
-- =====================================================================

INSERT INTO security."Permission" (code, name, description)
VALUES ('INVENTORY.V', 'View Inventory', 'View products, warehouses, stock and inventory reports'),
       ('INVENTORY.M', 'Manage Inventory', 'Add and edit inventory settings; post stock adjustments, transfers and counts'),
       ('PURCHASE.V', 'View Purchases', 'View suppliers, purchase documents and purchase reports'),
       ('PURCHASE.M', 'Manage Purchases', 'Add and edit suppliers and taxes; enter and post purchase documents and payments')
ON CONFLICT (code) DO NOTHING;

INSERT INTO security."RolePermission" ("roleId", "permissionId")
SELECT r.id, p.id
FROM security."Role" r
JOIN security."Permission" p ON p.code IN ('INVENTORY.V', 'INVENTORY.M', 'PURCHASE.V', 'PURCHASE.M')
WHERE r.name = 'Super Admin'
ON CONFLICT DO NOTHING;

-- =====================================================================
-- Menus: Inventory and Purchase, each with Settings, Transactions and Reports
-- =====================================================================

DO $$
DECLARE
    super_admin int := (SELECT id FROM security."Role" WHERE name = 'Super Admin');
    next_top int := (SELECT coalesce(max("sortOrder"), 0) FROM security."Menu" WHERE "parentId" IS NULL);
    module_menu record;
    group_menu record;
    module_id int;
    group_id int;
    item_order int;
    item text[];
BEGIN
    FOR module_menu IN
        SELECT * FROM (VALUES
            (1, 'Inventory', 'package'),
            (2, 'Purchase', 'shopping-cart')) AS m(position, title, icon)
        ORDER BY position
    LOOP
        SELECT id INTO module_id FROM security."Menu" WHERE title = module_menu.title AND "parentId" IS NULL;
        IF module_id IS NULL THEN
            next_top := next_top + 1;
            INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
            VALUES (module_menu.title, '#', module_menu.icon, next_top, NULL, super_admin, true)
            RETURNING id INTO module_id;
        END IF;

        FOR group_menu IN
            SELECT * FROM (VALUES
                (1, 'Settings', 'settings'),
                (2, 'Transactions', 'arrow-left-right'),
                (3, 'Reports', 'chart-column')) AS g(position, title, icon)
            ORDER BY position
        LOOP
            SELECT id INTO group_id FROM security."Menu" WHERE title = group_menu.title AND "parentId" = module_id;
            IF group_id IS NULL THEN
                INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
                VALUES (group_menu.title, '#', group_menu.icon, group_menu.position, module_id, super_admin, true)
                RETURNING id INTO group_id;
            END IF;

            item_order := 0;
            FOR item IN
                SELECT ARRAY[i.title, i.path, i.icon] FROM (VALUES
                    ('Inventory', 'Settings', 1, 'Products', '/inventory/products', 'package'),
                    ('Inventory', 'Settings', 2, 'Categories', '/inventory/categories', 'folder-tree'),
                    ('Inventory', 'Settings', 3, 'Brands', '/inventory/brands', 'tag'),
                    ('Inventory', 'Settings', 4, 'Units', '/inventory/units', 'ruler'),
                    ('Inventory', 'Settings', 5, 'Warehouses', '/inventory/warehouses', 'warehouse'),
                    ('Inventory', 'Settings', 6, 'Inventory Settings', '/inventory/settings', 'sliders-horizontal'),
                    ('Inventory', 'Transactions', 1, 'Opening Stock', '/inventory/opening-stock', 'package-plus'),
                    ('Inventory', 'Transactions', 2, 'Stock Adjustments', '/inventory/adjustments', 'package-minus'),
                    ('Inventory', 'Transactions', 3, 'Stock Transfers', '/inventory/transfers', 'truck'),
                    ('Inventory', 'Transactions', 4, 'Stock Counts', '/inventory/counts', 'clipboard-check'),
                    ('Inventory', 'Reports', 1, 'Stock on Hand', '/inventory/reports/stock-on-hand', 'boxes'),
                    ('Inventory', 'Reports', 2, 'Stock Card', '/inventory/reports/stock-card', 'scroll-text'),
                    ('Inventory', 'Reports', 3, 'Stock Valuation', '/inventory/reports/valuation', 'coins'),
                    ('Inventory', 'Reports', 4, 'Stock Movements', '/inventory/reports/movements', 'arrow-left-right'),
                    ('Inventory', 'Reports', 5, 'Reorder Alert', '/inventory/reports/reorder', 'bell-ring'),
                    ('Inventory', 'Reports', 6, 'Expiry Alert', '/inventory/reports/expiry', 'calendar-clock'),
                    ('Purchase', 'Settings', 1, 'Suppliers', '/purchase/suppliers', 'contact'),
                    ('Purchase', 'Settings', 2, 'Tax Rates', '/purchase/tax-rates', 'percent'),
                    ('Purchase', 'Settings', 3, 'Tax Groups', '/purchase/tax-groups', 'layers'),
                    ('Purchase', 'Transactions', 1, 'Purchase Invoices', '/purchase/invoices', 'receipt'),
                    ('Purchase', 'Transactions', 2, 'Purchase Orders', '/purchase/orders', 'clipboard-list'),
                    ('Purchase', 'Transactions', 3, 'Goods Receipts', '/purchase/goods-receipts', 'package-check'),
                    ('Purchase', 'Transactions', 4, 'Purchase Returns', '/purchase/returns', 'undo-2'),
                    ('Purchase', 'Transactions', 5, 'Supplier Payments', '/purchase/payments', 'banknote'),
                    ('Purchase', 'Reports', 1, 'Supplier Ledger', '/purchase/reports/supplier-ledger', 'book-open'),
                    ('Purchase', 'Reports', 2, 'Supplier Dues', '/purchase/reports/supplier-dues', 'hand-coins'),
                    ('Purchase', 'Reports', 3, 'Purchase Register', '/purchase/reports/register', 'file-spreadsheet'),
                    ('Purchase', 'Reports', 4, 'Purchase by Product', '/purchase/reports/by-product', 'chart-bar'))
                    AS i(module, grp, position, title, path, icon)
                WHERE i.module = module_menu.title AND i.grp = group_menu.title
                ORDER BY i.position
            LOOP
                item_order := item_order + 1;
                IF NOT EXISTS (SELECT 1 FROM security."Menu" WHERE path = item[2]) THEN
                    INSERT INTO security."Menu" (title, path, icon, "sortOrder", "parentId", "roleId", "isActive")
                    VALUES (item[1], item[2], item[3], item_order, group_id, super_admin, true);
                END IF;
            END LOOP;
            group_id := NULL;
        END LOOP;
        module_id := NULL;
    END LOOP;
END $$;

COMMIT;
