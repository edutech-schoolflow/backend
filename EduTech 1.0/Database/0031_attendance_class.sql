-- Attendance becomes class-first: a register belongs to a CLASS, optionally to a specific arm.
-- Arm-less classes (no streams) can now take a register too (class_arm_id IS NULL).
ALTER TABLE attendance_records
    ADD COLUMN IF NOT EXISTS class_id UUID REFERENCES classes(id) ON DELETE CASCADE;

-- Backfill class_id from the arm every existing record points at.
UPDATE attendance_records r
   SET class_id = a.class_id
  FROM class_arms a
 WHERE a.id = r.class_arm_id AND r.class_id IS NULL;

ALTER TABLE attendance_records ALTER COLUMN class_arm_id DROP NOT NULL;
ALTER TABLE attendance_records ALTER COLUMN class_id SET NOT NULL;

-- Replace the old "one register per arm per day" unique key with two partial uniques:
-- arm registers keyed by (arm, date); whole-class registers keyed by (class, date).
ALTER TABLE attendance_records
    DROP CONSTRAINT IF EXISTS attendance_records_class_arm_id_attendance_date_key;
CREATE UNIQUE INDEX IF NOT EXISTS ux_attendance_arm_date
    ON attendance_records (class_arm_id, attendance_date) WHERE class_arm_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_attendance_class_date
    ON attendance_records (class_id, attendance_date) WHERE class_arm_id IS NULL;
CREATE INDEX IF NOT EXISTS ix_attendance_records_class ON attendance_records (class_id);
