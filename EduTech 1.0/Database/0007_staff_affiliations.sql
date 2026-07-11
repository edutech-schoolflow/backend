-- Staff module: per-school affiliations, permission templates/overrides, and invite tokens.
-- A staff_user (global identity, 0003) is linked to a school via one staff_affiliations row.

-- School-scoped permission templates (the "template permission" a school admin configures).
CREATE TABLE permission_templates (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id       UUID NOT NULL REFERENCES schools(id),
    name            VARCHAR(120) NOT NULL,
    description     TEXT,
    features        JSONB NOT NULL,                 -- full StaffFeatures map (13 booleans)
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (school_id, name)
);

-- One row per school a staff member is affiliated with.
CREATE TABLE staff_affiliations (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    staff_user_id           UUID NOT NULL REFERENCES staff_users(id),
    school_id               UUID NOT NULL REFERENCES schools(id),
    role                    VARCHAR(50) NOT NULL,
    position                VARCHAR(120),
    employment_type         VARCHAR(20) NOT NULL,   -- full_time | part_time (set by the school)
    permission_template_id  UUID REFERENCES permission_templates(id),  -- optional base
    status                  VARCHAR(50) NOT NULL DEFAULT 'invited',     -- invited|active|inactive|resigned
    invited_by              UUID,                   -- owner or staff with can_manage_permissions
    joined_at               TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (staff_user_id, school_id)
);

-- Full-time exclusivity backstop: at most ONE active full_time affiliation per person.
-- (The remaining rule — no other affiliations alongside a full_time one — is enforced in code.)
CREATE UNIQUE INDEX uq_one_active_full_time
    ON staff_affiliations (staff_user_id)
    WHERE employment_type = 'full_time' AND status = 'active';

CREATE INDEX ix_staff_affiliations_staff  ON staff_affiliations (staff_user_id);
CREATE INDEX ix_staff_affiliations_school ON staff_affiliations (school_id);

-- Per-affiliation individual feature overrides (on top of template/role defaults).
CREATE TABLE staff_feature_overrides (
    affiliation_id  UUID NOT NULL REFERENCES staff_affiliations(id),
    feature_key     VARCHAR(60) NOT NULL,           -- e.g. 'can_enter_grades'
    enabled         BOOLEAN NOT NULL,
    PRIMARY KEY (affiliation_id, feature_key)
);

-- Invite tokens — tie an SMS invite link to a specific pending affiliation, keyed by phone.
CREATE TABLE staff_invite_tokens (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    affiliation_id  UUID NOT NULL REFERENCES staff_affiliations(id),
    phone           VARCHAR(20) NOT NULL,           -- recipient (must match on accept)
    token_hash      TEXT NOT NULL,                  -- SHA-256 of the raw token in the link
    expires_at      TIMESTAMPTZ NOT NULL,           -- NOW() + 72 hours
    used_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX ux_staff_invite_tokens_hash       ON staff_invite_tokens (token_hash);
CREATE INDEX        ix_staff_invite_tokens_affiliation ON staff_invite_tokens (affiliation_id);
