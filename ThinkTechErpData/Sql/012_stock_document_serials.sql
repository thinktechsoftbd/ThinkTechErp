-- Serial numbers typed or scanned on stock adjustment and stock transfer lines (run after 009),
-- like purchase.goods_receipt_serial: kept with the draft, resolved to serial_number rows when posted.
-- The script is idempotent: running it again changes nothing.

BEGIN;

CREATE TABLE IF NOT EXISTS inventory.stock_adjustment_serial
(
    stock_adjustment_line_id BIGINT NOT NULL,
    serial_no                VARCHAR(100) NOT NULL,
    serial_number_id         BIGINT NULL,   -- set when posted

    CONSTRAINT pk_stock_adjustment_serial PRIMARY KEY (stock_adjustment_line_id, serial_no),
    CONSTRAINT fk_stock_adjustment_serial_line
        FOREIGN KEY (stock_adjustment_line_id)
        REFERENCES inventory.stock_adjustment_line (stock_adjustment_line_id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_adjustment_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

CREATE TABLE IF NOT EXISTS inventory.stock_transfer_serial
(
    stock_transfer_line_id BIGINT NOT NULL,
    serial_no              VARCHAR(100) NOT NULL,
    serial_number_id       BIGINT NULL,   -- set when dispatched

    CONSTRAINT pk_stock_transfer_serial PRIMARY KEY (stock_transfer_line_id, serial_no),
    CONSTRAINT fk_stock_transfer_serial_line
        FOREIGN KEY (stock_transfer_line_id)
        REFERENCES inventory.stock_transfer_line (stock_transfer_line_id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_transfer_serial_serial
        FOREIGN KEY (serial_number_id) REFERENCES inventory.serial_number (serial_number_id)
);

COMMIT;
