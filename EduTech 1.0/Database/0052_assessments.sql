-- EDD-014 Slice 5 — Assessments.
--
-- Schools admit differently, so an Assessment is typed (exam | interview | observation | portfolio |
-- external_result) — which also lets universities and training centres fit. Lifecycle: scheduled →
-- (completed with a recorded result | cancelled). The 1:1 result is folded onto the assessment row.
-- Owned by EduTech.Admissions (child of Application). Additive + idempotent; school_id = tenant scope.

CREATE TABLE IF NOT EXISTS assessments (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id UUID NOT NULL REFERENCES admission_applications(id) ON DELETE CASCADE,
    school_id      UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    type           VARCHAR(30) NOT NULL,   -- exam | interview | observation | portfolio | external_result
    scheduled_at   TIMESTAMPTZ,
    status         VARCHAR(20) NOT NULL DEFAULT 'scheduled',  -- scheduled | completed | cancelled
    outcome        VARCHAR(40),            -- recorded on completion (pass | fail | waitlist | …)
    score          NUMERIC(6, 2),
    result_notes   TEXT,
    recorded_at    TIMESTAMPTZ,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT assessments_type_chk CHECK (type IN
        ('exam', 'interview', 'observation', 'portfolio', 'external_result')),
    CONSTRAINT assessments_status_chk CHECK (status IN ('scheduled', 'completed', 'cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_assessments_application ON assessments(application_id);
