-- EDD-016 Sprint 4 — Audit Canonicalization (the last clean convergence). The audit trail attributes
-- each action to a canonical identity instead of the legacy actor user_id. A bare UUID with no
-- legacy-table FK, so this is a clean repoint. Pre-launch V1 has no data (no backfill).
-- Out of scope: platform_admin_audit_log (0008) — a distinct platform-admin actor model.

ALTER TABLE audit_logs DROP COLUMN IF EXISTS actor_user_id;
ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS actor_identity_id UUID REFERENCES identities(id);  -- NULL = system action
