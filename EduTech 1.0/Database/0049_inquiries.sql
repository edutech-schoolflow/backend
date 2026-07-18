-- EDD-014 Slice 2 — Inquiries.
--
-- An Inquiry is pre-application interest ("I'd like to know more"): a prospective family the school
-- can contact, optionally book a visit for, and eventually convert into an Application. It is
-- deliberately pre-identity — an inquirer has no account yet, so we store raw contact details.
-- Owned by EduTech.Admissions; references only the platform + the module's own admission_cycles.
-- Additive + idempotent; school_id is the operational organization scope (TenantRepository).

CREATE TABLE IF NOT EXISTS inquiries (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id                UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    cycle_id                 UUID REFERENCES admission_cycles(id) ON DELETE SET NULL,
    prospective_name         VARCHAR(160) NOT NULL,          -- the child the family is inquiring about
    guardian_name            VARCHAR(160),
    guardian_phone           VARCHAR(20) NOT NULL,
    notes                    TEXT,
    visit_at                 TIMESTAMPTZ,                    -- set when a visit is booked
    status                   VARCHAR(20) NOT NULL DEFAULT 'new',  -- new | contacted | visit_booked | converted | closed
    converted_application_id UUID,                           -- set on convert (FK wired in Slice 3)
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT inquiries_status_chk CHECK (status IN ('new', 'contacted', 'visit_booked', 'converted', 'closed'))
);

CREATE INDEX IF NOT EXISTS ix_inquiries_school ON inquiries(school_id, status);
