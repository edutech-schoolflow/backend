-- EDD-014 Slice 4 — Application Documents (the checklist).
--
-- Each required/optional document on an application has its own lifecycle so admissions officers
-- always know why an application can't progress: pending → uploaded → (verified | rejected); a
-- rejected document can be re-uploaded. The file itself lives in platform Storage (file_url).
-- Owned by EduTech.Admissions (child of Application). Additive + idempotent; school_id = tenant scope.

CREATE TABLE IF NOT EXISTS application_documents (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id UUID NOT NULL REFERENCES admission_applications(id) ON DELETE CASCADE,
    school_id      UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    doc_type       VARCHAR(60) NOT NULL,   -- birth_certificate | passport_photo | report_card | transfer_letter | …
    required       BOOLEAN NOT NULL DEFAULT TRUE,
    status         VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | uploaded | verified | rejected
    file_url       TEXT,                   -- platform Storage URL once uploaded
    notes          TEXT,                   -- e.g. the rejection reason
    verified_by    UUID,                   -- identity that verified (audit trail also records it)
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT application_documents_status_chk CHECK (status IN ('pending', 'uploaded', 'verified', 'rejected')),
    CONSTRAINT application_documents_type_uq UNIQUE (application_id, doc_type)
);

CREATE INDEX IF NOT EXISTS ix_application_documents_application ON application_documents(application_id);
