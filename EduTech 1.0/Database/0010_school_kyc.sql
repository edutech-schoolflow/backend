-- School KYC submission (Phase 1). The owner submits proprietor identity + bank + documents;
-- kyc_status on schools moves not_submitted -> under_review -> approved/rejected (admin review).
-- School profile fields (name/type/address/...) already live on the schools table.

-- One submission record per school (1:1).
CREATE TABLE school_kyc (
    school_id            UUID PRIMARY KEY REFERENCES schools(id) ON DELETE CASCADE,
    proprietor_name      VARCHAR(255),
    proprietor_id_type   VARCHAR(40),    -- e.g. national_id, passport, drivers_licence
    proprietor_id_number VARCHAR(80),
    proprietor_phone     VARCHAR(20),
    proprietor_email     VARCHAR(255),
    bank_name            VARCHAR(120),
    account_number       VARCHAR(20),
    account_name         VARCHAR(255),
    account_type         VARCHAR(20),    -- current | savings
    submitted_at         TIMESTAMPTZ,
    reviewed_at          TIMESTAMPTZ,
    admin_notes          TEXT,           -- internal review notes
    school_message       TEXT,           -- reason/feedback shown to the owner (e.g. on rejection)
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Uploaded KYC documents (5 types), each independently reviewable.
CREATE TABLE school_kyc_documents (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id    UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    type         VARCHAR(40) NOT NULL,   -- registration_cert | operating_licence | proof_of_address
                                         -- | proprietor_id_front | proprietor_id_back
    url          TEXT NOT NULL,
    status       VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | approved | rejected
    notes        TEXT,
    uploaded_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (school_id, type)             -- one current file per document type (re-submit replaces)
);

CREATE INDEX ix_school_kyc_documents_school ON school_kyc_documents (school_id);
