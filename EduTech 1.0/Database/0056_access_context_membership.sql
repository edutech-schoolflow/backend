-- EDD-012 Sprint B2c.1 — Canonical Identity Token.
--
-- access_contexts.reference_id still points at the LEGACY actor (owner_id | affiliation_id | parent_id) —
-- the login-compat pointer. This adds the canonical identity to the projection: membership_id, the
-- Membership (EDD-007) each context belongs to. The projector (AccessContextProjector) writes it going
-- forward; this backfills existing rows from the canonical membership. B2c.3 flips the token / login onto
-- membership_id and drops reference_id. Additive + idempotent; access_contexts is a disposable projection.

ALTER TABLE access_contexts ADD COLUMN IF NOT EXISTS membership_id UUID REFERENCES memberships(id);

-- Backfill from the canonical membership: same (identity, organization, kind) match the projector uses.
-- ac.type ∈ {owner, staff, parent} maps 1:1 to memberships.kind; ac.organization_id = memberships.school_id.
UPDATE access_contexts ac
   SET membership_id = m.id
  FROM memberships m
 WHERE ac.membership_id IS NULL
   AND m.identity_id = ac.identity_id
   AND m.school_id   = ac.organization_id
   AND m.kind        = ac.type
   AND m.status      = 'active';

CREATE INDEX IF NOT EXISTS ix_access_contexts_membership ON access_contexts(membership_id);
