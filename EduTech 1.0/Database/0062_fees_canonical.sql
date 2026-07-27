-- EDD-016 Sprint 3 — Fees Canonicalization. The owner who approved a fee type is recorded canonically
-- (identity) instead of the legacy actor user_id. Write-only column (never read), so this is a clean cut.
-- Pre-launch V1 has no data, so this repoints the column directly (no backfill).
-- Note: payments.parent_id (the payer) is deliberately NOT here — it retires with the legacy `parents`
-- table in the Students/Family sprint, alongside parent_children/guardian modelling.

ALTER TABLE fee_types DROP COLUMN IF EXISTS approved_by_user_id;
ALTER TABLE fee_types ADD COLUMN IF NOT EXISTS approved_by_identity_id UUID REFERENCES identities(id);
