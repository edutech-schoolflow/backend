-- Session-scoped enrollment history: one row per student per academic session, recording which class/arm
-- they were in and how that session ended. students.class_id stays the *current* placement (fast reads);
-- this table is the durable history behind alumni and past-session ("who was in SS 1 in 2024/2025") queries.
-- class_id is RESTRICT so a class that ever held students can't be hard-deleted — history is permanent.
CREATE TABLE IF NOT EXISTS student_enrollments (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id         UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    student_id        UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    academic_year_id  UUID REFERENCES academic_years(id) ON DELETE SET NULL,
    class_id          UUID NOT NULL REFERENCES classes(id) ON DELETE RESTRICT,
    class_arm_id      UUID REFERENCES class_arms(id) ON DELETE SET NULL,
    outcome           VARCHAR(20) NOT NULL DEFAULT 'enrolled',   -- enrolled|promoted|repeated|graduated|withdrawn|transferred
    enrolled_on       DATE NOT NULL DEFAULT CURRENT_DATE,
    ended_on          DATE,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_student_enrollments_student ON student_enrollments (student_id);
CREATE INDEX IF NOT EXISTS ix_student_enrollments_class_year ON student_enrollments (class_id, academic_year_id);
CREATE INDEX IF NOT EXISTS ix_student_enrollments_school ON student_enrollments (school_id);

-- At most one enrollment per student per session (only when the session is known).
CREATE UNIQUE INDEX IF NOT EXISTS ux_student_enrollments_student_year
    ON student_enrollments (student_id, academic_year_id) WHERE academic_year_id IS NOT NULL;
