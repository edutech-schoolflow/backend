-- Trim school KYC to the lean set. The proprietor is verified digitally via NIN + BVN (same engine
-- as staff/parent), so the photographed-ID fields/documents are redundant; CAC is the only document
-- a number lookup can't replace. School location/GPS stays on the schools table (future geo-search).

ALTER TABLE school_kyc DROP COLUMN IF EXISTS proprietor_id_type;
ALTER TABLE school_kyc DROP COLUMN IF EXISTS proprietor_id_number;
ALTER TABLE school_kyc DROP COLUMN IF EXISTS proprietor_phone;
ALTER TABLE school_kyc DROP COLUMN IF EXISTS proprietor_email;
ALTER TABLE school_kyc DROP COLUMN IF EXISTS account_type;

-- Keep only the business-registration (CAC) document; drop the redundant identity/address docs.
DELETE FROM school_kyc_documents WHERE type <> 'registration_cert';
