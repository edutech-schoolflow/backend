-- EDD-001 Sprint 4/5 — the owner IS an employment. school_owners rows already carry identity_id
-- (0036); adding position_id (→ the platform-seeded 'owner' position) makes each row a full
-- employment record (identity + organization + position). The physical merge into one employments
-- table rides the Workforce extraction (EDD-002 V6); nothing reads differently until then.

ALTER TABLE school_owners ADD COLUMN IF NOT EXISTS position_id UUID REFERENCES positions(id);

UPDATE school_owners o SET position_id = p.id
FROM positions p
WHERE o.position_id IS NULL AND p.school_id IS NULL AND p.slug = 'owner';
