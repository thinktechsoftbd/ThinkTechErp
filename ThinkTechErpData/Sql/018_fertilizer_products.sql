-- Bangladesh fertilizer master list (bangladesh_fertilizer_master_list.txt) for organization 2
-- (M/S Akhi Enterprise), replacing the products that are in the table now.
--
-- Step 1 wipes the shop's inventory/purchase/sales data. It has to: every document line points at a
-- product, so the products cannot be deleted while the documents stay, and deleting only the lines
-- would leave invoice headers with no lines and wrong totals. Every row in these tables belongs to
-- org 2 today (checked), so TRUNCATE is the same as an org-2 delete; CASCADE clears the children
-- (document lines, serials, line taxes, allocations, product units and barcodes) along with them.
--
-- Reference data is left alone: inventory.unit, inventory.brand, inventory.category,
-- inventory.warehouse, purchase.supplier and the security/public lookups all stay.
--
-- Step 2 loads the list. Rows follow what the app expects for a fertilizer: base unit kg with a
-- 50 kg bag, Batch tracking with expiry, and the N-P-K ratio in attributes->>'npk' where the list
-- gives one. SOP, zinc, boric acid, magnesium sulphate and gypsum carry no N-P-K numbers in the
-- source, so they keep their nutrient wording in attributes->>'nutrient' instead of "0-0-0".
--
-- Re-running the script is safe: it wipes and reloads.

BEGIN;

-- 1. Clear the shop's transactions and products .................................

TRUNCATE TABLE
    inventory.product,
    inventory.batch,
    inventory.serial_number,
    inventory.stock_balance,
    inventory.stock_movement,
    inventory.stock_adjustment,
    inventory.stock_count,
    inventory.stock_transfer,
    purchase.purchase_requisition,
    purchase.purchase_order,
    purchase.goods_receipt,
    purchase.purchase_invoice,
    purchase.purchase_return,
    purchase.supplier_payment,
    purchase.invoice_allocation,
    sales.sales_invoice,
    sales.sales_return,
    sales.customer_receipt,
    sales.invoice_allocation
CASCADE;

-- 2. Manufacturers -> inventory.brand ...........................................

INSERT INTO inventory.brand (organization_id, brand_name, is_active, created_at, created_by)
SELECT 2, v.brand_name, TRUE, (now() AT TIME ZONE 'utc'), 2
FROM (VALUES
    ('BCIC'),
    ('KAFCO'),
    ('SABIC Agri-Nutrients'),
    ('TSP Complex Limited'),
    ('OCP Group'),
    ('DAP Fertilizer Company Limited'),
    ('Uralkali'),
    ('Nutrien'),
    ('Belaruskali')
) AS v(brand_name)
WHERE NOT EXISTS (
    SELECT 1 FROM inventory.brand b
    WHERE b.organization_id = 2 AND lower(b.brand_name) = lower(v.brand_name));

-- 3. One sub class per fertilizer line, under Fertilizer ........................

INSERT INTO inventory.category (organization_id, parent_category_id, category_name, sort_order, is_active, created_at, created_by)
SELECT 2, f.category_id, v.category_name, v.sort_order, TRUE, (now() AT TIME ZONE 'utc'), 2
FROM (VALUES
    ('Urea', 5),
    ('Triple Super Phosphate', 10),
    ('Diammonium Phosphate', 20),
    ('Muriate of Potash', 30),
    ('Sulphate of Potash', 40),
    ('Ammonium Sulphate', 50),
    ('Zinc Sulphate', 60),
    ('Boric Acid', 70),
    ('Magnesium Sulphate', 80),
    ('Gypsum', 90)
) AS v(category_name, sort_order)
CROSS JOIN (
    SELECT category_id FROM inventory.category
    WHERE organization_id = 2 AND parent_category_id IS NULL AND lower(category_name) = 'fertilizer'
) f
WHERE NOT EXISTS (
    SELECT 1 FROM inventory.category c
    WHERE c.organization_id = 2 AND c.parent_category_id = f.category_id
      AND lower(c.category_name) = lower(v.category_name));

-- 4. The products. Codes are unique per organization, so the short names that repeat in the list
--    (G-UREA, TSP, DAP and MOP) take the manufacturer as well.

