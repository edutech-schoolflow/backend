-- Academic calendar (per school). Years hold terms; exactly one year and one term per year may be
-- marked current. school_id is denormalized onto terms so every tenant query filters by @SchoolId.

CREATE TABLE academic_years (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id   UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    name        VARCHAR(20) NOT NULL,                 -- e.g. "2024/2025"
    is_current  BOOLEAN NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (school_id, name)
);
CREATE INDEX ix_academic_years_school ON academic_years (school_id);

CREATE TABLE terms (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id         UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    academic_year_id  UUID NOT NULL REFERENCES academic_years(id) ON DELETE CASCADE,
    name              VARCHAR(10) NOT NULL,            -- first | second | third
    start_date        DATE,
    end_date          DATE,
    is_current        BOOLEAN NOT NULL DEFAULT FALSE,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (academic_year_id, name)
);
CREATE INDEX ix_terms_school ON terms (school_id);
CREATE INDEX ix_terms_year ON terms (academic_year_id);
