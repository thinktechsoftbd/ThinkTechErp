-- fccomdb only (Farhad Computer, organization 6): a ledger head for the service charge (labour) of finalized service
-- invoices, chosen as Inventory -> Settings -> Service charge ledger head.
--   * Map class "Service Income" under Revenue -> Operating Revenue (main class 15, main code 40101).
--   * Sub class (ledger head) "Service Charge Income" in it.
-- Numbered the way the Chart of Accounts screen numbers new nodes (map code: the next 4 digits in the main class;
-- sub code: main code + the next 4 digits; sl: the next 3 digits in the map class). Safe to run again: existing
-- nodes of these names are reused, and a service charge head already chosen is left alone.
BEGIN;

-- As the screen does: hold the main class while the next codes are worked out.
SELECT 1 FROM public.acc_main_class WHERE acc_main_class_id = 15 FOR UPDATE;

INSERT INTO public.acc_map_class (map_code, map_name, prnt_code, ac_code, main_code, acc_main_class_id)
SELECT lpad((coalesce((
           SELECT max(btrim(x.map_code)::int) FROM public.acc_map_class x
           WHERE x.acc_main_class_id = mc.acc_main_class_id AND btrim(x.map_code) ~ '^[0-9]+$'), 0) + 1)::text, 4, '0'),
       'Service Income', NULL, mc.ac_code, mc.main_code, mc.acc_main_class_id
FROM public.acc_main_class mc
WHERE mc.acc_main_class_id = 15
  AND NOT EXISTS (SELECT 1 FROM public.acc_map_class x WHERE x.acc_main_class_id = 15 AND btrim(x.map_name) = 'Service Income');

INSERT INTO public.acc_sub_class (ac_code, main_code, sub_code, sub_name, sl, map_code, acc_map_class_id)
SELECT m.ac_code, m.main_code,
       btrim(m.main_code) || lpad((coalesce((
           SELECT max(substr(btrim(s.sub_code), length(btrim(m.main_code)) + 1)::int)
           FROM public.acc_sub_class s
           WHERE s.main_code IS NOT DISTINCT FROM m.main_code
             AND btrim(s.sub_code) ~ ('^' || btrim(m.main_code) || '[0-9]+$')), 0) + 1)::text, 4, '0'),
       'Service Charge Income',
       lpad((coalesce((
           SELECT max(btrim(s.sl)::int) FROM public.acc_sub_class s
           WHERE s.acc_map_class_id = m.acc_map_class_id AND btrim(s.sl) ~ '^[0-9]+$'), 0) + 1)::text, 3, '0'),
       m.map_code, m.acc_map_class_id
FROM public.acc_map_class m
WHERE m.acc_main_class_id = 15 AND btrim(m.map_name) = 'Service Income'
  AND NOT EXISTS (SELECT 1 FROM public.acc_sub_class s
                  WHERE s.acc_map_class_id = m.acc_map_class_id AND btrim(s.sub_name) = 'Service Charge Income');

UPDATE public.inventory_setting st
SET service_charge_acc_sub_class_id = h.acc_sub_class_id, updated_at = (now() AT TIME ZONE 'utc')
FROM (SELECT s.acc_sub_class_id
      FROM public.acc_sub_class s
      JOIN public.acc_map_class m ON m.acc_map_class_id = s.acc_map_class_id
      WHERE m.acc_main_class_id = 15 AND btrim(m.map_name) = 'Service Income' AND btrim(s.sub_name) = 'Service Charge Income'
      ORDER BY s.acc_sub_class_id LIMIT 1) h
WHERE st.organization_id = 6 AND st.service_charge_acc_sub_class_id IS NULL;

SELECT st.organization_id, st.service_charge_acc_sub_class_id, s.sub_code, s.sub_name, m.map_code, m.map_name
FROM public.inventory_setting st
LEFT JOIN public.acc_sub_class s ON s.acc_sub_class_id = st.service_charge_acc_sub_class_id
LEFT JOIN public.acc_map_class m ON m.acc_map_class_id = s.acc_map_class_id
WHERE st.organization_id = 6;

COMMIT;
