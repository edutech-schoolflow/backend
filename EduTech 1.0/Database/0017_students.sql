-- Students (per school) + their guardians (embedded contacts). A student optionally sits in a class
-- arm. admission_number is assigned at creation and unique per school. Parent-account links and
-- documents come in later slices.

CREATE TABLE students (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id       UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    first_name      VARCHAR(120) NOT NULL,
    middle_name     VARCHAR(120),
    last_name       VARCHAR(120) NOT NULL,
    date_of_birth   DATE NOT NULL,
    gender          VARCHAR(10) NOT NULL,           -- male | female
    photo_url       TEXT,
    previous_school TEXT,
    medical_notes   TEXT,
    admission_number VARCHAR(50),
    class_arm_id    UUID REFERENCES class_arms(id) ON DELETE SET NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'active',   -- active | withdrawn
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (school_id, admission_number)
);
CREATE INDEX ix_students_school ON students (school_id);
CREATE INDEX ix_students_arm ON students (class_arm_id);

CREATE TABLE student_guardians (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id     UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    student_id    UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    name          VARCHAR(255) NOT NULL,
    phone         VARCHAR(20) NOT NULL,
    relationship  VARCHAR(50) NOT NULL,
    email         VARCHAR(255),
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_student_guardians_student ON student_guardians (student_id);
