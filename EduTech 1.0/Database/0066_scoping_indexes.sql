-- V1 readiness #4 — indexes for the class-teacher scoping filters. The Attendance markable-arms and
-- Grades gradeable-arms queries filter on class_teacher_membership_id (WHERE … = @Mem); the canonical
-- migrations (0060) added the column but no index (the old affiliation column had none either). Partial
-- (teacher-assigned rows only) so the index stays small — most rows the query rejects are NULL anyway.
-- The subject-teacher (class_subject_teachers) and staff-checkin (staff_checkins) membership lookups are
-- already covered by their UNIQUE constraints; the other 0060-0065 canonical columns aren't filtered.

CREATE INDEX IF NOT EXISTS ix_class_arms_class_teacher
    ON class_arms (class_teacher_membership_id) WHERE class_teacher_membership_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_classes_class_teacher
    ON classes (class_teacher_membership_id) WHERE class_teacher_membership_id IS NOT NULL;
