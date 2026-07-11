-- Soft-delete for fee types. A fee type that has already generated invoice lines must never be hard
-- deleted (it would orphan the audit trail); it is ARCHIVED instead — kept for history but excluded
-- from new invoice drafts. Only never-used fee types can be hard deleted.

ALTER TABLE fee_types ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE;
