-- EDD-001 Sprint 1 — Identity foundation (invisible to the running app).
-- Creates the global identities table, links the three legacy silos to it, and backfills by
-- normalized phone: the same phone across parents/staff_users/school_owners merges into ONE
-- identity (the dual-persona payoff). Legacy logins keep working untouched; refresh_tokens and
-- otp_codes are already polymorphic (actor_type/target_type) and need no change.
-- Idempotent: safe to re-run (IF NOT EXISTS / ON CONFLICT / identity_id IS NULL guards).

CREATE TABLE IF NOT EXISTS identities (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    first_name         VARCHAR(120) NOT NULL,
    middle_name        VARCHAR(120),
    last_name          VARCHAR(120) NOT NULL,
    phone              VARCHAR(20)  NOT NULL,          -- canonical identifier (EDD-001 D1), +234-normalized
    email              VARCHAR(255),
    password_hash      TEXT,                            -- NULL = pending/unclaimed (claim-on-register)
    phone_verified     BOOLEAN NOT NULL DEFAULT FALSE,
    email_verified     BOOLEAN NOT NULL DEFAULT FALSE,
    status             VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | active | suspended
    failed_login_count INTEGER NOT NULL DEFAULT 0,
    locked_until       TIMESTAMPTZ,
    photo_url          TEXT,
    preferred_language VARCHAR(10),
    timezone           VARCHAR(60),
    last_login_at      TIMESTAMPTZ,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT identities_phone_key UNIQUE (phone),
    CONSTRAINT identities_email_key UNIQUE (email),
    CONSTRAINT identities_status_chk CHECK (status IN ('pending', 'active', 'suspended'))
);

ALTER TABLE parents       ADD COLUMN IF NOT EXISTS identity_id UUID REFERENCES identities(id);
ALTER TABLE staff_users   ADD COLUMN IF NOT EXISTS identity_id UUID REFERENCES identities(id);
ALTER TABLE school_owners ADD COLUMN IF NOT EXISTS identity_id UUID REFERENCES identities(id);

CREATE INDEX IF NOT EXISTS ix_parents_identity       ON parents(identity_id);
CREATE INDEX IF NOT EXISTS ix_staff_users_identity   ON staff_users(identity_id);
CREATE INDEX IF NOT EXISTS ix_school_owners_identity ON school_owners(identity_id);

-- ── Backfill ─────────────────────────────────────────────────────────────────────────────────
-- One identity per normalized phone. Base row (name/password source) is chosen per phone by:
-- has password > phone verified > earliest created. Verified flags are OR-ed across silos.
-- Emails are deduplicated (identities.email is unique): first phone to claim an email keeps it.

WITH src AS (
    SELECT id, first_name, middle_name, last_name, phone, email, password_hash,
           phone_verified, FALSE AS email_verified, created_at
    FROM parents
    UNION ALL
    SELECT id, first_name, middle_name, last_name, phone, email, password_hash,
           phone_verified, email_verified, created_at
    FROM staff_users
    UNION ALL
    SELECT id, first_name, middle_name, last_name, phone, email, password_hash,
           phone_verified, email_verified, created_at
    FROM school_owners
),
norm AS (
    SELECT *,
        CASE
            WHEN phone ~ '^\+234[0-9]{10}$' THEN phone
            WHEN phone ~ '^234[0-9]{10}$'   THEN '+' || phone
            WHEN phone ~ '^0[0-9]{10}$'     THEN '+234' || substring(phone FROM 2)
            ELSE phone
        END AS nphone
    FROM src
),
ranked AS (
    SELECT *,
        ROW_NUMBER() OVER (
            PARTITION BY nphone
            ORDER BY (password_hash IS NOT NULL) DESC, phone_verified DESC, created_at ASC
        ) AS rn
    FROM norm
),
best AS (SELECT * FROM ranked WHERE rn = 1),
merged AS (
    SELECT b.nphone,
           b.first_name, b.middle_name, b.last_name, b.password_hash,
           COALESCE(b.email,
                    (SELECT MIN(n.email) FROM norm n WHERE n.nphone = b.nphone AND n.email IS NOT NULL)) AS email,
           (SELECT BOOL_OR(n.phone_verified) FROM norm n WHERE n.nphone = b.nphone) AS phone_verified,
           (SELECT BOOL_OR(n.email_verified) FROM norm n WHERE n.nphone = b.nphone) AS email_verified,
           (SELECT MIN(n.created_at)         FROM norm n WHERE n.nphone = b.nphone) AS created_at
    FROM best b
),
email_deduped AS (
    SELECT *,
        CASE WHEN email IS NULL THEN NULL
             WHEN ROW_NUMBER() OVER (PARTITION BY email ORDER BY created_at, nphone) = 1 THEN email
             ELSE NULL
        END AS unique_email
    FROM merged
)
INSERT INTO identities (first_name, middle_name, last_name, phone, email, password_hash,
                        phone_verified, email_verified, status, created_at)
SELECT first_name, middle_name, last_name, nphone, unique_email, password_hash,
       COALESCE(phone_verified, FALSE), COALESCE(email_verified, FALSE),
       CASE WHEN password_hash IS NOT NULL THEN 'active' ELSE 'pending' END,
       created_at
FROM email_deduped
WHERE nphone IS NOT NULL AND nphone <> ''
ON CONFLICT (phone) DO NOTHING;

-- ── Link the silos ───────────────────────────────────────────────────────────────────────────

UPDATE parents p SET identity_id = i.id
FROM identities i
WHERE p.identity_id IS NULL
  AND i.phone = CASE
        WHEN p.phone ~ '^\+234[0-9]{10}$' THEN p.phone
        WHEN p.phone ~ '^234[0-9]{10}$'   THEN '+' || p.phone
        WHEN p.phone ~ '^0[0-9]{10}$'     THEN '+234' || substring(p.phone FROM 2)
        ELSE p.phone END;

UPDATE staff_users s SET identity_id = i.id
FROM identities i
WHERE s.identity_id IS NULL
  AND i.phone = CASE
        WHEN s.phone ~ '^\+234[0-9]{10}$' THEN s.phone
        WHEN s.phone ~ '^234[0-9]{10}$'   THEN '+' || s.phone
        WHEN s.phone ~ '^0[0-9]{10}$'     THEN '+234' || substring(s.phone FROM 2)
        ELSE s.phone END;

UPDATE school_owners o SET identity_id = i.id
FROM identities i
WHERE o.identity_id IS NULL
  AND i.phone = CASE
        WHEN o.phone ~ '^\+234[0-9]{10}$' THEN o.phone
        WHEN o.phone ~ '^234[0-9]{10}$'   THEN '+' || o.phone
        WHEN o.phone ~ '^0[0-9]{10}$'     THEN '+234' || substring(o.phone FROM 2)
        ELSE o.phone END;
