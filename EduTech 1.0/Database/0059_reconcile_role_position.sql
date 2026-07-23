-- EDD-015 / B2d.1 Stage 2.5 — one-time reconciliation: make position_id agree with today's role.
--
-- 0037 backfilled staff_affiliations.position_id from the role slug, but UpdateRoleAsync since then changed
-- `role` without touching `position_id`, so the two could have drifted — and the canonical role
-- (Employment → Position.slug) would be stale for anyone whose role changed after 0037. This converges
-- everything to the current role, so the canonical read model is correct BEFORE Stage 3 reads it. Same
-- philosophy as the B2a projector rebuild: no behavioral change, just convergence. Idempotent.

-- 1) Re-resolve every staff affiliation's position_id from its current role (school-specific, else global).
UPDATE staff_affiliations a
   SET position_id = COALESCE(
           (SELECT id FROM positions WHERE slug = a.role AND school_id = a.school_id),
           (SELECT id FROM positions WHERE slug = a.role AND school_id IS NULL),
           a.position_id)
 WHERE a.identity_id IS NOT NULL;

-- 2) Repoint every active staff employment to its affiliation's (now-current) position.
UPDATE employments e
   SET position_id = a.position_id, updated_at = NOW()
  FROM staff_affiliations a
  JOIN memberships m ON m.identity_id = a.identity_id AND m.school_id = a.school_id AND m.kind = 'staff'
 WHERE e.membership_id = m.id
   AND e.status = 'active'
   AND e.position_id IS DISTINCT FROM a.position_id;
