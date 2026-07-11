-- pgcrypto provides gen_random_uuid() (built into core on PG13+, but ensured here for safety).
CREATE EXTENSION IF NOT EXISTS pgcrypto;
