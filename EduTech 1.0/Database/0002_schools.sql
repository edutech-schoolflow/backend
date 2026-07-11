-- Tenant root. A school starts pending_kyc; name/subdomain filled during KYC, provisioned on approval.
CREATE TABLE schools (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(255),
    subdomain           VARCHAR(100) UNIQUE,
    type                VARCHAR(50),
    phone               VARCHAR(20),
    email               VARCHAR(255),
    address             TEXT,
    city                VARCHAR(120),
    state               VARCHAR(120),
    logo_url            TEXT,
    location_lat        DECIMAL(9,6),
    location_lng        DECIMAL(9,6),
    status              VARCHAR(50) NOT NULL DEFAULT 'pending_kyc',   -- pending_kyc | active | suspended
    kyc_status          VARCHAR(50) NOT NULL DEFAULT 'not_submitted',
    payments_enabled    BOOLEAN NOT NULL DEFAULT FALSE,
    visibility          VARCHAR(20) NOT NULL DEFAULT 'hidden',        -- hidden | public
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- The legal proprietor account (1:1 with a school). Phone is the primary identity.
CREATE TABLE school_owners (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id           UUID NOT NULL REFERENCES schools(id),
    full_name           VARCHAR(255) NOT NULL,
    phone               VARCHAR(20) NOT NULL UNIQUE,
    email               VARCHAR(255) UNIQUE,
    password_hash       TEXT NOT NULL,
    phone_verified      BOOLEAN NOT NULL DEFAULT FALSE,
    email_verified      BOOLEAN NOT NULL DEFAULT FALSE,
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_count  INT NOT NULL DEFAULT 0,
    locked_until        TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_school_owners_school_id ON school_owners (school_id);

-- Optional email verification (phone OTP is primary; email is secondary).
CREATE TABLE email_verification_tokens (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id        UUID NOT NULL REFERENCES school_owners(id),
    token_hash      TEXT NOT NULL,
    expires_at      TIMESTAMPTZ NOT NULL,
    used_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
