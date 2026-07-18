-- EDD-014 Slice 1 — Admission Cycles.
--
-- An AdmissionCycle is a named intake an organization admits into. It is org-type-neutral
-- (session / undergraduate intake / bootcamp / cohort), so it fits schools, universities, training
-- centres, and tutors alike. Applications will belong to a cycle (later slices). Owned by the new
-- EduTech.Admissions module; references only the platform (schools/organizations) — no other module.
--
-- school_id is the operational organization scope (TenantRepository convention); it re-points to
-- `organizations` with the rest of the platform in the FK-repointing sprint. Additive + idempotent.

CREATE TABLE IF NOT EXISTS admission_cycles (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id    UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    name         VARCHAR(160) NOT NULL,
    intake_type  VARCHAR(40),                            -- session | intake | bootcamp | cohort | …
    opens_at     TIMESTAMPTZ,
    closes_at    TIMESTAMPTZ,
    quota        INT,                                    -- optional cap on offers/enrollments
    status       VARCHAR(20) NOT NULL DEFAULT 'draft',   -- draft | open | closed | archived
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT admission_cycles_status_chk CHECK (status IN ('draft', 'open', 'closed', 'archived')),
    CONSTRAINT admission_cycles_quota_chk  CHECK (quota IS NULL OR quota >= 0)
);

CREATE INDEX IF NOT EXISTS ix_admission_cycles_school ON admission_cycles(school_id, status);
