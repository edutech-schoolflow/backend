-- EDD-002 revision — a parent relationship is ORGANIZATION-SCOPED, not school-agnostic.
-- A parent membership belongs to one school (like staff employment), so the identity has one parent
-- AccessContext per school (from `memberships`), not a single NULL-org row. That gives parent sessions
-- an org-scoped token (school_id claim) and routes them through the same /o/{slug} pipeline as staff,
-- and lets parent queries bind @SchoolId + @ParentId — the structural guard tenant data already gets.
--
-- reference_id stays the parent_id (strangler-safe: IssueParent(parentId) and the parent refresh path
-- keep working); organization_id joins the unique key so one parent can hold a context per school.
-- Flip reference_id -> membership_id later, once memberships are the sole source.

-- 1. The context key gains organization_id (a parent now has one row per school).
ALTER TABLE access_contexts DROP CONSTRAINT access_contexts_ref_key;
ALTER TABLE access_contexts ADD CONSTRAINT access_contexts_ref_key
    UNIQUE (type, reference_id, organization_id);

-- 2. Drop the legacy school-agnostic parent rows; they are rebuilt per-org below.
DELETE FROM access_contexts WHERE type = 'parent' AND organization_id IS NULL;

-- 3. One parent context per membership (memberships is already backfilled per-school in 0037).
INSERT INTO access_contexts (identity_id, type, reference_id, organization_id)
SELECT m.identity_id, 'parent', p.id, m.school_id
FROM memberships m
JOIN parents p ON p.identity_id = m.identity_id AND p.is_active = TRUE
WHERE m.kind = 'parent' AND m.status = 'active'
ON CONFLICT (type, reference_id, organization_id) DO NOTHING;
