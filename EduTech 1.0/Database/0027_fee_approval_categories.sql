-- Fee categorization (compulsory | optional) + an owner-approval gate. A fee set up by a STAFF member
-- stays pending_approval until the school owner approves it; only approved fees are shown to parents.
-- Fees created by the owner account are approved on creation (set by the app, not this default).

ALTER TABLE fee_types ADD COLUMN category             VARCHAR(20)  NOT NULL DEFAULT 'compulsory';  -- compulsory | optional
ALTER TABLE fee_types ADD COLUMN approval_status      VARCHAR(20)  NOT NULL DEFAULT 'pending_approval';
ALTER TABLE fee_types ADD COLUMN submitted_by_is_owner BOOLEAN     NOT NULL DEFAULT FALSE;
ALTER TABLE fee_types ADD COLUMN approved_by_user_id  UUID;
ALTER TABLE fee_types ADD COLUMN approved_at          TIMESTAMPTZ;
ALTER TABLE fee_types ADD COLUMN rejection_reason     TEXT;

ALTER TABLE fee_types ADD CONSTRAINT chk_fee_types_category
    CHECK (category IN ('compulsory', 'optional'));
ALTER TABLE fee_types ADD CONSTRAINT chk_fee_types_approval
    CHECK (approval_status IN ('pending_approval', 'approved', 'rejected'));

-- Existing fee types predate the gate — treat them as approved so they keep working.
UPDATE fee_types SET approval_status = 'approved', submitted_by_is_owner = TRUE, approved_at = NOW();

CREATE INDEX ix_fee_types_approval ON fee_types (school_id, approval_status);
