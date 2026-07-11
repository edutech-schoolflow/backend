-- Sessions need a real chronological order — promotion ("which session is next") and progression depend on
-- it, and a string sort of the name is unreliable. starts_in = the calendar year the session begins
-- (e.g. 2024 for "2024/2025"), parsed from the name.
ALTER TABLE academic_years ADD COLUMN IF NOT EXISTS starts_in INT;

UPDATE academic_years
   SET starts_in = (regexp_match(name, '\d{4}'))[1]::int
 WHERE starts_in IS NULL AND name ~ '\d{4}';

CREATE INDEX IF NOT EXISTS ix_academic_years_order ON academic_years (school_id, starts_in);
