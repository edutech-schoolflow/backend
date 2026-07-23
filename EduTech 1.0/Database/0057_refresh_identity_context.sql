-- EDD-012 Sprint B2c.3c — refresh identity re-key (Phase 1: additive coexistence).
--
-- refresh_tokens is keyed on (actor_type, actor_id) — the legacy actor. This adds the canonical key:
-- identity_id (WHO) + context_id (WHERE, = access_contexts.id, NULL for an identity-scope session). New
-- tokens populate BOTH; in-flight tokens keep only the actor key and fall back to it on rotation. Reads
-- prefer identity+context (Phase 2); the actor columns retire in B2d (Phase 3). context_id carries NO FK
-- — access_contexts is a disposable projection (rows come and go); a stale context_id simply fails the
-- active-context lookup on refresh and ends the session, which is correct.

ALTER TABLE refresh_tokens ADD COLUMN IF NOT EXISTS identity_id UUID REFERENCES identities(id) ON DELETE CASCADE;
ALTER TABLE refresh_tokens ADD COLUMN IF NOT EXISTS context_id  UUID;

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_identity_context ON refresh_tokens (identity_id, context_id);
