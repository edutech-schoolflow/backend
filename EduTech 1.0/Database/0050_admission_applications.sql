-- EDD-014 Slice 3 — Applications (the new Admissions-owned application model).
--
-- An Application is a prospective learner applying to an AdmissionCycle. The applicant is conceptually
-- a Child Profile (EDD-007); until the platform publishes a Child-Profile-creation contract, the
-- applicant's details are carried inline here and `child_profile_id` is linked later (a surfaced
-- platform seam, not a foundation change). Draft → submitted → … → withdrawn.
--
-- This is a NEW table owned by EduTech.Admissions. The legacy `applications` (0023) + the
-- EduTech.Students/Admissions flow keep working untouched; convergence onto this model happens once
-- the new lifecycle reaches parity (later slices). Additive + idempotent; school_id = tenant scope.

CREATE TABLE IF NOT EXISTS admission_applications (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id          UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    cycle_id           UUID NOT NULL REFERENCES admission_cycles(id) ON DELETE RESTRICT,
    child_profile_id   UUID REFERENCES child_profiles(id),   -- the applicant (EDD-007); linked when the contract exists
    source_inquiry_id  UUID REFERENCES inquiries(id) ON DELETE SET NULL,  -- set when converted from an inquiry
    prospective_name   VARCHAR(160) NOT NULL,                -- inline applicant details (pre child-profile link)
    date_of_birth      DATE,
    gender             VARCHAR(10),
    guardian_name      VARCHAR(160),
    guardian_phone     VARCHAR(20) NOT NULL,
    preferred_class    VARCHAR(120),
    status             VARCHAR(20) NOT NULL DEFAULT 'draft',
    submitted_at       TIMESTAMPTZ,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    -- Full lifecycle reserved so later slices (decision/offer/enrollment) don't re-alter the constraint.
    CONSTRAINT admission_applications_status_chk CHECK (status IN
        ('draft', 'submitted', 'in_review', 'decided', 'offered', 'accepted', 'enrolled', 'withdrawn'))
);

CREATE INDEX IF NOT EXISTS ix_admission_applications_cycle  ON admission_applications(cycle_id, status);
CREATE INDEX IF NOT EXISTS ix_admission_applications_school ON admission_applications(school_id, status);
