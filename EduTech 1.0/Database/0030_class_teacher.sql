-- A class can take students directly (no arms). Such a class still needs a class teacher, so the
-- class-teacher slot now also lives on the class itself. When a class is split into arms, each arm
-- keeps its own class teacher (class_arms.class_teacher_affiliation_id) as before.
ALTER TABLE classes
    ADD COLUMN IF NOT EXISTS class_teacher_affiliation_id UUID REFERENCES staff_affiliations(id) ON DELETE SET NULL;
