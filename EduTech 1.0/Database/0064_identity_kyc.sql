-- EDD-017 Slice 1 — Identity KYC canonical data model (ADDITIVE, no runtime readers yet).
-- Identity becomes the owner of the verified fact; Compliance (Slice 2) owns the attempt history.
-- Legacy staff_users/parents nin+kyc_status stay until Slice 5 retirement (Auth still reads them until
-- Slice 3), so this is additive-first — nothing reads the new columns yet, so no dual-write window.

-- ── Identity owns the verified fact (additive extension of the frozen Identity aggregate) ─────────────
ALTER TABLE identities ADD COLUMN IF NOT EXISTS nin                     TEXT;         -- encrypted at rest; never logged/returned
ALTER TABLE identities ADD COLUMN IF NOT EXISTS kyc_status              VARCHAR(20) NOT NULL DEFAULT 'unverified';
ALTER TABLE identities ADD COLUMN IF NOT EXISTS nin_verified_at         TIMESTAMPTZ;
ALTER TABLE identities ADD COLUMN IF NOT EXISTS reverification_required BOOLEAN NOT NULL DEFAULT FALSE;  -- review axis, independent of status
ALTER TABLE identities DROP CONSTRAINT IF EXISTS identities_kyc_status_chk;
ALTER TABLE identities ADD CONSTRAINT identities_kyc_status_chk
    CHECK (kyc_status IN ('unverified','pending','verified','rejected'));

-- ── Compliance owns the attempt history (append-only; NIN attempts only — no generic kind) ────────────
CREATE TABLE IF NOT EXISTS identity_verifications (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id   UUID NOT NULL REFERENCES identities(id) ON DELETE CASCADE,
    provider      VARCHAR(30) NOT NULL,          -- 'dojah' | 'stub' | …
    provider_ref  VARCHAR(120),                  -- external reference, for async resolution
    outcome       VARCHAR(20) NOT NULL,          -- 'pending' | 'succeeded' | 'failed'
    reason        TEXT,                          -- failure detail (never the credential)
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT identity_verifications_outcome_chk CHECK (outcome IN ('pending','succeeded','failed'))
);
CREATE INDEX IF NOT EXISTS ix_identity_verifications_identity ON identity_verifications (identity_id, created_at DESC);

-- ── Backfill from the legacy actor rows (best status wins; NIN coalesced) ─────────────────────────────
-- V1 has no production data, so this is effectively a no-op on release; kept for correctness + any dev data.
-- Status precedence: verified(3) > pending(2) > rejected(1) > not_submitted/unverified(0). Legacy
-- 'not_submitted' maps to canonical 'unverified'. nin_verified_at is left NULL (no reliable historical time;
-- Slice 2 stamps it going forward).
UPDATE identities i SET
    kyc_status = CASE v.best WHEN 3 THEN 'verified' WHEN 2 THEN 'pending' WHEN 1 THEN 'rejected' ELSE 'unverified' END,
    nin        = v.nin
FROM (
    SELECT identity_id,
           MAX(rank) AS best,
           (ARRAY_AGG(nin ORDER BY rank DESC) FILTER (WHERE nin IS NOT NULL))[1] AS nin
    FROM (
        SELECT identity_id, nin,
               CASE kyc_status WHEN 'verified' THEN 3 WHEN 'pending' THEN 2 WHEN 'rejected' THEN 1 ELSE 0 END AS rank
        FROM staff_users WHERE identity_id IS NOT NULL
        UNION ALL
        SELECT identity_id, nin,
               CASE kyc_status WHEN 'verified' THEN 3 WHEN 'pending' THEN 2 WHEN 'rejected' THEN 1 ELSE 0 END AS rank
        FROM parents WHERE identity_id IS NOT NULL
    ) s
    GROUP BY identity_id
) v
WHERE i.id = v.identity_id;
