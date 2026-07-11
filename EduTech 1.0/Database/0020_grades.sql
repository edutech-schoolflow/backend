-- Grades: a per-class subjects catalog + term score entry. One grade_record per
-- (arm, subject, term, assessment_type) with a per-student score; records move draft -> published.
-- Grading scale, positions, comments and report cards come in a later slice.

CREATE TABLE subjects (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id     UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    class_id      UUID NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
    name          VARCHAR(120) NOT NULL,
    max_ca        INT NOT NULL DEFAULT 30,    -- cap for EACH CA (first_ca, second_ca)
    max_exam      INT NOT NULL DEFAULT 40,    -- cap for the exam  (30 + 30 + 40 = 100)
    display_order INT NOT NULL DEFAULT 0,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (class_id, name)
);
CREATE INDEX ix_subjects_school ON subjects (school_id);
CREATE INDEX ix_subjects_class ON subjects (class_id);


CREATE TABLE grade_records (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id                   UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    class_arm_id                UUID NOT NULL REFERENCES class_arms(id) ON DELETE CASCADE,
    subject_id                  UUID NOT NULL REFERENCES subjects(id) ON DELETE CASCADE,
    term_id                     UUID NOT NULL REFERENCES terms(id) ON DELETE CASCADE,
    assessment_type             VARCHAR(20) NOT NULL,                    -- first_ca | second_ca | exam
    max_score                   INT NOT NULL,                            -- cap snapshot at submit time
    status                      VARCHAR(20) NOT NULL DEFAULT 'draft',    -- draft | published
    published_at                TIMESTAMPTZ,
    submitted_by_affiliation_id UUID REFERENCES staff_affiliations(id),  -- NULL when submitted by the owner
    submitted_at                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (class_arm_id, subject_id, term_id, assessment_type)          -- one record per assessment column
);
CREATE INDEX ix_grade_records_school_term ON grade_records (school_id, term_id);
CREATE INDEX ix_grade_records_arm ON grade_records (class_arm_id);

CREATE TABLE grade_entries (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id       UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    grade_record_id UUID NOT NULL REFERENCES grade_records(id) ON DELETE CASCADE,
    student_id      UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    score           NUMERIC(5,2) NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (grade_record_id, student_id)
);
CREATE INDEX ix_grade_entries_record ON grade_entries (grade_record_id);
CREATE INDEX ix_grade_entries_student ON grade_entries (student_id);
