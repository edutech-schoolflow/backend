-- Shared, GLOBAL auth tables (no school_id). Used across all actors.

-- One-time phone OTPs (BCrypt-hashed; verified by looking up the latest active row per target).
CREATE TABLE otp_codes (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    target_type     VARCHAR(50) NOT NULL,   -- OtpPurpose (e.g. school_owner_phone_verification)
    target_id       UUID NOT NULL,          -- the actor/record the OTP is for
    phone           VARCHAR(20) NOT NULL,   -- recipient
    code_hash       TEXT NOT NULL,
    expires_at      TIMESTAMPTZ NOT NULL,
    attempts        INT NOT NULL DEFAULT 0,
    used_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_otp_codes_lookup ON otp_codes (target_type, target_id, used_at);

-- Rotating refresh tokens (SHA-256-hashed). Looked up by token_hash; rotated one-time-use.
CREATE TABLE refresh_tokens (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_type      VARCHAR(50) NOT NULL,   -- AuthActorTypes (school_owner|staff|parent|platform_admin)
    actor_id        UUID NOT NULL,
    token_hash      TEXT NOT NULL,
    family_id       UUID NOT NULL,          -- rotation lineage; reuse revokes the family
    issued_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at      TIMESTAMPTZ NOT NULL,
    rotated_at      TIMESTAMPTZ,
    revoked_at      TIMESTAMPTZ,
    ip_address      VARCHAR(64),
    user_agent      TEXT
);
CREATE UNIQUE INDEX ux_refresh_tokens_hash ON refresh_tokens (token_hash);
CREATE INDEX ix_refresh_tokens_actor ON refresh_tokens (actor_type, actor_id);
CREATE INDEX ix_refresh_tokens_family ON refresh_tokens (family_id);

-- Single-use password reset tokens (issued after OTP verification; 15-min expiry).
CREATE TABLE password_reset_tokens (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_type      VARCHAR(50) NOT NULL,   -- school_owner | staff | parent | platform_admin
    actor_id        UUID NOT NULL,
    token_hash      TEXT NOT NULL,
    expires_at      TIMESTAMPTZ NOT NULL,
    used_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_password_reset_tokens_actor ON password_reset_tokens (actor_type, actor_id);
