-- Move from a single full_name to structured first_name / middle_name? / last_name across all actor
-- tables. Aligns with NIMC/Dojah (which store names structured) and makes identity name-matching
-- exact. Backfill splits the existing full_name best-effort (first word = first name, the rest = last
-- name); single-word names duplicate into last_name to satisfy NOT NULL.

DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['school_owners', 'staff_users', 'parents', 'platform_admins']
    LOOP
        EXECUTE format('ALTER TABLE %I ADD COLUMN first_name  VARCHAR(120)', t);
        EXECUTE format('ALTER TABLE %I ADD COLUMN middle_name VARCHAR(120)', t);
        EXECUTE format('ALTER TABLE %I ADD COLUMN last_name   VARCHAR(120)', t);

        EXECUTE format($f$
            UPDATE %I SET
                first_name = COALESCE(NULLIF(split_part(full_name, ' ', 1), ''), 'Unknown'),
                last_name  = COALESCE(
                    NULLIF(btrim(substr(full_name, length(split_part(full_name, ' ', 1)) + 1)), ''),
                    NULLIF(split_part(full_name, ' ', 1), ''),
                    'Unknown')
        $f$, t);

        EXECUTE format('ALTER TABLE %I ALTER COLUMN first_name SET NOT NULL', t);
        EXECUTE format('ALTER TABLE %I ALTER COLUMN last_name  SET NOT NULL', t);
        EXECUTE format('ALTER TABLE %I DROP COLUMN full_name', t);
    END LOOP;
END $$;
