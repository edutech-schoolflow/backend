-- Child documents (FE enrol flow): the photo and birth certificate are REQUIRED at profile
-- creation (enforced in the service — existing rows predate the rule); the medical document is
-- optional. Files live in object storage; these hold their URLs.
ALTER TABLE child_profiles
    ADD COLUMN IF NOT EXISTS birth_cert_url  TEXT,
    ADD COLUMN IF NOT EXISTS medical_doc_url TEXT;
