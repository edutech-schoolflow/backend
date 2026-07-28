-- EDD-016 Sprint 1 — Attendance Canonicalization. The first Platform Convergence module: the actor ids
-- (affiliation_id / user_id) that scoped and attributed attendance become canonical (membership / identity).
-- Pre-launch V1 has no data, so this repoints columns directly (no backfill/dual-write window).

-- Student attendance — who submitted (NULL = the owner).
ALTER TABLE attendance_records DROP COLUMN IF EXISTS submitted_by_affiliation_id;
ALTER TABLE attendance_records ADD COLUMN IF NOT EXISTS submitted_by_membership_id UUID REFERENCES memberships(id);

-- Class teacher (Students/Classes) — attendance's markable-arm scoping joins this.
ALTER TABLE classes    DROP COLUMN IF EXISTS class_teacher_affiliation_id;
ALTER TABLE classes    ADD COLUMN IF NOT EXISTS class_teacher_membership_id UUID REFERENCES memberships(id);
ALTER TABLE class_arms DROP COLUMN IF EXISTS class_teacher_affiliation_id;
ALTER TABLE class_arms ADD COLUMN IF NOT EXISTS class_teacher_membership_id UUID REFERENCES memberships(id);

-- Staff attendance (Workforce) — whose check-in, and who manually overrode it.
-- Dropping affiliation_id also drops its UNIQUE(affiliation_id, date); re-add on membership.
ALTER TABLE staff_checkins DROP COLUMN IF EXISTS affiliation_id;
ALTER TABLE staff_checkins ADD COLUMN IF NOT EXISTS membership_id UUID NOT NULL REFERENCES memberships(id) ON DELETE CASCADE;
ALTER TABLE staff_checkins DROP COLUMN IF EXISTS overridden_by;
ALTER TABLE staff_checkins ADD COLUMN IF NOT EXISTS overridden_by_identity_id UUID REFERENCES identities(id);
ALTER TABLE staff_checkins ADD CONSTRAINT staff_checkins_membership_date_key UNIQUE (membership_id, date);
