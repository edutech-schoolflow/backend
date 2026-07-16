-- Staff check-in (geofenced) — Workforce context. Settings hold the school's location + rules;
-- one check-in row per affiliation per day (re-check-in replaces). "absent" rows exist only via
-- owner override — a missing row reads as absent on the board.
CREATE TABLE IF NOT EXISTS staff_attendance_settings (
    school_id          UUID PRIMARY KEY REFERENCES schools(id) ON DELETE CASCADE,
    lat                DOUBLE PRECISION,
    lng                DOUBLE PRECISION,
    geofence_radius_m  INT  NOT NULL DEFAULT 200,
    check_in_cutoff    TIME NOT NULL DEFAULT '08:00',
    work_start_time    TIME NOT NULL DEFAULT '07:30',
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS staff_checkins (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id          UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    affiliation_id     UUID NOT NULL REFERENCES staff_affiliations(id) ON DELETE CASCADE,
    date               DATE NOT NULL,
    check_in_at        TIMESTAMPTZ,
    lat                DOUBLE PRECISION,
    lng                DOUBLE PRECISION,
    distance_m         INT,
    status             VARCHAR(10) NOT NULL CHECK (status IN ('present', 'late', 'absent')),
    is_manual_override BOOLEAN NOT NULL DEFAULT FALSE,
    overridden_by      UUID,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (affiliation_id, date)
);

CREATE INDEX IF NOT EXISTS ix_staff_checkins_school_date ON staff_checkins (school_id, date);
