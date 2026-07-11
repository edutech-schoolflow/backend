-- Module B — Fees, Invoicing & Payments. Fee types are per term+class; one invoice per student+term
-- made of lines; payments record base + platform_fee (the school gets 100% of base, the platform keeps
-- the convenience fee). Discounts/scholarships (source=discount) and store lines come in later slices.

CREATE TABLE fee_types (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    school_id   UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    term_id     UUID NOT NULL REFERENCES terms(id) ON DELETE CASCADE,
    name        VARCHAR(120) NOT NULL,             -- "Tuition", "PTA levy"
    amount      NUMERIC(12,2) NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_fee_types_school_term ON fee_types (school_id, term_id);

CREATE TABLE fee_type_classes (                    -- which classes a fee applies to
    fee_type_id UUID NOT NULL REFERENCES fee_types(id) ON DELETE CASCADE,
    class_id    UUID NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
    PRIMARY KEY (fee_type_id, class_id)
);

CREATE TABLE invoices (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    student_id   UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    school_id    UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    term_id      UUID NOT NULL REFERENCES terms(id) ON DELETE CASCADE,
    status       VARCHAR(20) NOT NULL DEFAULT 'draft',   -- draft|issued|partial|paid|void
    total_amount NUMERIC(12,2) NOT NULL DEFAULT 0,       -- net of discounts
    total_paid   NUMERIC(12,2) NOT NULL DEFAULT 0,
    balance      NUMERIC(12,2) NOT NULL DEFAULT 0,
    due_date     DATE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (student_id, term_id),
    CONSTRAINT chk_invoices_status CHECK (status IN ('draft', 'issued', 'partial', 'paid', 'void'))
);
CREATE INDEX ix_invoices_school_term ON invoices (school_id, term_id, status);
CREATE INDEX ix_invoices_student ON invoices (student_id);

CREATE TABLE invoice_lines (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_id          UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    source              VARCHAR(20) NOT NULL DEFAULT 'fee',     -- fee | store | discount
    fee_type_id         UUID REFERENCES fee_types(id) ON DELETE SET NULL,
    store_assignment_id UUID,                                   -- when source=store (later)
    description         VARCHAR(160) NOT NULL,
    amount              NUMERIC(12,2) NOT NULL,                 -- negative for discount lines
    paid                NUMERIC(12,2) NOT NULL DEFAULT 0,
    balance             NUMERIC(12,2) NOT NULL DEFAULT 0,
    status              VARCHAR(20) NOT NULL DEFAULT 'unpaid'   -- unpaid|partial|paid
);
CREATE INDEX ix_invoice_lines_invoice ON invoice_lines (invoice_id);

CREATE TABLE payments (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_id        UUID REFERENCES invoices(id) ON DELETE SET NULL,   -- NULL for application-fee payments
    application_id    UUID REFERENCES applications(id) ON DELETE SET NULL,
    parent_id         UUID NOT NULL REFERENCES parents(id) ON DELETE CASCADE,
    student_id        UUID REFERENCES students(id) ON DELETE SET NULL,
    school_id         UUID NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
    base_amount       NUMERIC(12,2) NOT NULL,                 -- toward fees (school's)
    platform_fee      NUMERIC(12,2) NOT NULL DEFAULT 0,       -- convenience fee on top (platform's)
    total_charged     NUMERIC(12,2) NOT NULL,                 -- base + platform_fee
    method            VARCHAR(20) NOT NULL,                   -- virtual_account|card|transfer|ussd|stub
    monnify_reference VARCHAR(120) NOT NULL,
    status            VARCHAR(20) NOT NULL DEFAULT 'pending', -- pending|successful|failed
    paid_at           TIMESTAMPTZ,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_payments_status CHECK (status IN ('pending', 'successful', 'failed'))
);
CREATE INDEX ix_payments_parent ON payments (parent_id);
CREATE INDEX ix_payments_invoice ON payments (invoice_id);
CREATE UNIQUE INDEX uq_payments_reference ON payments (monnify_reference);
