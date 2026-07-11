-- Defense-in-depth: enforce the fixed, closed value sets at the DB layer too (the services already
-- validate them in C# via the EduTech.Shared.Constants types). Kept as CHECK constraints on the
-- existing VARCHAR columns rather than native PG ENUM types, which are painful to ALTER later.
-- Existing rows were written through the validating services, so they already satisfy these.

ALTER TABLE terms
    ADD CONSTRAINT chk_terms_name
    CHECK (name IN ('first', 'second', 'third'));

ALTER TABLE classes
    ADD CONSTRAINT chk_classes_level
    CHECK (level IN ('pre_school', 'nursery', 'primary', 'junior_secondary', 'senior_secondary'));

ALTER TABLE students
    ADD CONSTRAINT chk_students_gender
    CHECK (gender IN ('male', 'female'));

ALTER TABLE students
    ADD CONSTRAINT chk_students_status
    CHECK (status IN ('active', 'withdrawn'));

ALTER TABLE attendance_marks
    ADD CONSTRAINT chk_attendance_marks_status
    CHECK (status IN ('present', 'absent', 'late'));
