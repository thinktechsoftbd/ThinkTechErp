-- A ledger head (a sub class of the chart, public.acc_sub_class) can be mapped to each product category
-- and each supplier (run after 021).
--
--   * public.category.acc_sub_class_id: set in Inventory -> Categories (add/edit).
--   * public.supplier.acc_sub_class_id: set in Purchase -> Suppliers (add/edit). Suppliers and customers
--     share public.supplier, so a party that is both has one mapped head.
--   * Both are optional. A sub class that a category or supplier is mapped to can't be deleted.
--
-- The script is idempotent: running it again changes nothing.

BEGIN;

ALTER TABLE public.category
    ADD COLUMN IF NOT EXISTS acc_sub_class_id INT NULL;
ALTER TABLE public.supplier
    ADD COLUMN IF NOT EXISTS acc_sub_class_id INT NULL;

ALTER TABLE public.category
    DROP CONSTRAINT IF EXISTS fk_category_acc_sub_class;
ALTER TABLE public.category
    ADD CONSTRAINT fk_category_acc_sub_class
        FOREIGN KEY (acc_sub_class_id) REFERENCES public.acc_sub_class (acc_sub_class_id);

ALTER TABLE public.supplier
    DROP CONSTRAINT IF EXISTS fk_supplier_acc_sub_class;
ALTER TABLE public.supplier
    ADD CONSTRAINT fk_supplier_acc_sub_class
        FOREIGN KEY (acc_sub_class_id) REFERENCES public.acc_sub_class (acc_sub_class_id);

-- So deleting a sub class finds the rows that still point at it without scanning the tables.
CREATE INDEX IF NOT EXISTS ix_category_acc_sub_class ON public.category (acc_sub_class_id) WHERE acc_sub_class_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_supplier_acc_sub_class ON public.supplier (acc_sub_class_id) WHERE acc_sub_class_id IS NOT NULL;

COMMENT ON COLUMN public.category.acc_sub_class_id IS 'The ledger head (acc_sub_class) mapped to the category; optional.';
COMMENT ON COLUMN public.supplier.acc_sub_class_id IS 'The ledger head (acc_sub_class) mapped to the supplier/customer; optional.';

COMMIT;
