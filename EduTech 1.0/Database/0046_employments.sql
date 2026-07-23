-- EDD-009 Sprint C2 — Employment: the canonical working-relationship edge.
--
-- Employment = an active or historical working relationship between a Membership and an Organization.
-- It references Membership (belonging) + Position (role) and owns NOTHING else (no payroll, leave,
-- attendance, performance, recruitment — those are Workforce-business). This is the "physical merge"
-- migration 0038 anticipated: staff_affiliations (staff) and school_owners (owner) converge here.
--
-- Additive + idempotent; the legacy silos are untouched and keep working (readers move off them in
-- a later sprint). No login/JWT/access_contexts changes.

-- ── The employments table (a Sacred Six member) ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS employments (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    membership_id          UUID NOT NULL REFERENCES memberships(id) ON DELETE CASCADE,
    organization_id        UUID NOT NULL REFERENCES schools(id)     ON DELETE CASCADE,  -- → organizations (Sprint D)
    position_id            UUID REFERENCES positions(id),
    organizational_unit_id UUID,                                    -- FK deferred: faculties/campuses/departments/houses
    manager_employment_id  UUID REFERENCES employments(id) ON DELETE SET NULL,          -- reporting line (acyclic; enforced later)
    employment_type        VARCHAR(20) NOT NULL DEFAULT 'full_time',
    status                 VARCHAR(20) NOT NULL DEFAULT 'active',
    started_at             TIMESTAMPTZ,
    ended_at               TIMESTAMPTZ,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    -- One row per (membership, position): allows two concurrent jobs (Teacher AND Dean) while
    -- preventing a duplicate identical job. organization_id is derivable from the membership.
    UNIQUE (membership_id, position_id),
    CONSTRAINT employments_status_chk CHECK (status IN ('draft', 'pending', 'active', 'suspended', 'ended')),
    CONSTRAINT employments_type_chk   CHECK (employment_type IN
        ('full_time', 'part_time', 'contract', 'temporary', 'volunteer', 'intern', 'consultant'))
);

CREATE INDEX IF NOT EXISTS ix_employments_membership   ON employments(membership_id);
CREATE INDEX IF NOT EXISTS ix_employments_organization ON employments(organization_id);
CREATE INDEX IF NOT EXISTS ix_employments_position     ON employments(position_id);

-- ── Backfill STAFF employments from active-or-historical affiliations ─────────────────────────
-- Join each affiliation to its 'staff' membership via (identity, school). One affiliation per
-- (staff, school) today ⇒ one employment per membership.
INSERT INTO employments (membership_id, organization_id, position_id, employment_type, status, started_at)
SELECT m.id, a.school_id, a.position_id, a.employment_type,
       CASE a.status
           WHEN 'active'  THEN 'active'
           WHEN 'invited' THEN 'pending'
           ELSE 'ended'                                    -- inactive (deactivated) / resigned: ended,
                                                           -- consistent with B1 (deactivate ends the
                                                           -- membership). 'suspended' is reserved for a
                                                           -- future explicit leave-of-absence flow.
       END,
       COALESCE(a.joined_at, a.created_at)
FROM staff_affiliations a
JOIN memberships m ON m.identity_id = a.identity_id AND m.school_id = a.school_id AND m.kind = 'staff'
WHERE a.identity_id IS NOT NULL
ON CONFLICT (membership_id, position_id) DO NOTHING;

-- ── Backfill OWNER employments ───────────────────────────────────────────────────────────────
INSERT INTO employments (membership_id, organization_id, position_id, employment_type, status, started_at)
SELECT m.id, o.school_id, o.position_id, 'full_time',
       CASE WHEN o.is_active THEN 'active' ELSE 'ended' END,
       o.created_at
FROM school_owners o
JOIN memberships m ON m.identity_id = o.identity_id AND m.school_id = o.school_id AND m.kind = 'owner'
WHERE o.identity_id IS NOT NULL
ON CONFLICT (membership_id, position_id) DO NOTHING;
