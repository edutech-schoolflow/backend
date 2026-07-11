-- Parent compliance (NIN). Mirrors staff_users: encrypted NIN + platform KYC status.
-- (Staff already has these columns from 0003.)
ALTER TABLE parents ADD COLUMN nin TEXT;   -- encrypted at rest; never logged/returned
ALTER TABLE parents ADD COLUMN kyc_status VARCHAR(50) NOT NULL DEFAULT 'not_submitted';
