-- EDD-018 Slice 1 — Canonical Workforce schema (ADDITIVE, no runtime readers yet). Establishes the two
-- remaining canonical columns so Slice 2 can run the invitation/lifecycle workflow on Employment. Legacy
-- staff_affiliations / staff_invite_tokens.affiliation_id stay until Slice 5 (additive-first, no dual-write).

-- The invite token points at the Employment being accepted (was: affiliation_id → staff_affiliations).
ALTER TABLE staff_invite_tokens ADD COLUMN IF NOT EXISTS employment_id UUID REFERENCES employments(id) ON DELETE CASCADE;

-- Invite attribution becomes a canonical Identity, on the Employment.
ALTER TABLE employments ADD COLUMN IF NOT EXISTS invited_by_identity_id UUID REFERENCES identities(id);

-- ── Backfill (V1 has no data, so a release no-op; correct for any dev data) ───────────────────────────
-- affiliation → employment: staff_affiliations (identity_id, school_id, position_id) → membership → employment.
UPDATE staff_invite_tokens t SET employment_id = e.id
FROM staff_affiliations a
JOIN memberships m ON m.identity_id = a.identity_id AND m.school_id = a.school_id
JOIN employments e ON e.membership_id = m.id AND e.position_id IS NOT DISTINCT FROM a.position_id
WHERE t.affiliation_id = a.id AND t.employment_id IS NULL;

-- invited_by (a legacy actor id) → Identity, best-effort via staff_users (owner-invites may stay NULL —
-- attribution only; the actor tables retire in Slice 5 regardless).
UPDATE employments e SET invited_by_identity_id = su.identity_id
FROM staff_affiliations a
JOIN memberships m ON m.identity_id = a.identity_id AND m.school_id = a.school_id
JOIN staff_users su ON su.id = a.invited_by
WHERE e.membership_id = m.id AND e.position_id IS NOT DISTINCT FROM a.position_id
  AND e.invited_by_identity_id IS NULL;
