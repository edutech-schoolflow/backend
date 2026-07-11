-- Release feature flags (operational on/off control for whole product features).
-- DISTINCT from staff permission flags: these answer "is feature X switched on in prod right now?",
-- toggled from the Platform Admin CMS for batched rollout + incident kill-switches.

-- Global default per feature.
CREATE TABLE feature_flags (
    key         VARCHAR(80) PRIMARY KEY,        -- e.g. fees, attendance, students
    description TEXT,
    enabled     BOOLEAN NOT NULL DEFAULT FALSE, -- ship dark: new features default OFF
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Per-school override (pilot a feature with specific schools before full rollout).
CREATE TABLE school_feature_flags (
    school_id   UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    key         VARCHAR(80) NOT NULL REFERENCES feature_flags(key) ON DELETE CASCADE,
    enabled     BOOLEAN NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (school_id, key)
);

CREATE INDEX ix_school_feature_flags_key ON school_feature_flags (key);

-- Seed the known feature keys (all OFF). Idempotent — matches FeatureKeys.All; the app also
-- re-seeds on startup so new keys appear without a manual migration.
INSERT INTO feature_flags (key, description, enabled) VALUES
    ('fees',       'Fees & invoicing',        FALSE),
    ('attendance', 'Attendance',              FALSE),
    ('students',   'Student records',         FALSE),
    ('grades',     'Grades & exams',          FALSE),
    ('store',      'School store',            FALSE),
    ('compliance', 'School KYC / compliance', FALSE)
ON CONFLICT (key) DO NOTHING;
