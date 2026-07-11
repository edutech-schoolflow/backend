-- EDD-001 Sprints 3–4 (additive) — Positions, Memberships, and identity links on employments.
-- 1. positions: the Position aggregate (EDD-003) — platform-seeded jobs (+ room for school-defined).
-- 2. memberships: the Membership aggregate — non-employment belonging (parent, PTA, governor…).
--    Backfilled for parents from their children's enrollments.
-- 3. staff_affiliations (future employments) gains identity_id + position_id, backfilled.
-- Idempotent throughout; legacy flows keep working (columns are additive, nothing renamed yet).

-- ── 1. Positions ────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS positions (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id  UUID REFERENCES schools(id) ON DELETE CASCADE,   -- NULL = platform-seeded (global)
    slug       VARCHAR(50)  NOT NULL,
    name       VARCHAR(120) NOT NULL,
    is_academic BOOLEAN NOT NULL DEFAULT FALSE,                 -- teaching positions (roster/teacher pickers)
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- One slug per scope (global vs per-school).
CREATE UNIQUE INDEX IF NOT EXISTS ux_positions_scope_slug
    ON positions (COALESCE(school_id, '00000000-0000-0000-0000-000000000000'::uuid), slug);

INSERT INTO positions (school_id, slug, name, is_academic) VALUES
    (NULL, 'owner',            'Owner',             FALSE),
    (NULL, 'school_admin',     'School Administrator', FALSE),
    (NULL, 'principal',        'Principal',         TRUE),
    (NULL, 'vice_principal',   'Vice Principal',    TRUE),
    (NULL, 'head_teacher',     'Head Teacher',      TRUE),
    (NULL, 'teacher',          'Teacher',           TRUE),
    (NULL, 'registrar',        'Registrar',         FALSE),
    (NULL, 'bursar',           'Bursar',            FALSE),
    (NULL, 'accountant',       'Accountant',        FALSE),
    (NULL, 'secretary',        'Secretary',         FALSE),
    (NULL, 'receptionist',     'Receptionist',      FALSE),
    (NULL, 'nurse',            'Nurse',             FALSE),
    (NULL, 'librarian',        'Librarian',         FALSE),
    (NULL, 'ict_officer',      'ICT Officer',       FALSE),
    (NULL, 'lab_technician',   'Lab Technician',    FALSE),
    (NULL, 'driver',           'Driver',            FALSE),
    (NULL, 'security_officer', 'Security Officer',  FALSE),
    (NULL, 'cleaner',          'Cleaner',           FALSE)
ON CONFLICT DO NOTHING;

-- ── 2. Memberships ──────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS memberships (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id UUID NOT NULL REFERENCES identities(id) ON DELETE CASCADE,
    school_id   UUID NOT NULL REFERENCES schools(id)    ON DELETE CASCADE,
    kind        VARCHAR(30) NOT NULL DEFAULT 'parent',   -- parent | pta | governor | volunteer
    status      VARCHAR(20) NOT NULL DEFAULT 'active',   -- active | ended
    joined_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ended_at    TIMESTAMPTZ,
    UNIQUE (identity_id, school_id, kind),
    CONSTRAINT memberships_kind_chk   CHECK (kind IN ('parent', 'pta', 'governor', 'volunteer')),
    CONSTRAINT memberships_status_chk CHECK (status IN ('active', 'ended'))
);

CREATE INDEX IF NOT EXISTS ix_memberships_identity ON memberships(identity_id);
CREATE INDEX IF NOT EXISTS ix_memberships_school   ON memberships(school_id);

-- Backfill parent memberships: a parent belongs to every school where a linked child is enrolled.
INSERT INTO memberships (identity_id, school_id, kind, joined_at)
SELECT DISTINCT p.identity_id, st.school_id, 'parent', MIN(st.created_at)
FROM parents p
JOIN parent_children pc ON pc.parent_id = p.id
JOIN students st        ON st.child_profile_id = pc.child_profile_id
WHERE p.identity_id IS NOT NULL
GROUP BY p.identity_id, st.school_id
ON CONFLICT (identity_id, school_id, kind) DO NOTHING;

-- ── 3. Employments (staff_affiliations) → identity + position links ─────────────────────────

ALTER TABLE staff_affiliations ADD COLUMN IF NOT EXISTS identity_id UUID REFERENCES identities(id);
ALTER TABLE staff_affiliations ADD COLUMN IF NOT EXISTS position_id UUID REFERENCES positions(id);

CREATE INDEX IF NOT EXISTS ix_staff_affiliations_identity ON staff_affiliations(identity_id);

UPDATE staff_affiliations a SET identity_id = su.identity_id
FROM staff_users su
WHERE a.staff_user_id = su.id AND a.identity_id IS NULL AND su.identity_id IS NOT NULL;

-- Role slugs map 1:1 onto the seeded global positions.
UPDATE staff_affiliations a SET position_id = pos.id
FROM positions pos
WHERE a.position_id IS NULL AND pos.school_id IS NULL AND pos.slug = a.role;
