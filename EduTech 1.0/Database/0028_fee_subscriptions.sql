-- Parent-pull billing: no auto-drafted invoices. A student's bill = the approved fee types applicable
-- to their class+term; the parent pays per fee type. Paying an OPTIONAL fee subscribes the student to
-- it (e.g. lessons). Payments now reference the fee type directly.

CREATE TABLE fee_subscriptions (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    student_id    UUID NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    fee_type_id   UUID NOT NULL REFERENCES fee_types(id) ON DELETE CASCADE,
    subscribed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (student_id, fee_type_id)
);
CREATE INDEX ix_fee_subscriptions_student ON fee_subscriptions (student_id);

ALTER TABLE payments ADD COLUMN fee_type_id UUID REFERENCES fee_types(id) ON DELETE SET NULL;
ALTER TABLE payments ADD COLUMN term_id     UUID REFERENCES terms(id) ON DELETE SET NULL;
CREATE INDEX ix_payments_student_fee ON payments (student_id, fee_type_id);
