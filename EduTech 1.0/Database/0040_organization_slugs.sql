-- FE-001 Phase 2 groundwork (D-FE-1 decided: everything under /o/{slug}) — every organization gets
-- a stable, URL-safe slug. Prefers the subdomain, else a name-derived slug, else a short id;
-- duplicates disambiguated with an id suffix. New shells receive an id-based slug at creation and
-- the onboarding wizard re-slugs from the chosen name.

ALTER TABLE schools ADD COLUMN IF NOT EXISTS slug VARCHAR(80);

UPDATE schools SET slug = sub.slug
FROM (
    SELECT id,
           base || CASE WHEN COUNT(*) OVER (PARTITION BY base) > 1
                        THEN '-' || left(id::text, 4) ELSE '' END AS slug
    FROM (
        SELECT id, COALESCE(
                   NULLIF(subdomain, ''),
                   NULLIF(trim(both '-' from regexp_replace(lower(COALESCE(name, '')), '[^a-z0-9]+', '-', 'g')), ''),
                   's-' || left(id::text, 8)) AS base
        FROM schools
    ) b
) sub
WHERE schools.id = sub.id AND schools.slug IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_schools_slug ON schools(slug);