WITH fertilizer (product_code, product_name, category_name, brand_name, country_code, npk, nutrient) AS (
    VALUES
        ('UREA',            'Urea',                                                  'Urea',                     'BCIC',                           'BD', '46-0-0', NULL),
        ('G-UREA-KAFCO',    'Granular Urea (KAFCO)',                                 'Urea',                     'KAFCO',                          'BD', '46-0-0', NULL),
        ('G-UREA-SABIC',    'Granular Urea (SABIC Agri-Nutrients)',                  'Urea',                     'SABIC Agri-Nutrients',           'SA', '46-0-0', NULL),
        ('TSP-TSPCL',       'Triple Super Phosphate (TSP Complex Limited)',          'Triple Super Phosphate',   'TSP Complex Limited',            'BD', '0-46-0', NULL),
        ('TSP-OCP',         'Triple Super Phosphate (OCP Group)',                    'Triple Super Phosphate',   'OCP Group',                      'MA', '0-46-0', NULL),
        ('DAP-DAPCL',       'Diammonium Phosphate (DAP Fertilizer Company Limited)', 'Diammonium Phosphate',     'DAP Fertilizer Company Limited', 'BD', '18-46-0', NULL),
        ('DAP-OCP',         'Diammonium Phosphate (OCP Group)',                      'Diammonium Phosphate',     'OCP Group',                      'MA', '18-46-0', NULL),
        ('DAP-SABIC',       'Diammonium Phosphate (SABIC Agri-Nutrients)',           'Diammonium Phosphate',     'SABIC Agri-Nutrients',           'SA', '18-46-0', NULL),
        ('MOP-URALKALI',    'Muriate of Potash (Uralkali)',                          'Muriate of Potash',        'Uralkali',                       'RU', '0-0-60', NULL),
        ('MOP-NUTRIEN',     'Muriate of Potash (Nutrien)',                           'Muriate of Potash',        'Nutrien',                        'CA', '0-0-60', NULL),
        ('MOP-BELARUSKALI', 'Muriate of Potash (Belaruskali)',                       'Muriate of Potash',        'Belaruskali',                    'BY', '0-0-60', NULL),
        ('SOP',             'Sulphate of Potash',                                    'Sulphate of Potash',       NULL,                             NULL, NULL,     'Potash (K2O) and Sulphur (S)'),
        ('AS',              'Ammonium Sulphate',                                     'Ammonium Sulphate',        NULL,                             NULL, '21-0-0', NULL),
        ('ZN',              'Zinc Sulphate',                                         'Zinc Sulphate',            NULL,                             NULL, NULL,     'Zinc (Zn)'),
        ('BA',              'Boric Acid',                                            'Boric Acid',               NULL,                             NULL, NULL,     'Boron (B)'),
        ('MGSO4',           'Magnesium Sulphate',                                    'Magnesium Sulphate',       NULL,                             NULL, NULL,     'Magnesium (Mg), Sulphur (S)'),
        ('GYP',             'Gypsum',                                                'Gypsum',                   NULL,                             NULL, NULL,     'Calcium (Ca), Sulphur (S)')
)
INSERT INTO inventory.product (
    organization_id, product_code, product_name, product_type, category_id, brand_id, base_unit_id,
    tracking_type, has_expiry, attributes, country_of_origin, is_active, created_at, created_by)
SELECT 2, f.product_code, f.product_name, 'Stock', c.category_id, b.brand_id,
       (SELECT unit_id FROM inventory.unit WHERE organization_id = 2 AND unit_code = 'kg'),
       'Batch', TRUE,
       CASE WHEN f.npk IS NOT NULL THEN jsonb_build_object('npk', f.npk)
            ELSE jsonb_build_object('nutrient', f.nutrient) END,
       f.country_code, TRUE, (now() AT TIME ZONE 'utc'), 2
FROM fertilizer f
LEFT JOIN inventory.category c
       ON c.organization_id = 2 AND lower(c.category_name) = lower(f.category_name)
LEFT JOIN inventory.brand b
       ON b.organization_id = 2 AND lower(b.brand_name) = lower(f.brand_name);

-- 5. Sold by the kg (base unit) and bought by the 50 kg bag .....................

INSERT INTO inventory.product_unit (product_id, unit_id, conversion_factor, is_purchase_default, is_sale_default, created_at, created_by)
SELECT p.product_id, u.unit_id, 1, FALSE, TRUE, (now() AT TIME ZONE 'utc'), 2
FROM inventory.product p
CROSS JOIN (SELECT unit_id FROM inventory.unit WHERE organization_id = 2 AND unit_code = 'kg') u
WHERE p.organization_id = 2;

INSERT INTO inventory.product_unit (product_id, unit_id, conversion_factor, is_purchase_default, is_sale_default, created_at, created_by)
SELECT p.product_id, u.unit_id, 50, TRUE, FALSE, (now() AT TIME ZONE 'utc'), 2
FROM inventory.product p
CROSS JOIN (SELECT unit_id FROM inventory.unit WHERE organization_id = 2 AND unit_code = 'bag') u
WHERE p.organization_id = 2;

COMMIT;

-- Check afterwards:
--   SELECT p.product_code, p.product_name, c.category_name, b.brand_name, p.country_of_origin, p.attributes
--   FROM inventory.product p
--   LEFT JOIN inventory.category c ON c.category_id = p.category_id
--   LEFT JOIN inventory.brand b ON b.brand_id = p.brand_id
--   WHERE p.organization_id = 2
--   ORDER BY c.category_name, p.product_name;
