-- Spec-align (Actor 3 §3.5 + Module A §A.2): a `child_profile` is the parent-owned GLOBAL child
-- identity; a `student` becomes a thin ENROLLMENT of that child at a school. Bio moves from students
-- to child_profiles; embedded student_guardians become guardian_contacts (non-account) PLUS a pending
-- parent account linked via parent_children (the school-add-by-phone path). Existing rows are
-- relocated, not lost.

-- 1. New tables --------------------------------------------------------------

CREATE TABLE child_profiles (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    parent_id       UUID REFERENCES parents(id),          -- owner; nullable (school-add before a claim)
    first_name      VARCHAR(120) NOT NULL,
    middle_name     VARCHAR(120),
    last_name       VARCHAR(120) NOT NULL,
    date_of_birth   DATE NOT NULL,
    gender          VARCHAR(10),                          -- male | female
    photo_url       TEXT,
    previous_school TEXT,
    medical_info    TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_child_profiles_parent ON child_profiles (parent_id);

CREATE TABLE parent_children (
    parent_id        UUID NOT NULL REFERENCES parents(id) ON DELETE CASCADE,
    child_profile_id UUID NOT NULL REFERENCES child_profiles(id) ON DELETE CASCADE,
    relationship     VARCHAR(50),                         -- mother | father | guardian
    is_primary       BOOLEAN NOT NULL DEFAULT FALSE,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (parent_id, child_profile_id)
);
CREATE INDEX ix_parent_children_child ON parent_children (child_profile_id);

CREATE TABLE guardian_contacts (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    child_profile_id UUID NOT NULL REFERENCES child_profiles(id) ON DELETE CASCADE,
    name             VARCHAR(255) NOT NULL,
    phone            VARCHAR(20) NOT NULL,
    relationship     VARCHAR(50),
    email            VARCHAR(255),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_guardian_contacts_child ON guardian_contacts (child_profile_id);

-- 2. students gains the enrollment columns -----------------------------------

ALTER TABLE students ADD COLUMN child_profile_id UUID REFERENCES child_profiles(id);
ALTER TABLE students ADD COLUMN enrolled_at      TIMESTAMPTZ;

-- 3. Backfill: relocate existing data (bio -> child_profiles; guardians -> guardian_contacts +
--    a pending parent linked via parent_children). The first guardian is the primary/owner.
DO $$
DECLARE
    s   RECORD;
    g   RECORD;
    cp_id UUID;
    p_id  UUID;
    first_guardian BOOLEAN;
BEGIN
    FOR s IN SELECT * FROM students LOOP
        INSERT INTO child_profiles (first_name, middle_name, last_name, date_of_birth, gender,
                                    photo_url, previous_school, medical_info, created_at)
        VALUES (s.first_name, s.middle_name, s.last_name, s.date_of_birth, s.gender,
                s.photo_url, s.previous_school, s.medical_notes, s.created_at)
        RETURNING id INTO cp_id;

        UPDATE students SET child_profile_id = cp_id, enrolled_at = created_at WHERE id = s.id;

        first_guardian := TRUE;
        FOR g IN SELECT * FROM student_guardians WHERE student_id = s.id ORDER BY created_at LOOP
            IF first_guardian THEN
                -- primary guardian -> a pending parent ACCOUNT (dedup by phone) + parent_children link
                SELECT id INTO p_id FROM parents WHERE phone = g.phone;
                IF p_id IS NULL THEN
                    INSERT INTO parents (first_name, last_name, phone, status, phone_verified)
                    VALUES (
                        COALESCE(NULLIF(split_part(g.name, ' ', 1), ''), 'Guardian'),
                        COALESCE(NULLIF(btrim(substr(g.name, length(split_part(g.name, ' ', 1)) + 1)), ''),
                                 NULLIF(split_part(g.name, ' ', 1), ''), 'Guardian'),
                        g.phone, 'pending', FALSE)
                    RETURNING id INTO p_id;
                END IF;

                UPDATE child_profiles SET parent_id = p_id WHERE id = cp_id;
                INSERT INTO parent_children (parent_id, child_profile_id, relationship, is_primary)
                VALUES (p_id, cp_id, g.relationship, TRUE)
                ON CONFLICT (parent_id, child_profile_id) DO NOTHING;

                first_guardian := FALSE;
            ELSE
                -- additional guardians -> non-account contacts
                INSERT INTO guardian_contacts (child_profile_id, name, phone, relationship, email)
                VALUES (cp_id, g.name, g.phone, g.relationship, g.email);
            END IF;
        END LOOP;
    END LOOP;
END $$;

-- 4. Finalize students as a thin enrollment ----------------------------------

ALTER TABLE students ALTER COLUMN child_profile_id SET NOT NULL;

-- one ACTIVE enrollment per child (a child can re-enrol elsewhere after transfer/withdraw)
CREATE UNIQUE INDEX uq_one_active_enrollment ON students (child_profile_id) WHERE status = 'active';

-- drop the gender CHECK (its column is moving) then the relocated bio columns + the old guardians table
ALTER TABLE students DROP CONSTRAINT IF EXISTS chk_students_gender;
ALTER TABLE students
    DROP COLUMN first_name,
    DROP COLUMN middle_name,
    DROP COLUMN last_name,
    DROP COLUMN date_of_birth,
    DROP COLUMN gender,
    DROP COLUMN photo_url,
    DROP COLUMN previous_school,
    DROP COLUMN medical_notes;

DROP TABLE student_guardians;
