-- Sample subscription plans (Starter, Business, Enterprise) with their features and details, in BDT.
-- Run after 028_subscription_plans.sql. A plan whose name is already there is left as it is, so the script
-- can be run again.

BEGIN;

DO $$
DECLARE
    bdt_id      INT8;
    plan_id     INT4;
    feature_id  INT4;
BEGIN
    SELECT d.lookup_data_id INTO bdt_id
    FROM public.lookup_data d
    JOIN public.lookup_table t USING (lookup_table_id)
    WHERE lower(t.lookup_table_name) = 'currency' AND upper(d.lookup_data_name) = 'BDT'
    LIMIT 1;
    IF bdt_id IS NULL THEN
        RAISE EXCEPTION 'There is no BDT value in the Currency lookup table to price the sample plans in.';
    END IF;

    -- Starter: monthly, no discount.
    IF NOT EXISTS (SELECT 1 FROM public.subscription_plan WHERE lower(subscription_plan_name_en) = 'starter') THEN
        INSERT INTO public.subscription_plan (subscription_plan_name_en, subscription_plan_name_loc, plan_short_note,
            plan_regular_price, plan_discount_pct, plan_discount_price, rf_currency_id, loc_exchange_rate, billing_period_type)
        VALUES ('Starter', 'স্টার্টার', 'For a single shop getting started.', 1500, 0, 1500, bdt_id, 1, 'Monthly')
        RETURNING id INTO plan_id;

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Inventory') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Up to 500 products'), (feature_id, '1 warehouse'), (feature_id, 'Stock adjustments and counts');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Sales') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Cash and credit sales'), (feature_id, 'Customer dues');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Support') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Email support');
    END IF;

    -- Business: monthly, 10% off.
    IF NOT EXISTS (SELECT 1 FROM public.subscription_plan WHERE lower(subscription_plan_name_en) = 'business') THEN
        INSERT INTO public.subscription_plan (subscription_plan_name_en, subscription_plan_name_loc, plan_short_note,
            plan_regular_price, plan_discount_pct, plan_discount_price, rf_currency_id, loc_exchange_rate, billing_period_type)
        VALUES ('Business', 'বিজনেস', 'For growing shops with a godown.', 3000, 10, 2700, bdt_id, 1, 'Monthly')
        RETURNING id INTO plan_id;

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Inventory') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Unlimited products'), (feature_id, 'Up to 3 warehouses'), (feature_id, 'Batch and expiry tracking');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Purchase') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Purchase orders and goods receipts'), (feature_id, 'Supplier ledger and dues');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Sales') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Sales invoices and returns'), (feature_id, 'Customer ledger and money receipts');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'SMS') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, '500 SMS per month');
    END IF;

    -- Enterprise: yearly, 20% off.
    IF NOT EXISTS (SELECT 1 FROM public.subscription_plan WHERE lower(subscription_plan_name_en) = 'enterprise') THEN
        INSERT INTO public.subscription_plan (subscription_plan_name_en, subscription_plan_name_loc, plan_short_note,
            plan_regular_price, plan_discount_pct, plan_discount_price, rf_currency_id, loc_exchange_rate, billing_period_type)
        VALUES ('Enterprise', 'এন্টারপ্রাইজ', 'Everything, for businesses with several branches.', 60000, 20, 48000, bdt_id, 1, 'Yearly')
        RETURNING id INTO plan_id;

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Inventory & Purchase') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Unlimited warehouses and stock transfers'), (feature_id, 'Landed cost on purchases');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Accounting') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Chart of accounts and vouchers'), (feature_id, 'Ledger and trial balance reports');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Users & Security') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Unlimited users with roles'), (feature_id, 'API clients for integrations');

        INSERT INTO public.subscription_plan_feature_h (subscription_plan_id, feature_title) VALUES (plan_id, 'Support') RETURNING id INTO feature_id;
        INSERT INTO public.subscription_plan_feature_d (subscription_plan_feature_h_id, feature_description)
        VALUES (feature_id, 'Priority phone support'), (feature_id, 'Free onboarding and training');
    END IF;
END;
$$;

COMMIT;
