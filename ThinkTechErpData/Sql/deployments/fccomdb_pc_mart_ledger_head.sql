-- fccomdb only (Farhad Computer, organization 6): a ledger head for the party pc mart (supplier_id 26, S-00003, a
-- customer and a supplier) under Accounts Receivables (map class 17, main code 10203), numbered the way the Chart of
-- Accounts screen numbers a new head (sub code = main code + the next 4 digits; sl = the next 3 digits in the map
-- class), and mapped to pc mart so its credit sales and service invoices can be finalized. Safe to run again: an
-- existing "pc mart" head is reused, and a party that already has a head is left alone.
BEGIN;

-- As the screen does: hold the main class while the next code is worked out.
SELECT 1 FROM public.acc_main_class WHERE acc_main_class_id = 4 FOR UPDATE;

INSERT INTO public.acc_sub_class (ac_code, main_code, sub_code, sub_name, sl, map_code, acc_map_class_id)
SELECT m.ac_code, m.main_code,
       btrim(m.main_code) || lpad((coalesce((
           SELECT max(substr(btrim(s.sub_code), length(btrim(m.main_code)) + 1)::int)
           FROM public.acc_sub_class s
           WHERE s.main_code IS NOT DISTINCT FROM m.main_code
             AND btrim(s.sub_code) ~ ('^' || btrim(m.main_code) || '[0-9]+$')), 0) + 1)::text, 4, '0'),
       'pc mart',
       lpad((coalesce((
           SELECT max(btrim(s.sl)::int)
           FROM public.acc_sub_class s
           WHERE s.acc_map_class_id = m.acc_map_class_id AND btrim(s.sl) ~ '^[0-9]+$'), 0) + 1)::text, 3, '0'),
       m.map_code, m.acc_map_class_id
FROM public.acc_map_class m
WHERE m.acc_map_class_id = 17
  AND NOT EXISTS (SELECT 1 FROM public.acc_sub_class x WHERE x.acc_map_class_id = 17 AND btrim(x.sub_name) = 'pc mart');

UPDATE public.supplier p
SET acc_sub_class_id = h.acc_sub_class_id, updated_at = (now() AT TIME ZONE 'utc')
FROM (SELECT acc_sub_class_id FROM public.acc_sub_class
      WHERE acc_map_class_id = 17 AND btrim(sub_name) = 'pc mart'
      ORDER BY acc_sub_class_id LIMIT 1) h
WHERE p.organization_id = 6 AND p.supplier_id = 26 AND p.supplier_name = 'pc mart' AND p.acc_sub_class_id IS NULL;

SELECT p.supplier_id, p.supplier_code, p.supplier_name, p.acc_sub_class_id, s.sub_code, s.sub_name, s.sl
FROM public.supplier p
LEFT JOIN public.acc_sub_class s ON s.acc_sub_class_id = p.acc_sub_class_id
WHERE p.organization_id = 6 AND p.supplier_id = 26;

COMMIT;
