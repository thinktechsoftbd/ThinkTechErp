-- Inventory gets both transaction groups:
--   Inventory -> Settings, Sales Transactions, Purchase Transactions, Reports
--   * Inventory -> Transactions is renamed Sales Transactions.
--   * Purchase -> Transactions (with its screens) moves under Inventory as Purchase Transactions, right after
--     Sales Transactions.
--   * Purchase is then left with nothing to show (its Settings and Reports were emptied by 031 and 032), so it is
--     hidden (isActive = false) rather than deleted.
-- Menus are found by title, not id. Run after 032_move_purchase_report_menus.sql; the script is idempotent.

BEGIN;

DO $$
DECLARE
    inventory_id      INT;
    purchase_id       INT;
    purchase_tx_id    INT;
    sales_tx_order    INT;
BEGIN
    SELECT id INTO inventory_id FROM public."Menu" WHERE "parentId" IS NULL AND title = 'Inventory';
    IF inventory_id IS NULL THEN
        RAISE EXCEPTION 'There is no Inventory menu.';
    END IF;
    SELECT id INTO purchase_id FROM public."Menu" WHERE "parentId" IS NULL AND title = 'Purchase';

    UPDATE public."Menu" SET title = 'Sales Transactions'
    WHERE "parentId" = inventory_id AND title = 'Transactions';

    SELECT id INTO purchase_tx_id FROM public."Menu" WHERE "parentId" = purchase_id AND title = 'Transactions';
    IF purchase_tx_id IS NOT NULL THEN
        SELECT "sortOrder" INTO sales_tx_order FROM public."Menu" WHERE "parentId" = inventory_id AND title = 'Sales Transactions';
        sales_tx_order := coalesce(sales_tx_order, 0);
        -- Room right after Sales Transactions (Reports moves down one).
        UPDATE public."Menu" SET "sortOrder" = "sortOrder" + 1
        WHERE "parentId" = inventory_id AND "sortOrder" > sales_tx_order;
        UPDATE public."Menu"
        SET "parentId" = inventory_id, title = 'Purchase Transactions', "sortOrder" = sales_tx_order + 1
        WHERE id = purchase_tx_id;
    END IF;

    IF purchase_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM public."Menu" WHERE "parentId" = purchase_id AND "isActive") THEN
        UPDATE public."Menu" SET "isActive" = false WHERE id = purchase_id;
    END IF;
END;
$$;

COMMIT;
