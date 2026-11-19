-- Farhad Computer (fc.thinktechsoft.com, database fccomdb): 54 units for a computer, laptop, CCTV, camera,
-- projector and IT accessories shop (Inventory -> Settings -> Units), from the shop's unit list: general,
-- computer & electronics, cable & measurement, packaging and other measurement units. A unit listed in more than one
-- group (Pcs, Pair, Set, Roll, Coil, Bdl, Mtr, Ft) is added once.
--
-- unit_type follows ck_unit_type; fractions are allowed for measured units (length, weight, volume, area and hours);
-- iso_code is the UN/ECE Recommendation 20 code where one is well established. Idempotent: a unit whose code the
-- organization already has (any letter case) is left as it is. Run on fccomdb only.

BEGIN;

WITH org AS (
    SELECT organization_id FROM public.organization WHERE organization_slug = 'farhadcomputer'
),
units (code, name, unit_type, iso, allow_decimal) AS (
    VALUES
        ('Pcs'     , 'Piece'        , 'Count'  , 'H87' , FALSE),
        ('Unit'    , 'Unit'         , 'Count'  , NULL  , FALSE),
        ('Set'     , 'Set'          , 'Count'  , 'SET' , FALSE),
        ('Pair'    , 'Pair'         , 'Count'  , 'PR'  , FALSE),
        ('Box'     , 'Box'          , 'Count'  , NULL  , FALSE),
        ('Pack'    , 'Pack'         , 'Count'  , NULL  , FALSE),
        ('Pkt'     , 'Packet'       , 'Count'  , NULL  , FALSE),
        ('Ctn'     , 'Carton'       , 'Count'  , NULL  , FALSE),
        ('Bdl'     , 'Bundle'       , 'Count'  , NULL  , FALSE),
        ('Roll'    , 'Roll'         , 'Count'  , NULL  , FALSE),
        ('Coil'    , 'Coil'         , 'Count'  , NULL  , FALSE),
        ('Doz'     , 'Dozen'        , 'Count'  , 'DZN' , FALSE),
        ('Lot'     , 'Lot'          , 'Count'  , NULL  , FALSE),
        ('Kit'     , 'Kit'          , 'Count'  , NULL  , FALSE),
        ('Mod'     , 'Module'       , 'Count'  , NULL  , FALSE),
        ('Card'    , 'Card'         , 'Count'  , NULL  , FALSE),
        ('Stick'   , 'Stick'        , 'Count'  , NULL  , FALSE),
        ('Drive'   , 'Drive'        , 'Count'  , NULL  , FALSE),
        ('Disk'    , 'Disk'         , 'Count'  , NULL  , FALSE),
        ('Board'   , 'Board'        , 'Count'  , NULL  , FALSE),
        ('Adapter' , 'Adapter'      , 'Count'  , NULL  , FALSE),
        ('Conn'    , 'Connector'    , 'Count'  , NULL  , FALSE),
        ('Cable'   , 'Cable'        , 'Count'  , NULL  , FALSE),
        ('Mtr'     , 'Meter'        , 'Length' , 'MTR' , TRUE ),
        ('Ft'      , 'Foot'         , 'Length' , 'FOT' , TRUE ),
        ('Cm'      , 'Centimeter'   , 'Length' , 'CMT' , TRUE ),
        ('Mm'      , 'Millimeter'   , 'Length' , 'MMT' , TRUE ),
        ('Inch'    , 'Inch'         , 'Length' , 'INH' , TRUE ),
        ('Yd'      , 'Yard'         , 'Length' , 'YRD' , TRUE ),
        ('Km'      , 'Kilometer'    , 'Length' , 'KMT' , TRUE ),
        ('Reel'    , 'Reel'         , 'Count'  , NULL  , FALSE),
        ('Spool'   , 'Spool'        , 'Count'  , NULL  , FALSE),
        ('Bag'     , 'Bag'          , 'Count'  , NULL  , FALSE),
        ('Btl'     , 'Bottle'       , 'Count'  , NULL  , FALSE),
        ('Can'     , 'Can'          , 'Count'  , NULL  , FALSE),
        ('Case'    , 'Case'         , 'Count'  , NULL  , FALSE),
        ('Cart'    , 'Cartridge'    , 'Count'  , NULL  , FALSE),
        ('Env'     , 'Envelope'     , 'Count'  , NULL  , FALSE),
        ('Pallet'  , 'Pallet'       , 'Count'  , NULL  , FALSE),
        ('Tray'    , 'Tray'         , 'Count'  , NULL  , FALSE),
        ('Tube'    , 'Tube'         , 'Count'  , NULL  , FALSE),
        ('Sheet'   , 'Sheet'        , 'Count'  , NULL  , FALSE),
        ('Slv'     , 'Sleeve'       , 'Count'  , NULL  , FALSE),
        ('Gm'      , 'Gram'         , 'Weight' , 'GRM' , TRUE ),
        ('Kg'      , 'Kilogram'     , 'Weight' , 'KGM' , TRUE ),
        ('Mg'      , 'Milligram'    , 'Weight' , 'MGM' , TRUE ),
        ('Ltr'     , 'Liter'        , 'Volume' , 'LTR' , TRUE ),
        ('Ml'      , 'Milliliter'   , 'Volume' , 'MLT' , TRUE ),
        ('Sqm'     , 'Square Meter' , 'Area'   , 'MTK' , TRUE ),
        ('Sqft'    , 'Square Foot'  , 'Area'   , 'FTK' , TRUE ),
        ('Hr'      , 'Hour'         , 'Time'   , 'HUR' , TRUE ),
        ('Day'     , 'Day'          , 'Time'   , 'DAY' , FALSE),
        ('Mon'     , 'Month'        , 'Time'   , 'MON' , FALSE),
        ('Yr'      , 'Year'         , 'Time'   , 'ANN' , FALSE)
)
INSERT INTO public.unit (organization_id, unit_code, unit_name, unit_type, iso_code, allow_decimal, is_active)
SELECT o.organization_id, u.code, u.name, u.unit_type, u.iso, u.allow_decimal, TRUE
FROM org o
CROSS JOIN units u
ON CONFLICT (organization_id, lower(unit_code)) DO NOTHING;

-- What Farhad Computer has now, by type.
SELECT u.unit_type, count(*) AS units, string_agg(u.unit_code, ', ' ORDER BY u.unit_id) AS codes
FROM public.unit u
JOIN public.organization o ON o.organization_id = u.organization_id AND o.organization_slug = 'farhadcomputer'
GROUP BY u.unit_type
ORDER BY count(*) DESC;

COMMIT;
