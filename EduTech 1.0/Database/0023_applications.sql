-- Admission applications (Actor 3 §3.5): a parent applies a child_profile to a school. The school
-- reviews (optional entrance exam + assessment) then ADMITS (creates a thin students enrollment from
-- the child) or REJECTS. Child bio / extra guardians are read from child_profiles / guardian_contacts,
-- so they are NOT duplicated here. Fee is stubbed (non-blocking) until the Fees/Monnify module.

CREATE TABLE applications (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reference_number     VARCHAR(60) NOT NULL UNIQUE,            -- e.g. APP/2026/0001
    child_profile_id     UUID NOT NULL REFERENCES child_profiles(id) ON DELETE CASCADE,
    parent_id            UUID NOT NULL REFERENCES parents(id) ON DELETE CASCADE,
    school_id            UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    desired_class        VARCHAR(60),
    term_id              UUID REFERENCES terms(id) ON DELETE SET NULL,

    application_fee      NUMERIC(12,2) NOT NULL DEFAULT 0,
    application_fee_paid BOOLEAN NOT NULL DEFAULT FALSE,
    payment_reference    VARCHAR(120),

    status               VARCHAR(40) NOT NULL DEFAULT 'under_review',
                         -- under_review | exam_scheduled | admitted | rejected

    -- entrance exam (optional)
    exam_date            DATE,
    exam_time            VARCHAR(20),
    exam_venue           VARCHAR(255),
    exam_instructions    TEXT,

    -- assessment (optional)
    assessment_rating    VARCHAR(20),                           -- excellent | good | fair | poor
    assessment_notes     TEXT,

    -- outcome
    rejection_reason     TEXT,
    admitted_student_id  UUID REFERENCES students(id) ON DELETE SET NULL,
    admission_number     VARCHAR(60),

    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_applications_status
        CHECK (status IN ('under_review', 'exam_scheduled', 'admitted', 'rejected'))
);
CREATE INDEX ix_applications_school ON applications (school_id, status);
CREATE INDEX ix_applications_parent ON applications (parent_id);
CREATE INDEX ix_applications_child ON applications (child_profile_id);

-- a child may have only ONE open (non-terminal) application per school at a time
CREATE UNIQUE INDEX uq_one_open_application_per_school
    ON applications (child_profile_id, school_id)
    WHERE status IN ('under_review', 'exam_scheduled');
