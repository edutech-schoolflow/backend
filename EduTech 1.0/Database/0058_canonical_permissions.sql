-- EDD-015 / B2d.1 Stage 1 — Canonical permission model (additive; no readers change yet).
--
-- Authorization state still lives on the legacy actor row (staff_affiliations.permission_template_id +
-- staff_feature_overrides). This moves it onto the canonical aggregates so CapabilityResolver + the token
-- mint can later stop reading staff_affiliations (B2d.1 Stage 3):
--   • Position owns the ROLE DEFAULT template (positions.permission_template_id).
--   • Employment owns the PER-ASSIGNMENT template override (employments.permission_template_id, nullable);
--     resolution is Employment.template ?? Position.template.
--   • Employment owns per-assignment overrides (employment_feature_overrides), an explicit table (not a
--     blob) so EmploymentPermissionGranted/Revoked events map to rows. Flag-native (feature_key) to mirror
--     permission_templates + staff_feature_overrides exactly — the flag→capability rename is EDD-006, later.
--
-- Backfill is 1:1 from the legacy rows via staff_affiliations.identity_id (0037) → memberships → employments.
-- positions.permission_template_id stays NULL (defaults are set by admins later); behavior is preserved
-- because each employment inherits its affiliation's template directly.

ALTER TABLE positions   ADD COLUMN IF NOT EXISTS permission_template_id UUID REFERENCES permission_templates(id);
ALTER TABLE employments ADD COLUMN IF NOT EXISTS permission_template_id UUID REFERENCES permission_templates(id);

CREATE TABLE IF NOT EXISTS employment_feature_overrides (
    employment_id  UUID NOT NULL REFERENCES employments(id) ON DELETE CASCADE,
    feature_key    VARCHAR(60) NOT NULL,
    enabled        BOOLEAN NOT NULL,
    PRIMARY KEY (employment_id, feature_key)
);

-- Backfill: each staff employment inherits its legacy affiliation's template (per identity + school).
UPDATE employments e
   SET permission_template_id = a.permission_template_id
  FROM memberships m
  JOIN staff_affiliations a ON a.identity_id = m.identity_id AND a.school_id = m.school_id
 WHERE e.membership_id = m.id
   AND m.kind = 'staff'
   AND a.permission_template_id IS NOT NULL
   AND e.permission_template_id IS NULL;

-- Backfill: legacy per-affiliation overrides → per-employment overrides.
INSERT INTO employment_feature_overrides (employment_id, feature_key, enabled)
SELECT e.id, o.feature_key, o.enabled
  FROM staff_feature_overrides o
  JOIN staff_affiliations a ON a.id = o.affiliation_id
  JOIN memberships m  ON m.identity_id = a.identity_id AND m.school_id = a.school_id AND m.kind = 'staff'
  JOIN employments e  ON e.membership_id = m.id
ON CONFLICT (employment_id, feature_key) DO NOTHING;
