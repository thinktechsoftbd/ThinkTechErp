-- Farhad Computer (fc.thinktechsoft.com, database fccomdb): 3 sample computer products to try the product screens
-- with: a wireless mouse, an NVMe SSD and a Wi-Fi router. Barcodes are test codes from the in-shop 200... range.
--
-- Does what the API does when a product is created with an empty code: takes the next P-00001, P-00002, ... from
-- document_sequence (skipping codes already used), writes the product, its base unit row (Pcs, the default for
-- buying and selling), its barcode (on the base unit, unit_id NULL) and its tags (created by slug if new).
-- No tax (the organization has no tax groups yet); no images. Idempotent: a product whose barcode the organization
-- already has is skipped. Run on fccomdb only.

BEGIN;

DO $$
DECLARE
    v_org  int;
    v_pid  int;
    v_code text;
    p      record;
BEGIN
    SELECT organization_id INTO STRICT v_org FROM public.organization WHERE organization_slug = 'farhadcomputer';

    INSERT INTO public.document_sequence (organization_id, document_type, fiscal_year, prefix, number_padding)
    VALUES (v_org, 'Product', 0, 'P-', 5)
    ON CONFLICT ON CONSTRAINT ux_document_sequence DO NOTHING;

    FOR p IN
        SELECT *
        FROM (VALUES
            (1, '2001000000012', 'Logitech MX Master 3S Wireless Mouse', 'লজিটেক এমএক্স মাস্টার ৩এস ওয়্যারলেস মাউস',
             'Computer Accessories', 'Mouse', 'Logitech', 'None', 12500::numeric, 13500::numeric, 3::numeric, 10::numeric,
             'Wireless optical mouse', '847160', 'CN',
             '{"model": "MX Master 3S", "colour": "Graphite", "warranty": "1 year"}'::jsonb,
             ARRAY['Wireless', 'Bluetooth']),
            (2, '2001000000029', 'Kingston NV2 500GB M.2 NVMe SSD', 'কিংস্টন এনভি২ ৫০০ জিবি এনভিএমই এসএসডি',
             'Storage & Memory', 'NVMe SSD', 'Kingston', 'Serial', 4200::numeric, 4500::numeric, 5::numeric, 20::numeric,
             'Solid state drive', '852351', 'TW',
             '{"capacity": "500GB", "interface": "PCIe 4.0 NVMe", "form factor": "M.2 2280", "warranty": "3 years"}'::jsonb,
             ARRAY['SSD', 'NVMe']),
            (3, '2001000000036', 'TP-Link Archer C6 AC1200 Dual Band Wi-Fi Router', 'টিপি-লিংক আর্চার সি৬ ডুয়াল ব্যান্ড ওয়াই-ফাই রাউটার',
             'Networking Products', 'Wi-Fi Router', 'TP-Link', 'Serial', 3300::numeric, 3500::numeric, 3::numeric, 10::numeric,
             'Wireless router', '851762', 'CN',
             '{"speed": "AC1200", "bands": "2.4 GHz + 5 GHz", "ports": "4 LAN + 1 WAN (Gigabit)", "warranty": "1 year"}'::jsonb,
             ARRAY['Router', 'Dual Band'])
        ) AS v (seq, barcode, name, name_local, parent_category, category, brand, tracking, sale_price, mrp,
                reorder_level, reorder_quantity, generic_name, hs_code, country, attributes, tags)
        ORDER BY seq
    LOOP
        IF EXISTS (SELECT 1 FROM public.product_barcode WHERE organization_id = v_org AND barcode = p.barcode) THEN
            RAISE NOTICE 'Skipped %: barcode % is already used', p.name, p.barcode;
            CONTINUE;
        END IF;

        -- The next product code, skipping numbers already typed by hand.
        LOOP
            UPDATE public.document_sequence
            SET next_number = next_number + 1, updated_at = (now() AT TIME ZONE 'utc')
            WHERE organization_id = v_org AND document_type = 'Product' AND fiscal_year = 0
            RETURNING prefix || lpad((next_number - 1)::text, number_padding, '0') INTO v_code;
            EXIT WHEN NOT EXISTS (SELECT 1 FROM public.product WHERE organization_id = v_org AND lower(product_code) = lower(v_code));
        END LOOP;

        INSERT INTO public.product (
            organization_id, product_code, product_name, product_name_local, product_type, category_id, brand_id,
            base_unit_id, tracking_type, has_expiry, generic_name, hs_code, country_of_origin, sale_price, mrp,
            reorder_level, reorder_quantity, attributes, is_active, created_at)
        VALUES (
            v_org, v_code, p.name, p.name_local, 'Stock',
            (SELECT c.category_id FROM public.category c
             JOIN public.category parent ON parent.category_id = c.parent_category_id
             WHERE c.organization_id = v_org AND c.category_name = p.category AND parent.category_name = p.parent_category),
            (SELECT b.brand_id FROM public.brand b WHERE b.organization_id = v_org AND b.brand_name = p.brand),
            (SELECT u.unit_id FROM public.unit u WHERE u.organization_id = v_org AND u.unit_code = 'Pcs'),
            p.tracking, FALSE, p.generic_name, p.hs_code, p.country, p.sale_price, p.mrp,
            p.reorder_level, p.reorder_quantity, p.attributes, TRUE, (now() AT TIME ZONE 'utc'))
        RETURNING product_id INTO v_pid;

        INSERT INTO public.product_unit (product_id, unit_id, conversion_factor, sale_price, is_purchase_default, is_sale_default, created_at)
        SELECT v_pid, base_unit_id, 1, NULL, TRUE, TRUE, (now() AT TIME ZONE 'utc') FROM public.product WHERE product_id = v_pid;

        INSERT INTO public.product_barcode (organization_id, product_id, unit_id, barcode, created_at)
        VALUES (v_org, v_pid, NULL, p.barcode, (now() AT TIME ZONE 'utc'));

        -- Tags are shared and matched by slug (lower case, other characters as '-'), as the API does.
        INSERT INTO public.tag (name, slug)
        SELECT t, trim(BOTH '-' FROM regexp_replace(lower(trim(t)), '[^[:alnum:]]+', '-', 'g'))
        FROM unnest(p.tags) AS t
        ON CONFLICT (slug) DO NOTHING;
        INSERT INTO public.product_tag (product_id, tag_id)
        SELECT v_pid, tg.id FROM public.tag tg
        WHERE tg.slug IN (SELECT trim(BOTH '-' FROM regexp_replace(lower(trim(t)), '[^[:alnum:]]+', '-', 'g')) FROM unnest(p.tags) AS t)
        ON CONFLICT DO NOTHING;

        RAISE NOTICE 'Added % %', v_code, p.name;
    END LOOP;
END $$;

-- What Farhad Computer has now.
SELECT p.product_code, p.product_name, c.category_name AS category, b.brand_name AS brand, u.unit_code AS unit,
       p.tracking_type, p.sale_price, p.mrp,
       (SELECT string_agg(pb.barcode, ', ') FROM public.product_barcode pb WHERE pb.product_id = p.product_id) AS barcode,
       (SELECT string_agg(tg.name, ', ' ORDER BY tg.name) FROM public.product_tag pt JOIN public.tag tg ON tg.id = pt.tag_id
        WHERE pt.product_id = p.product_id) AS tags
FROM public.product p
JOIN public.organization o ON o.organization_id = p.organization_id AND o.organization_slug = 'farhadcomputer'
LEFT JOIN public.category c ON c.category_id = p.category_id
LEFT JOIN public.brand b ON b.brand_id = p.brand_id
JOIN public.unit u ON u.unit_id = p.base_unit_id
ORDER BY p.product_code;

COMMIT;
