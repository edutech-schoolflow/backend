-- Platform-wide settings, editable by Platform Admin (finance/super). Generic key/value so other
-- knobs can be added without schema churn. Seeds the payment platform fee — a FLAT amount added on
-- top of every payment (like a transfer fee), NOT a percentage.

CREATE TABLE platform_settings (
    key        VARCHAR(80) PRIMARY KEY,
    value      VARCHAR(255) NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO platform_settings (key, value)
VALUES ('payment.platform_fee', '50')
ON CONFLICT (key) DO NOTHING;
