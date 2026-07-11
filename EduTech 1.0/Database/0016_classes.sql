-- Classes -> Arms -> teacher assignments (per school). A class (JSS 1) has arms (JSS 1A/B); each arm
-- has one class teacher (form master) and any number of subject teachers. Teacher refs point at the
-- staff member's affiliation AT THIS SCHOOL (staff_affiliations).

CREATE TABLE classes (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id     UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    name          VARCHAR(50) NOT NULL,                 -- "JSS 1", "Primary 3"
    level         VARCHAR(20) NOT NULL,                 -- nursery|primary|junior_secondary|senior_secondary
    display_order INT NOT NULL DEFAULT 0,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (school_id, name)
);
CREATE INDEX ix_classes_school ON classes (school_id);

CREATE TABLE class_arms (
    id                            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id                     UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    class_id                      UUID NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
    arm                           VARCHAR(10) NOT NULL,   -- "A", "B", "C"
    class_teacher_affiliation_id  UUID REFERENCES staff_affiliations(id),
    created_at                    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (class_id, arm)
);
CREATE INDEX ix_class_arms_school ON class_arms (school_id);
CREATE INDEX ix_class_arms_class ON class_arms (class_id);

CREATE TABLE class_subject_teachers (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id              UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    class_arm_id           UUID NOT NULL REFERENCES class_arms(id) ON DELETE CASCADE,
    teacher_affiliation_id UUID NOT NULL REFERENCES staff_affiliations(id),
    subject                VARCHAR(80) NOT NULL,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (class_arm_id, teacher_affiliation_id, subject)
);
CREATE INDEX ix_class_subject_teachers_arm ON class_subject_teachers (class_arm_id);
