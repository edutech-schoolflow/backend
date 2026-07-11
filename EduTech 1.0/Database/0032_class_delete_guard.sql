-- Deleting a class must never silently orphan its students or destroy their grades/fees. The service
-- layer blocks this first with a friendly message; these RESTRICTs are defense-in-depth so that a raw or
-- direct delete errors out instead of cascading/NULL-ing dependent records.

-- students.class_id: was ON DELETE SET NULL (silently orphaned students) -> RESTRICT.
ALTER TABLE students DROP CONSTRAINT IF EXISTS students_class_id_fkey;
ALTER TABLE students ADD CONSTRAINT students_class_id_fkey
    FOREIGN KEY (class_id) REFERENCES classes(id) ON DELETE RESTRICT;

-- grade_records.class_arm_id: was ON DELETE CASCADE (grades wiped) -> RESTRICT.
ALTER TABLE grade_records DROP CONSTRAINT IF EXISTS grade_records_class_arm_id_fkey;
ALTER TABLE grade_records ADD CONSTRAINT grade_records_class_arm_id_fkey
    FOREIGN KEY (class_arm_id) REFERENCES class_arms(id) ON DELETE RESTRICT;

-- fee_type_classes.class_id: was ON DELETE CASCADE (fees silently orphaned) -> RESTRICT.
ALTER TABLE fee_type_classes DROP CONSTRAINT IF EXISTS fee_type_classes_class_id_fkey;
ALTER TABLE fee_type_classes ADD CONSTRAINT fee_type_classes_class_id_fkey
    FOREIGN KEY (class_id) REFERENCES classes(id) ON DELETE RESTRICT;
