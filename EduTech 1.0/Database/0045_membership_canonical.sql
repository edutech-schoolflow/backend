-- EDD-007 Sprint B — Membership canonicalization (adult edge).
--
-- `memberships` (0037) is the canonical IDENTITY-keyed belonging edge. This widens its `kind` to the
-- full adult set and backfills `staff` and `owner` memberships from the legacy silos, so the adult
-- side of "identity belongs to organization" is complete and login can derive from memberships.
--
-- The STUDENT edge is deliberately untouched: a student belongs as a Child Profile, not an Identity
-- (EDD-007 §4), and that edge already exists as `students` (child_profile_id + school_id + status).
--
-- Additive + idempotent; nothing renamed, nothing deleted; the legacy silos keep working.

-- ── 1. Widen the kind check to the canonical adult membership set ─────────────────────────────
-- Existing rows (parent|pta|governor|volunteer) are a subset of the new set, so revalidation passes.
ALTER TABLE memberships DROP CONSTRAINT IF EXISTS memberships_kind_chk;
ALTER TABLE memberships ADD CONSTRAINT memberships_kind_chk
    CHECK (kind IN ('parent', 'staff', 'owner', 'vendor', 'governor', 'pta', 'volunteer', 'alumni'));

-- ── 2. Backfill STAFF memberships from active, identity-linked affiliations ───────────────────
-- One membership per (identity, school); UNIQUE(identity_id, school_id, kind) makes this idempotent.
INSERT INTO memberships (identity_id, school_id, kind, joined_at)
SELECT a.identity_id, a.school_id, 'staff', MIN(COALESCE(a.joined_at, a.created_at))
FROM staff_affiliations a
WHERE a.identity_id IS NOT NULL AND a.status = 'active'
GROUP BY a.identity_id, a.school_id
ON CONFLICT (identity_id, school_id, kind) DO NOTHING;

-- ── 3. Backfill OWNER memberships from active school owners ───────────────────────────────────
INSERT INTO memberships (identity_id, school_id, kind, joined_at)
SELECT o.identity_id, o.school_id, 'owner', MIN(o.created_at)
FROM school_owners o
WHERE o.identity_id IS NOT NULL AND o.is_active = TRUE
GROUP BY o.identity_id, o.school_id
ON CONFLICT (identity_id, school_id, kind) DO NOTHING;

-- Parent memberships were already backfilled in 0037. Vendor/governor/pta/volunteer/alumni have no
-- legacy silo to backfill from — they arrive as first-class memberships when those flows are built.
