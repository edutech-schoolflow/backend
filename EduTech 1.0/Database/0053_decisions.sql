-- EDD-014 Slice 6 — Decisions.
--
-- An admissions Decision is richer than admit/reject: approved | conditional | waitlisted | rejected
-- | withdrawn. It is append-only — the history of decisions on an application; the latest row is the
-- current outcome. Recording a decision moves the application to 'decided'. Owned by EduTech.Admissions
-- (child of Application). Additive + idempotent; school_id = tenant scope.

CREATE TABLE IF NOT EXISTS decisions (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id UUID NOT NULL REFERENCES admission_applications(id) ON DELETE CASCADE,
    school_id      UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    outcome        VARCHAR(20) NOT NULL,   -- approved | conditional | waitlisted | rejected | withdrawn
    conditions     TEXT,                   -- required when outcome = conditional
    notes          TEXT,
    decided_by     UUID,                   -- identity that decided (audit trail also records it)
    decided_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT decisions_outcome_chk CHECK (outcome IN
        ('approved', 'conditional', 'waitlisted', 'rejected', 'withdrawn'))
);

CREATE INDEX IF NOT EXISTS ix_decisions_application ON decisions(application_id, decided_at DESC);
