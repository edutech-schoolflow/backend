-- Standalone, school-agnostic parent accounts (keyed by phone). Children/enrollments/virtual
-- accounts belong to their own modules and come in later migrations.
CREATE TABLE parents (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    full_name           VARCHAR(255) NOT NULL,
    phone               VARCHAR(20) NOT NULL UNIQUE,
    email               VARCHAR(255) UNIQUE,
    password_hash       TEXT,                       -- NULL until setup (register or school-add claim)
    phone_verified      BOOLEAN NOT NULL DEFAULT FALSE,
    payment_pin_hash    TEXT,                       -- 6-digit PIN (BCrypt); NULL until first set
    status              VARCHAR(50) NOT NULL DEFAULT 'pending',  -- pending | active
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_count  INT NOT NULL DEFAULT 0,
    locked_until        TIMESTAMPTZ,
    pin_failed_count    INT NOT NULL DEFAULT 0,
    pin_locked_until    TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
