-- EDD-014 Slice 7 — Offers.
--
-- An Offer is a first-class object (not a status flag): campus, class, academic year, fee plan,
-- scholarship, acceptance deadline, conditions. It is issued from an approved/conditional Decision and
-- is accepted, declined, withdrawn, or lapses at its deadline. At most ONE non-terminal (issued) offer
-- may exist per application. class_id is a SOFT reference to a Students-owned class — no cross-module
-- FK. Owned by EduTech.Admissions (child of Application). Additive + idempotent; school_id = tenant scope.

CREATE TABLE IF NOT EXISTS offers (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id      UUID NOT NULL REFERENCES admission_applications(id) ON DELETE CASCADE,
    decision_id         UUID REFERENCES decisions(id) ON DELETE SET NULL,
    school_id           UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    campus              VARCHAR(120),
    class_id            UUID,                    -- soft reference to a Students class (resolved via contract)
    academic_year       VARCHAR(40),
    fee_plan            VARCHAR(120),
    scholarship         VARCHAR(160),
    conditions          TEXT,
    acceptance_deadline TIMESTAMPTZ,
    status              VARCHAR(20) NOT NULL DEFAULT 'issued',  -- issued | accepted | declined | lapsed | withdrawn
    responded_at        TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT offers_status_chk CHECK (status IN ('issued', 'accepted', 'declined', 'lapsed', 'withdrawn'))
);

-- One outstanding offer per application (the EDD-014 invariant).
CREATE UNIQUE INDEX IF NOT EXISTS ux_offers_one_active ON offers(application_id) WHERE status = 'issued';
CREATE INDEX IF NOT EXISTS ix_offers_application ON offers(application_id);
