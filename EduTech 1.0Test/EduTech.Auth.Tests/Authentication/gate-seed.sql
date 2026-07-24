-- EDD-015 / B2d.1 Validation Gate seed — a consistent, projector-valid dataset for the
-- legacy-vs-canonical capability equivalence sweep (CapabilityEquivalenceGateTests).
--
-- Every active staff access_context has BOTH the legacy rows (staff_affiliations + staff_feature_overrides,
-- keyed by reference_id) AND the canonical rows (employment + position + employment_feature_overrides, keyed
-- by membership) set to the SAME role/template/overrides — exactly as the Stage 1 backfill / Stage 2 sync
-- produce. Scenarios: (A) role-only, (B) employment template, (C) template + feature override, (D) owner=all,
-- (E) parent=∅. (The projector invariant — active staff context ⟹ active employment — is respected.)

-- ── shared ────────────────────────────────────────────────────────────────────
INSERT INTO schools (id, name) VALUES ('a0000000-0000-0000-0000-000000000001', 'Gate School');

INSERT INTO permission_templates (id, school_id, name, features) VALUES
  ('b0000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'Std',
   '{"can_enter_grades": true, "can_manage_fees": false}');

-- positions: reference the platform-seeded GLOBAL positions (teacher/principal) by slug — don't insert.

-- identities / staff_users / memberships for A,B,C (staff), D (owner), E (parent)
INSERT INTO identities (id, first_name, last_name, phone) VALUES
  ('d0000000-0000-0000-0000-00000000000a', 'Aa', 'Aa', '+2348000000001'),
  ('d0000000-0000-0000-0000-00000000000b', 'Bb', 'Bb', '+2348000000002'),
  ('d0000000-0000-0000-0000-00000000000c', 'Cc', 'Cc', '+2348000000003'),
  ('d0000000-0000-0000-0000-00000000000d', 'Dd', 'Dd', '+2348000000004'),
  ('d0000000-0000-0000-0000-00000000000e', 'Ee', 'Ee', '+2348000000005');

INSERT INTO staff_users (id, identity_id, first_name, last_name, phone) VALUES
  ('e0000000-0000-0000-0000-00000000000a', 'd0000000-0000-0000-0000-00000000000a', 'Aa', 'Aa', '+2348000000001'),
  ('e0000000-0000-0000-0000-00000000000b', 'd0000000-0000-0000-0000-00000000000b', 'Bb', 'Bb', '+2348000000002'),
  ('e0000000-0000-0000-0000-00000000000c', 'd0000000-0000-0000-0000-00000000000c', 'Cc', 'Cc', '+2348000000003');

INSERT INTO memberships (id, identity_id, school_id, kind, status) VALUES
  ('f0000000-0000-0000-0000-00000000000a', 'd0000000-0000-0000-0000-00000000000a', 'a0000000-0000-0000-0000-000000000001', 'staff',  'active'),
  ('f0000000-0000-0000-0000-00000000000b', 'd0000000-0000-0000-0000-00000000000b', 'a0000000-0000-0000-0000-000000000001', 'staff',  'active'),
  ('f0000000-0000-0000-0000-00000000000c', 'd0000000-0000-0000-0000-00000000000c', 'a0000000-0000-0000-0000-000000000001', 'staff',  'active'),
  ('f0000000-0000-0000-0000-00000000000d', 'd0000000-0000-0000-0000-00000000000d', 'a0000000-0000-0000-0000-000000000001', 'owner',  'active'),
  ('f0000000-0000-0000-0000-00000000000e', 'd0000000-0000-0000-0000-00000000000e', 'a0000000-0000-0000-0000-000000000001', 'parent', 'active');

