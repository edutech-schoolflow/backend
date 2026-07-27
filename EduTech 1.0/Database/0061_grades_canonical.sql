-- EDD-016 Sprint 2 — Grades Canonicalization. Removes affiliation_id from the Grades module: the actor
-- who submitted a grade, and the subject teacher who may enter it, become canonical memberships.
-- Pre-launch V1 has no data, so this repoints columns directly (no backfill/dual-write window).

-- Grade attribution — who submitted (NULL = the owner). Grades-owned.
ALTER TABLE grade_records DROP COLUMN IF EXISTS submitted_by_affiliation_id;
ALTER TABLE grade_records ADD COLUMN IF NOT EXISTS submitted_by_membership_id UUID REFERENCES memberships(id);

-- Subject teacher (Students/Classes) — Grades' secondary-tier scoping joins this.
-- Dropping teacher_affiliation_id also drops its UNIQUE(class_arm_id, teacher_affiliation_id, subject);
-- re-add on the canonical membership.
ALTER TABLE class_subject_teachers DROP COLUMN IF EXISTS teacher_affiliation_id;
ALTER TABLE class_subject_teachers ADD COLUMN IF NOT EXISTS teacher_membership_id UUID NOT NULL REFERENCES memberships(id);
ALTER TABLE class_subject_teachers ADD CONSTRAINT class_subject_teachers_arm_membership_subject_key
    UNIQUE (class_arm_id, teacher_membership_id, subject);
