-- Per-school audit trail — an append-only record of significant actions (admissions decisions, student
-- lifecycle changes, …). Written by the AuditLogHandler observer whenever an IAuditableEvent is published,
-- so raising a domain event both notifies people AND leaves a durable trail, with no coupling in between.
CREATE TABLE IF NOT EXISTS audit_logs (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id     UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    actor_user_id UUID,                        -- who did it (staff/owner user id); null for system actions
    actor_type    VARCHAR(20),                 -- staff | school | system
    action        VARCHAR(80) NOT NULL,        -- dotted verb, e.g. application.admitted, student.withdrawn
    entity_type   VARCHAR(40),                 -- application | student | fee_type | ...
    entity_id     UUID,
    summary       TEXT NOT NULL,               -- human-readable one-liner
    metadata      JSONB,                       -- optional before/after / extra context
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_audit_logs_school ON audit_logs (school_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_logs_entity ON audit_logs (entity_type, entity_id);
