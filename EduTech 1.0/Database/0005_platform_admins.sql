-- Internal operators. Seeded only (no public registration). Email + password + mandatory TOTP.
CREATE TABLE platform_admins (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    full_name           VARCHAR(255) NOT NULL,
    email               VARCHAR(255) NOT NULL UNIQUE,
    password_hash       TEXT NOT NULL,
    role                VARCHAR(40) NOT NULL,       -- super_admin | compliance_reviewer | finance | support
    totp_secret         TEXT,                       -- encrypted; set at MFA enrolment
    totp_enrolled       BOOLEAN NOT NULL DEFAULT FALSE,
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    created_by          UUID REFERENCES platform_admins(id),
    failed_login_count  INT NOT NULL DEFAULT 0,
    locked_until        TIMESTAMPTZ,
    last_login_at       TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
