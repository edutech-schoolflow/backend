-- EDD-010 Sprint D — Organization: the platform root.
--
-- Establishes the Organization aggregate as the account/company that owns everything (Slack's
-- Workspace, not its channels). It stays as boring as Identity: institutional identity only —
-- id, name, slug, type, status, owner. Schools link UP to their organization via
-- schools.organization_id.
--
-- Strangler discipline: this is a SHADOW ROOT. It is created and backfilled, but nothing re-points
-- to it yet — memberships/employments/positions/access_contexts keep referencing `schools`, and the
-- existing school-as-org auth/onboarding layer is untouched. Re-pointing FKs is a later, isolated
-- sprint. Additive + idempotent; nothing renamed or deleted.

-- ── The organizations table (a Sacred Six member) ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS organizations (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(255),
    slug                VARCHAR(80) UNIQUE,
    type                VARCHAR(30) NOT NULL DEFAULT 'school',   -- education platform, not just schools
    status              VARCHAR(20) NOT NULL DEFAULT 'active',
    owner_membership_id UUID REFERENCES memberships(id),         -- the owner AS a membership (EDD-007)
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT organizations_type_chk CHECK (type IN
        ('school', 'university', 'training_centre', 'tutor', 'corporate', 'ngo')),
    CONSTRAINT organizations_status_chk CHECK (status IN ('active', 'suspended', 'archived'))
);

-- ── Link each school UP to its organization (nullable during the transition) ────────────────────
ALTER TABLE schools ADD COLUMN IF NOT EXISTS organization_id UUID REFERENCES organizations(id);
CREATE INDEX IF NOT EXISTS ix_schools_organization ON schools(organization_id);

-- ── Backfill: one organization per existing school (1:1), keyed on the school's unique slug (0040) ─
-- 1. An organization per school that doesn't already have a matching org.
INSERT INTO organizations (name, slug, type, status, created_at)
SELECT s.name, s.slug, 'school',
       CASE WHEN s.status = 'suspended' THEN 'suspended' ELSE 'active' END,
       s.created_at
FROM schools s
WHERE s.slug IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM organizations o WHERE o.slug = s.slug);

-- 2. Link the school up to its organization.
UPDATE schools s SET organization_id = o.id
FROM organizations o
WHERE s.organization_id IS NULL AND s.slug IS NOT NULL AND o.slug = s.slug;

-- 3. Backfill owner_membership_id from the school's active owner → its 'owner' membership (EDD-007).
UPDATE organizations o SET owner_membership_id = m.id
FROM schools s
JOIN school_owners so ON so.school_id = s.id AND so.is_active = TRUE AND so.identity_id IS NOT NULL
JOIN memberships m ON m.identity_id = so.identity_id AND m.school_id = s.id AND m.kind = 'owner'
WHERE o.id = s.organization_id AND o.owner_membership_id IS NULL;
