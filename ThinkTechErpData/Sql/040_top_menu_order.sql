-- Orders the sidebar's top-level menus:
--   1 Dashboard, 2 Inventory, 3 Warranty, 4 Accounting, 5 WebSite, 6 SMS, 7 Greetings, 8 Settings, 9 Security
-- Purchase (hidden since 033) comes after them. Any other top-level menu keeps its relative order after those.
-- Menus are found by title, not id; only "sortOrder" changes. The script is idempotent.

BEGIN;

WITH wanted(title, position) AS (
    VALUES ('Dashboard', 1), ('Inventory', 2), ('Warranty', 3), ('Accounting', 4), ('WebSite', 5),
           ('SMS', 6), ('Greetings', 7), ('Settings', 8), ('Security', 9), ('Purchase', 10)
),
ordered AS (
    SELECT m.id,
           row_number() OVER (ORDER BY w.position NULLS LAST, m."sortOrder", m.id) AS new_order
    FROM public."Menu" m
    LEFT JOIN wanted w ON w.title = m.title
    WHERE m."parentId" IS NULL
)
UPDATE public."Menu" m
SET "sortOrder" = o.new_order
FROM ordered o
WHERE m.id = o.id AND m."sortOrder" IS DISTINCT FROM o.new_order;

SELECT id, title, "sortOrder", "isActive" FROM public."Menu" WHERE "parentId" IS NULL ORDER BY "sortOrder", id;

COMMIT;
