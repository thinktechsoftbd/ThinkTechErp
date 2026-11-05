-- Bill vouchers made from purchase and sales invoices ("finalize").
--   * voucher.purchase_invoice_id / voucher.sales_invoice_id: the invoice a voucher was made from (at most one of
--     the two). Finalizing an invoice posts a Bill Voucher with the link; the invoice stays locked (it can't be
--     cancelled) while the voucher is there, i.e. until it is unposted and deleted (is_deleted) or cancelled.
--   * One live voucher per invoice: the partial unique indexes leave out deleted and cancelled vouchers, so an
--     invoice can be finalized again once its voucher is gone.
-- Run after 029_subscription_plan_sample_data.sql. The script is idempotent.

BEGIN;

ALTER TABLE public.voucher ADD COLUMN IF NOT EXISTS purchase_invoice_id INT8 NULL;
ALTER TABLE public.voucher ADD COLUMN IF NOT EXISTS sales_invoice_id INT8 NULL;

ALTER TABLE public.voucher DROP CONSTRAINT IF EXISTS fk_voucher_purchase_invoice;
ALTER TABLE public.voucher
    ADD CONSTRAINT fk_voucher_purchase_invoice
        FOREIGN KEY (purchase_invoice_id) REFERENCES public.purchase_invoice (purchase_invoice_id);

ALTER TABLE public.voucher DROP CONSTRAINT IF EXISTS fk_voucher_sales_invoice;
ALTER TABLE public.voucher
    ADD CONSTRAINT fk_voucher_sales_invoice
        FOREIGN KEY (sales_invoice_id) REFERENCES public.sales_invoice (sales_invoice_id);

ALTER TABLE public.voucher DROP CONSTRAINT IF EXISTS ck_voucher_one_invoice;
ALTER TABLE public.voucher
    ADD CONSTRAINT ck_voucher_one_invoice CHECK (purchase_invoice_id IS NULL OR sales_invoice_id IS NULL);

CREATE UNIQUE INDEX IF NOT EXISTS ux_voucher_purchase_invoice
    ON public.voucher (purchase_invoice_id)
    WHERE purchase_invoice_id IS NOT NULL AND NOT is_deleted AND status <> 'Cancelled';

CREATE UNIQUE INDEX IF NOT EXISTS ux_voucher_sales_invoice
    ON public.voucher (sales_invoice_id)
    WHERE sales_invoice_id IS NOT NULL AND NOT is_deleted AND status <> 'Cancelled';

COMMENT ON COLUMN public.voucher.purchase_invoice_id IS 'The purchase invoice this bill voucher was made from (finalize).';
COMMENT ON COLUMN public.voucher.sales_invoice_id IS 'The sales invoice this bill voucher was made from (finalize).';

COMMIT;
