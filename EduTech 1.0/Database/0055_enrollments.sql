-- EDD-014 Slice 8 — Enrollments (the platform transition).
--
-- Enrollment confirms an accepted offer became a real place in the organization. It is deliberately
-- NOT the Student: a child can be admitted (offer accepted) yet never enroll. Enrolling emits
-- StudentEnrolled — the ONLY bridge to the Students module, which creates the Student from the event.
-- One enrollment per application. Owned by EduTech.Admissions. Additive + idempotent; school_id = tenant.

CREATE TABLE IF NOT EXISTS enrollments (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id   UUID NOT NULL UNIQUE REFERENCES admission_applications(id) ON DELETE CASCADE,
    offer_id         UUID REFERENCES offers(id) ON DELETE SET NULL,
    school_id        UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    child_profile_id UUID REFERENCES child_profiles(id),   -- linked when the Child-Profile contract exists
    status           VARCHAR(20) NOT NULL DEFAULT 'active',  -- active | cancelled
    cancelled_reason TEXT,
    enrolled_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT enrollments_status_chk CHECK (status IN ('active', 'cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_enrollments_school ON enrollments(school_id, status);
