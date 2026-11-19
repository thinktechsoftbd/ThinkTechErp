-- Farhad Computer (fc.thinktechsoft.com, database fccomdb): 10 warehouses for a computer, laptop, CCTV, camera,
-- projector and IT accessories shop (Inventory -> Warehouses), from the shop's list of common warehouse types.
--
-- warehouse_type follows ck_warehouse_type: 'Warehouse' (godown) for the places that only hold stock (main, wholesale
-- and branch warehouses), 'Store' (shop or outlet) for the rest. Address and phone are left empty to fill in later.
-- Idempotent: a warehouse whose code the organization already has (any letter case) is left as it is.
-- Run on fccomdb only.

BEGIN;

WITH org AS (
    SELECT organization_id FROM public.organization WHERE organization_slug = 'farhadcomputer'
),
warehouses (code, name, warehouse_type) AS (
    VALUES
        ('MAIN',    'Main Warehouse',      'Warehouse'),  -- Central inventory
        ('HO',      'Head Office Store',   'Store'),      -- Office stock
        ('SHOW',    'Showroom',            'Store'),      -- Display and sales
        ('RETAIL',  'Retail Store',        'Store'),      -- Retail inventory
        ('WHOLE',   'Wholesale Warehouse', 'Warehouse'),  -- Wholesale stock
        ('BRANCH',  'Branch Warehouse',    'Warehouse'),  -- Branch inventory
        ('SERVICE', 'Service Center',      'Store'),      -- Repair and servicing
        ('SPARE',   'Spare Parts Store',   'Store'),      -- Spare parts
        ('ACC',     'Accessories Store',   'Store'),      -- Accessories
        ('ELEC',    'Electronics Store',   'Store')       -- Electronic products
)
INSERT INTO public.warehouse (organization_id, warehouse_code, warehouse_name, warehouse_type, is_active)
SELECT o.organization_id, w.code, w.name, w.warehouse_type, TRUE
FROM org o
CROSS JOIN warehouses w
ON CONFLICT (organization_id, lower(warehouse_code)) DO NOTHING;

-- What Farhad Computer has now.
SELECT w.warehouse_code, w.warehouse_name, w.warehouse_type, w.is_active
FROM public.warehouse w
JOIN public.organization o ON o.organization_id = w.organization_id AND o.organization_slug = 'farhadcomputer'
ORDER BY w.warehouse_id;

COMMIT;
