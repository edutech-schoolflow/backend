-- Students belong to a CLASS directly. An arm (stream) is OPTIONAL — set only when a school splits a
-- class into A/B/C. Previously a student required a class_arm_id and every class carried a default
-- (unnamed) arm. This moves the enrolment link onto class_id, keeps the arm optional, and removes the
-- old default arms so a class can simply have no streams.

ALTER TABLE students ADD COLUMN IF NOT EXISTS class_id UUID REFERENCES classes(id) ON DELETE SET NULL;

-- Backfill each student's class from their current arm.
UPDATE students s
   SET class_id = ca.class_id
  FROM class_arms ca
 WHERE ca.id = s.class_arm_id
   AND s.class_id IS NULL;

-- Retire the auto-created default (unnamed) arms: detach their students (now class-only), then drop.
UPDATE students
   SET class_arm_id = NULL
 WHERE class_arm_id IN (SELECT id FROM class_arms WHERE arm = '');

DELETE FROM class_arms WHERE arm = '';

CREATE INDEX IF NOT EXISTS ix_students_class_id ON students(class_id);
