-- EDD-003 AccessContext — the projection between Identity and the business contexts:
-- "this identity can currently operate in this context". Maintained by the relationship writers
-- (owner/staff ensure-links, parent link) and converged by the identity reconciliation sweep
-- (EDD-004 rule 5). Login reads THIS table; it no longer needs to know what a parent/staff/owner is.
-- type uses the serving names (owner|staff|parent); they normalize to employment|membership when
-- portal tokens give way to organization-context tokens.

CREATE TABLE IF NOT EXISTS access_contexts (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id     UUID NOT NULL REFERENCES identities(id) ON DELETE CASCADE,
    type            VARCHAR(20) NOT NULL,
    reference_id    UUID NOT NULL,                       -- owner_id | affiliation_id | parent_id
    organization_id UUID REFERENCES schools(id) ON DELETE CASCADE,  -- NULL: parent is school-agnostic (D-FE-1)
    status          VARCHAR(20) NOT NULL DEFAULT 'active',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT access_contexts_type_chk   CHECK (type IN ('owner', 'staff', 'parent')),
    CONSTRAINT access_contexts_status_chk CHECK (status IN ('active', 'ended')),
    CONSTRAINT access_contexts_ref_key    UNIQUE (type, reference_id)
);

CREATE INDEX IF NOT EXISTS ix_access_contexts_identity ON access_contexts(identity_id) WHERE status = 'active';

-- Backfill from the silos (idempotent).
INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
SELECT o.identity_id, 'owner', o.id, o.school_id
FROM school_owners o WHERE o.identity_id IS NOT NULL AND o.is_active = TRUE
ON CONFLICT (type, reference_id) DO NOTHING;

INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
SELECT a.identity_id, 'staff', a.id, a.school_id
FROM staff_affiliations a WHERE a.identity_id IS NOT NULL AND a.status = 'active'
ON CONFLICT (type, reference_id) DO NOTHING;

INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
SELECT p.identity_id, 'parent', p.id, NULL
FROM parents p WHERE p.identity_id IS NOT NULL AND p.is_active = TRUE
ON CONFLICT (type, reference_id) DO NOTHING;
