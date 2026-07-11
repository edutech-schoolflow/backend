-- Proprietor identity numbers for school KYC: NIN + BVN (required for proprietor + payout-account
-- verification). Stored ENCRYPTED at rest (AES-GCM); never logged or returned to clients.
ALTER TABLE school_kyc ADD COLUMN proprietor_nin TEXT;   -- encrypted
ALTER TABLE school_kyc ADD COLUMN proprietor_bvn TEXT;   -- encrypted
