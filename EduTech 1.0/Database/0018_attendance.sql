-- Daily attendance register: one record per class arm per day, with a per-student mark.
-- The class teacher of an arm marks that arm's register (owner may mark any); re-submitting a day
-- REPLACES that day's marks. term_id is stamped from the school's current term for later reporting.

CREATE TABLE attendance_records (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id                   UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    class_arm_id                UUID NOT NULL REFERENCES class_arms(id) ON DELETE CASCADE,
    term_id                     UUID REFERENCES terms(id) ON DELETE SET NULL,
    attendance_date             DATE NOT NULL,
    submitted_by_affiliation_id UUID REFERENCES staff_affiliations(id),  -- NULL when submitted by the owner
    submitted_at                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (class_arm_id, attendance_date)         -- one register per arm per day (upsert key)
);
CREATE INDEX ix_attendance_records_school_date ON attendance_records (school_id, attendance_date);
CREATE INDEX ix_attendance_records_arm ON attendance_records (class_arm_id);

CREATE TABLE attendance_marks (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id            UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    attendance_record_id UUID NOT NULL REFERENCES attendance_records(id) ON DELETE CASCADE,
    student_id           UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    status               VARCHAR(10) NOT NULL,      -- present | absent | late
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (attendance_record_id, student_id)
);
CREATE INDEX ix_attendance_marks_record ON attendance_marks (attendance_record_id);
CREATE INDEX ix_attendance_marks_student ON attendance_marks (student_id);
