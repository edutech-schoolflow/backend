-- Immutable, append-only audit trail of every Platform Admin action (spec §4.5).
CREATE TABLE platform_admin_audit_log (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    admin_id        UUID NOT NULL REFERENCES platform_admins(id),
    action          VARCHAR(80) NOT NULL,   -- e.g. kyc.approve, kyc.reject, school.suspend
    target_type     VARCHAR(40),            -- school | staff | parent | payment | admin
    target_id       UUID,
    metadata        JSONB,                  -- reason, before/after, amounts
    ip_address      VARCHAR(64),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_admin_audit_admin  ON platform_admin_audit_log (admin_id);
CREATE INDEX ix_admin_audit_target ON platform_admin_audit_log (target_type, target_id);
