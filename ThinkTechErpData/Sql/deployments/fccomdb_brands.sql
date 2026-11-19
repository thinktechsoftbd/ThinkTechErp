-- Farhad Computer (fc.thinktechsoft.com, database fccomdb): 81 brands for a computer, laptop, CCTV, camera,
-- projector and IT accessories shop (Inventory -> Settings -> Brands), in the order of the list they came from.
-- Each brand's description names its group (e.g. "Computer & Laptop Accessories"); a brand listed in two groups
-- (UGREEN) is added once. Idempotent: a brand the organization already has (any letter case) is left as it is.
-- Run on fccomdb only.

BEGIN;

WITH org AS (
    SELECT organization_id FROM public.organization WHERE organization_slug = 'farhadcomputer'
),
brands (position, name, grp) AS (
    VALUES
        ( 1, 'Logitech', 'Computer & Laptop Accessories'),
        ( 2, 'A4Tech', 'Computer & Laptop Accessories'),
        ( 3, 'Rapoo', 'Computer & Laptop Accessories'),
        ( 4, 'Havit', 'Computer & Laptop Accessories'),
        ( 5, 'Fantech', 'Computer & Laptop Accessories'),
        ( 6, 'Redragon', 'Computer & Laptop Accessories'),
        ( 7, 'Razer', 'Computer & Laptop Accessories'),
        ( 8, 'Corsair', 'Computer & Laptop Accessories'),
        ( 9, 'Gamdias', 'Computer & Laptop Accessories'),
        (10, 'Dareu', 'Computer & Laptop Accessories'),
        (11, 'UGREEN', 'Computer & Laptop Accessories'),
        (12, 'Baseus', 'Computer & Laptop Accessories'),
        (13, 'Orico', 'Computer & Laptop Accessories'),
        (14, 'Vention', 'Computer & Laptop Accessories'),
        (15, 'TP-Link', 'Computer & Laptop Accessories'),
        (16, 'Tenda', 'Computer & Laptop Accessories'),
        (17, 'ASUS', 'Laptop & Computer Components'),
        (18, 'Gigabyte', 'Laptop & Computer Components'),
        (19, 'MSI', 'Laptop & Computer Components'),
        (20, 'ASRock', 'Laptop & Computer Components'),
        (21, 'Intel', 'Laptop & Computer Components'),
        (22, 'AMD', 'Laptop & Computer Components'),
        (23, 'Kingston', 'Laptop & Computer Components'),
        (24, 'Samsung', 'Laptop & Computer Components'),
        (25, 'Transcend', 'Laptop & Computer Components'),
        (26, 'Crucial', 'Laptop & Computer Components'),
        (27, 'Western Digital', 'Laptop & Computer Components'),
        (28, 'Seagate', 'Laptop & Computer Components'),
        (29, 'DeepCool', 'Laptop & Computer Components'),
        (30, 'Thermaltake', 'Laptop & Computer Components'),
        (31, 'Antec', 'Laptop & Computer Components'),
        (32, 'Hikvision', 'CCTV & Security Camera Accessories'),
        (33, 'Dahua', 'CCTV & Security Camera Accessories'),
        (34, 'Uniview', 'CCTV & Security Camera Accessories'),
        (35, 'CP Plus', 'CCTV & Security Camera Accessories'),
        (36, 'EZVIZ', 'CCTV & Security Camera Accessories'),
        (37, 'IMOU', 'CCTV & Security Camera Accessories'),
        (38, 'Tiandy', 'CCTV & Security Camera Accessories'),
        (39, 'Hiksemi', 'CCTV & Security Camera Accessories'),
        (40, 'HiLook', 'CCTV & Security Camera Accessories'),
        (41, 'ZKTeco', 'CCTV & Security Camera Accessories'),
        (42, 'Canon', 'Camera & Photography Accessories'),
        (43, 'Nikon', 'Camera & Photography Accessories'),
        (44, 'Sony', 'Camera & Photography Accessories'),
        (45, 'Fujifilm', 'Camera & Photography Accessories'),
        (46, 'Panasonic', 'Camera & Photography Accessories'),
        (47, 'GoPro', 'Camera & Photography Accessories'),
        (48, 'DJI', 'Camera & Photography Accessories'),
        (49, 'Sigma', 'Camera & Photography Accessories'),
        (50, 'Tamron', 'Camera & Photography Accessories'),
        (51, 'SanDisk', 'Camera & Photography Accessories'),
        (52, 'Kingjoy', 'Camera & Photography Accessories'),
        (53, 'Manfrotto', 'Camera & Photography Accessories'),
        (54, 'Godox', 'Camera & Photography Accessories'),
        (55, 'Yongnuo', 'Camera & Photography Accessories'),
        (56, 'Epson', 'Projector & Display Accessories'),
        (57, 'BenQ', 'Projector & Display Accessories'),
        (58, 'ViewSonic', 'Projector & Display Accessories'),
        (59, 'Optoma', 'Projector & Display Accessories'),
        (60, 'Acer', 'Projector & Display Accessories'),
        (61, 'NEC', 'Projector & Display Accessories'),
        (62, 'XGIMI', 'Projector & Display Accessories'),
        (63, 'Wanbo', 'Projector & Display Accessories'),
        (64, 'Anker', 'Cables, Connectors & Power Accessories'),
        (65, 'Belkin', 'Cables, Connectors & Power Accessories'),
        (66, 'Essager', 'Cables, Connectors & Power Accessories'),
        (67, 'LDNIO', 'Cables, Connectors & Power Accessories'),
        (68, 'Aukey', 'Cables, Connectors & Power Accessories'),
        (69, 'Prolink', 'Cables, Connectors & Power Accessories'),
        (70, 'APC', 'Cables, Connectors & Power Accessories'),
        (71, 'Power Guard', 'Cables, Connectors & Power Accessories'),
        (72, 'Cisco', 'Networking & Server Accessories'),
        (73, 'MikroTik', 'Networking & Server Accessories'),
        (74, 'Ruijie', 'Networking & Server Accessories'),
        (75, 'Mercusys', 'Networking & Server Accessories'),
        (76, 'Netgear', 'Networking & Server Accessories'),
        (77, 'D-Link', 'Networking & Server Accessories'),
        (78, 'Ubiquiti', 'Networking & Server Accessories'),
        (79, 'Huawei', 'Networking & Server Accessories'),
        (80, 'JBL', 'Audio Accessories'),
        (81, 'Edifier', 'Audio Accessories')
)
INSERT INTO public.brand (organization_id, brand_name, description, sort_order, is_active)
SELECT o.organization_id, b.name, b.grp, b.position, TRUE
FROM org o
CROSS JOIN brands b
ON CONFLICT (organization_id, lower(brand_name)) DO NOTHING;

-- What Farhad Computer has now, by group.
SELECT b.description AS "group", count(*) AS brands
FROM public.brand b
JOIN public.organization o ON o.organization_id = b.organization_id AND o.organization_slug = 'farhadcomputer'
GROUP BY b.description
ORDER BY min(b.sort_order);

COMMIT;
