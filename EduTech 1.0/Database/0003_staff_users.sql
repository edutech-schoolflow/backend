-- Global, standalone staff identity (one per person, keyed by phone). Per-school affiliations
-- (staff_affiliations) belong to the Staff module and are created in a later migration.
CREATE TABLE staff_users (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    full_name           VARCHAR(255) NOT NULL,
    phone               VARCHAR(20) NOT NULL UNIQUE,
    email               VARCHAR(255) UNIQUE,
    password_hash       TEXT,                       -- NULL until account setup (register or invite-accept)
    phone_verified      BOOLEAN NOT NULL DEFAULT FALSE,
    email_verified      BOOLEAN NOT NULL DEFAULT FALSE,
    kyc_status          VARCHAR(50) NOT NULL DEFAULT 'not_submitted',  -- platform-level (Dojah)
    nin                 TEXT,                       -- encrypted at rest; never logged/returned
    dojah_reference     VARCHAR(120),
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_count  INT NOT NULL DEFAULT 0,
    locked_until        TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
