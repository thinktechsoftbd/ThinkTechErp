-- Payment Source of a sales invoice and a service invoice, as on a purchase invoice (022): the line
-- (inv_payment_source_d) whose ledger head the money taken now goes to. Nullable: invoices entered before
-- this column, and sales invoices billed from a service job, have none. Safe to run again.
ALTER TABLE public.sales_invoice
    ADD COLUMN IF NOT EXISTS inv_payment_source_d_id INT NULL;

ALTER TABLE public.service_invoice
    ADD COLUMN IF NOT EXISTS inv_payment_source_d_id INT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sales_invoice_payment_source_d') THEN
        ALTER TABLE public.sales_invoice
            ADD CONSTRAINT fk_sales_invoice_payment_source_d
            FOREIGN KEY (inv_payment_source_d_id) REFERENCES public.inv_payment_source_d (id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_service_invoice_payment_source_d') THEN
        ALTER TABLE public.service_invoice
            ADD CONSTRAINT fk_service_invoice_payment_source_d
            FOREIGN KEY (inv_payment_source_d_id) REFERENCES public.inv_payment_source_d (id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_sales_invoice_payment_source_d
    ON public.sales_invoice (inv_payment_source_d_id)
    WHERE inv_payment_source_d_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_service_invoice_payment_source_d
    ON public.service_invoice (inv_payment_source_d_id)
    WHERE inv_payment_source_d_id IS NOT NULL;
