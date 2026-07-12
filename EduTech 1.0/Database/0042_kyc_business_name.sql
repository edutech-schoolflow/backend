-- KYC now persists the CAC-registered business name (previously collected in the form but dropped on
-- submit). School GPS (location_lat/location_lng) already exists on the schools table — it just starts
-- being written now, so no column change is needed there.
ALTER TABLE school_kyc ADD COLUMN IF NOT EXISTS business_name VARCHAR(255);
