-- Standard warranty policies for computer sales and accessories (Warranty -> Policies), added to every active
-- internal organization: 12 policies with their descriptions as the card terms.
--
-- * "No Warranty" is not a row: a product without a policy has no warranty (product.warranty_policy_id NULL),
--   and ck_warranty_policy_period requires warranty_months > 0.
-- * The periods below are starting values; edit them here before running, or later per organization in
--   Warranty -> Policies.
-- * Idempotent: a policy whose name the organization already has (any letter case) is left as it is, so
--   periods or terms changed in the app are never overwritten.
-- * To target other organizations, change the target_orgs list below (e.g. WHERE organization_id IN (2, 7)).

BEGIN;

WITH target_orgs AS (
    SELECT organization_id
    FROM public.organization
    WHERE is_active AND NOT is_external
),
policies (policy_name, warranty_months, replacement_days, covers_parts, covers_labor, terms) AS (
    VALUES
        -- name                     months  replace  parts  labor  terms (printed on the warranty card)
        ('Official Warranty',          12,       7,  TRUE,  TRUE,  'Manufacturer-provided warranty.'),
        ('Brand Warranty',             12,       7,  TRUE,  TRUE,  'Brand-authorized warranty.'),
        ('Seller Warranty',             6,       7,  TRUE,  TRUE,  'Warranty provided by the seller.'),
        ('Service Warranty',           12,       0, FALSE,  TRUE,  'Service and repair coverage: labor is free, parts are charged.'),
        ('Replacement Warranty',       12,     365,  TRUE,  TRUE,  'Product replacement coverage: a faulty unit is replaced during the warranty.'),
        ('Parts Warranty',             12,       0,  TRUE, FALSE,  'Selected parts covered: the parts are free, labor is charged.'),
        ('Limited Warranty',            6,       0,  TRUE,  TRUE,  'Limited coverage under specific conditions.'),
        ('International Warranty',     12,       0,  TRUE,  TRUE,  'International manufacturer warranty.'),
        ('Extended Warranty',          24,       0,  TRUE,  TRUE,  'Additional warranty period.'),
        ('Carry-in Warranty',          12,       0,  TRUE,  TRUE,  'Carry-in: the customer brings the product to the service center.'),
        ('On-site Warranty',           12,       0,  TRUE,  TRUE,  'On-site: service is provided at the customer''s location.'),
        ('DOA Replacement',             1,       7,  TRUE,  TRUE,  'Dead on Arrival: a unit faulty at delivery is replaced within 7 days.')
)
INSERT INTO public.warranty_policy
    (organization_id, policy_name, warranty_months, replacement_days, covers_parts, covers_labor, terms, is_active)
SELECT o.organization_id, p.policy_name, p.warranty_months, p.replacement_days, p.covers_parts, p.covers_labor, p.terms, TRUE
FROM target_orgs o
CROSS JOIN policies p
ON CONFLICT (organization_id, lower(policy_name)) DO NOTHING;

-- What each organization has now.
SELECT o.organization_name_en AS organization, w.policy_name, w.warranty_months AS months,
       w.replacement_days AS replacement_days, w.covers_parts AS parts, w.covers_labor AS labor, w.is_active AS active
FROM public.warranty_policy w
JOIN public.organization o ON o.organization_id = w.organization_id
ORDER BY o.organization_name_en, w.warranty_months DESC, w.policy_name;

COMMIT;