-- ── legacy rows (what the live CapabilityResolver reads, keyed by reference_id) ──
INSERT INTO staff_affiliations (id, staff_user_id, school_id, identity_id, role, employment_type, permission_template_id, position_id, status) VALUES
  ('11110000-0000-0000-0000-00000000000a', 'e0000000-0000-0000-0000-00000000000a', 'a0000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-00000000000a', 'teacher',   'full_time', NULL,                                     (SELECT id FROM positions WHERE slug = 'teacher' AND school_id IS NULL), 'active'),
  ('11110000-0000-0000-0000-00000000000b', 'e0000000-0000-0000-0000-00000000000b', 'a0000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-00000000000b', 'teacher',   'full_time', 'b0000000-0000-0000-0000-000000000001',   (SELECT id FROM positions WHERE slug = 'teacher' AND school_id IS NULL), 'active'),
  ('11110000-0000-0000-0000-00000000000c', 'e0000000-0000-0000-0000-00000000000c', 'a0000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-00000000000c', 'principal', 'full_time', 'b0000000-0000-0000-0000-000000000001',   (SELECT id FROM positions WHERE slug = 'principal' AND school_id IS NULL), 'active');

INSERT INTO staff_feature_overrides (affiliation_id, feature_key, enabled) VALUES
  ('11110000-0000-0000-0000-00000000000c', 'can_manage_fees', true);

-- ── canonical rows (what CanonicalCapabilityResolver reads, keyed by membership) ──
INSERT INTO employments (id, membership_id, organization_id, position_id, permission_template_id, status, started_at) VALUES
  ('22220000-0000-0000-0000-00000000000a', 'f0000000-0000-0000-0000-00000000000a', 'a0000000-0000-0000-0000-000000000001', (SELECT id FROM positions WHERE slug = 'teacher' AND school_id IS NULL), NULL,                                     'active', NOW()),
  ('22220000-0000-0000-0000-00000000000b', 'f0000000-0000-0000-0000-00000000000b', 'a0000000-0000-0000-0000-000000000001', (SELECT id FROM positions WHERE slug = 'teacher' AND school_id IS NULL), 'b0000000-0000-0000-0000-000000000001',   'active', NOW()),
  ('22220000-0000-0000-0000-00000000000c', 'f0000000-0000-0000-0000-00000000000c', 'a0000000-0000-0000-0000-000000000001', (SELECT id FROM positions WHERE slug = 'principal' AND school_id IS NULL), 'b0000000-0000-0000-0000-000000000001',   'active', NOW());

INSERT INTO employment_feature_overrides (employment_id, feature_key, enabled) VALUES
  ('22220000-0000-0000-0000-00000000000c', 'can_manage_fees', true);

-- ── access contexts (the sweep: reference_id = affiliation for staff; membership_id = canonical key) ──
INSERT INTO access_contexts (id, identity_id, type, reference_id, organization_id, membership_id, status) VALUES
  ('33330000-0000-0000-0000-00000000000a', 'd0000000-0000-0000-0000-00000000000a', 'staff',  '11110000-0000-0000-0000-00000000000a', 'a0000000-0000-0000-0000-000000000001', 'f0000000-0000-0000-0000-00000000000a', 'active'),
  ('33330000-0000-0000-0000-00000000000b', 'd0000000-0000-0000-0000-00000000000b', 'staff',  '11110000-0000-0000-0000-00000000000b', 'a0000000-0000-0000-0000-000000000001', 'f0000000-0000-0000-0000-00000000000b', 'active'),
  ('33330000-0000-0000-0000-00000000000c', 'd0000000-0000-0000-0000-00000000000c', 'staff',  '11110000-0000-0000-0000-00000000000c', 'a0000000-0000-0000-0000-000000000001', 'f0000000-0000-0000-0000-00000000000c', 'active'),
  ('33330000-0000-0000-0000-00000000000d', 'd0000000-0000-0000-0000-00000000000d', 'owner',  '44440000-0000-0000-0000-00000000000d', 'a0000000-0000-0000-0000-000000000001', 'f0000000-0000-0000-0000-00000000000d', 'active'),
  ('33330000-0000-0000-0000-00000000000e', 'd0000000-0000-0000-0000-00000000000e', 'parent', '44440000-0000-0000-0000-00000000000e', 'a0000000-0000-0000-0000-000000000001', 'f0000000-0000-0000-0000-00000000000e', 'active');
