-- Payment Source of a purchase invoice: the line (inv_payment_source_d) whose ledger head the
-- invoice's payment posts to. Nullable: invoices entered before this column have none.
ALTER TABLE public.purchase_invoice
    ADD COLUMN IF NOT EXISTS inv_payment_source_d_id INT NULL;

ALTER TABLE public.purchase_invoice
    ADD CONSTRAINT fk_purchase_invoice_payment_source_d
    FOREIGN KEY (inv_payment_source_d_id) REFERENCES public.inv_payment_source_d (id);

CREATE INDEX IF NOT EXISTS idx_purchase_invoice_payment_source_d
    ON public.purchase_invoice (inv_payment_source_d_id)
    WHERE inv_payment_source_d_id IS NOT NULL;
