-- Report cards: a school-configurable grading scale, plus a per-(student, term) report holding the
-- human-entered bits (comments, behavioral ratings, resumption date) and a draft -> published lifecycle.
-- Subject grades, totals and the attendance summary are COMPUTED on read from grade_entries / attendance.

CREATE TABLE grade_boundaries (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id   UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    min_score   INT NOT NULL,
    max_score   INT NOT NULL,
    grade       VARCHAR(5) NOT NULL,        -- "A", "B", ... (school-defined, free text)
    remark      VARCHAR(60) NOT NULL,       -- "Excellent", "Very Good", ...
    sort_order  INT NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_grade_boundaries_school ON grade_boundaries (school_id);

CREATE TABLE report_cards (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id            UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    student_id           UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    term_id              UUID NOT NULL REFERENCES terms(id) ON DELETE CASCADE,
    class_arm_id         UUID REFERENCES class_arms(id) ON DELETE SET NULL,
    teacher_comment      TEXT,
    principal_comment    TEXT,
    next_term_resumption DATE,
    status               VARCHAR(20) NOT NULL DEFAULT 'draft',   -- draft | published
    published_at         TIMESTAMPTZ,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (student_id, term_id)
);
CREATE INDEX ix_report_cards_school_term ON report_cards (school_id, term_id);
CREATE INDEX ix_report_cards_arm ON report_cards (class_arm_id);

CREATE TABLE report_behavioral_ratings (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id      UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    report_card_id UUID NOT NULL REFERENCES report_cards(id) ON DELETE CASCADE,
    trait          VARCHAR(30) NOT NULL,    -- punctuality | attentiveness | cooperation | neatness | politeness | leadership
    score          INT NOT NULL,            -- 1..5
    UNIQUE (report_card_id, trait)
);
CREATE INDEX ix_report_behavioral_report ON report_behavioral_ratings (report_card_id);
