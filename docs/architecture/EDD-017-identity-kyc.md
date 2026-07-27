# EDD-017 — Identity Verification (KYC)

**Status:** DESIGN COMPLETE. All four policy questions are resolved (see *Resolved decisions*);
implementation is deferred to the actor-table retirement, per the canonical-first rule. The first of the
three canonical domains that close EDD-016 (Identity KYC → Workforce → Family → retirement).

> **The reframe.** KYC is not a Staff feature or a Parent feature. It is a fact about a **person** — an
> **Identity**. Today that fact is wrongly duplicated as `staff_users.nin`/`kyc_status` and
> `parents.nin`/`kyc_status`, so someone who is both a teacher and a parent must verify the same NIN
> twice. This EDD makes **Identity** the single owner of "is this person verified," and turns **Compliance**
> into the *service that verifies an Identity* — not a per-actor cleanup.

## The conceptual leak this removes (Authentication)

Authentication currently asks "is this **staff** KYC'd?" by reading `staff_users.kyc_status`. After this
EDD it asks one question of the canonical graph:

```
Authentication → Identity → (KYC) → Membership → Access Context → Employment → Authorization
```

> "Is this **Identity** verified?"  — one field, one owner, read the same way for every actor type.

## Ownership (the whole point)

| Concern | Owner | Holds / does |
| --- | --- | --- |
| **The verified fact** | **Identity** | `kyc_status`, the verified `nin` (encrypted), `nin_verified_at` — the canonical "this person is verified" state, read cheaply by anyone (esp. Auth). |
| **The verification workflow** | **Compliance** (Identity-verification service) | submit → provider check → accept/reject; providers; policies; attempt history; retries; its own events + audit. Owns *how* an Identity becomes verified. |
| **Gating** | **Authentication** (consumer) | reads `Identity.kyc_status` only. Never writes it, never calls a provider, never knows about Dojah. |

Rule: **Identity owns the *state*, Compliance owns the *workflow*, Auth *consumes* the state.** No module
reads a provider or a raw NIN except Compliance.

---

## Product

A person proves who they are once, and every role they hold trusts it.

- A user (in any workspace — staff, parent, owner) submits their **NIN** (11 digits; liveness/selfie is
  gated client-side and never reaches the backend — unchanged from today).
- Compliance verifies the NIN with the provider (name-match), stores it **encrypted on the Identity**, and
  marks the Identity **verified**.
- Any school the person is a member of, any employment, any parent relationship — all read the one verified
  status. No re-verification per school, per role, or per login.
- KYC-gated actions (e.g. staff read-only mode until verified) check `Identity.kyc_status`.

**Out of scope (explicitly):** *School* KYC / proprietor onboarding (`schools.kyc_status`,
`under_review`/`approved`) is a **different aggregate** — it verifies an *organization*, not a person. It
stays where it is; it may *reuse* `IIdentityVerifier` (e.g. proprietor BVN) but its state is not
Identity KYC.

---

## Domain

### What is an *Identity Verification*?
A **verification attempt**: a person submitting a credential (NIN today; BVN possible later) to a provider,
producing a result. The Identity's **current KYC status** is a *projection* of its attempt history — the
latest successful attempt makes it `verified`.

Two things, deliberately separated:
- **`Identity` (current state, denormalized):** `kyc_status`, encrypted `nin`, `nin_verified_at`. The
  frozen fact everyone reads. Cheap, single-row, no joins — this is what the login gate reads.
- **`identity_verifications` (history, append-only):** every attempt (type, provider, submitted_at,
  outcome, reason). The source of truth for *how* the state was reached; supports audit, retries, and
  future re-verification policies.

This mirrors the platform's existing shape: a canonical projection (`Identity.kyc_status`, like
`access_contexts`) fed by an append-only source (`identity_verifications`, like the event/attempt log).

### States — *status* is separate from *review* (decision 1)
Two orthogonal axes, so **"verified" never has to be un-said.**

**Verification status** — the person's standing:
```
unverified ──submit──▶ pending ──provider OK──▶ verified
     ▲                    │
     └──────provider fail─┴──▶ rejected ──resubmit──▶ pending ──▶ …
```
| Status | Meaning |
| --- | --- |
| `unverified` | no successful verification (replaces today's `not_submitted`) |
| `pending` | an attempt is in flight / awaiting an async provider result |
| `verified` | a provider confirmed NIN + name match; the encrypted NIN is stored on the Identity |
| `rejected` | the latest attempt failed (name mismatch, not found, provider error) — resubmittable |

**Review requirement** — an *operational* axis, independent of status: `reverification_required` (bool). A
regulation change, a provider-confidence drop, or suspicious activity sets it **without** downgrading a
`verified` identity. The system never says "you are no longer verified"; it says **"you must reverify."** So
`verified` is *sticky* — reverification is **requested, not retracted**. `rejected` is always recoverable by
a new attempt.

### Who owns NIN?
**Identity.** One encrypted NIN per person (unique real-world fact). Never logged, never returned in any DTO
(the logging middleware already redacts `nin`/`bvn`). The raw NIN exists only transiently inside Compliance
during a submission; only the encrypted value is persisted, only on `identities`.

### Can an Identity have multiple verification attempts?
**Yes** — that is exactly what `identity_verifications` records. `identities.kyc_status` reflects the latest;
the history keeps every attempt for audit and retry policy. (Today's model *cannot* express this — it
overwrites a single `kyc_status` column.)

### Verification policy (decision 4) — retry limits are policy, not domain
Retry limits are **not** hard-coded into the aggregate. The aggregate *records* attempts; a **Verification
Policy** *decides* whether another attempt is allowed:
- **max consecutive failures** before lockout,
- **cooldown period** between attempts,
- **manual-review threshold** (route to a human after N failures).

This keeps operational rules (which change) out of the domain model (which shouldn't). V1 can ship a
permissive default policy; tightening it later touches configuration, not the aggregate. The append-only
history is what makes any policy enforceable.

### How do external providers integrate?
Through the existing **`IIdentityVerifier`** seam (Dojah in prod, Stub in dev) — unchanged. Compliance is the
*only* caller. The design allows a provider to be **synchronous** (today: verify returns a result inline →
`pending` is transient) or **asynchronous** (future: submit → `pending` → webhook → `verified`/`rejected`);
`identity_verifications` carries `provider` + external reference so an async callback can resolve an attempt.

### What does Auth consume?
Only `Identity.kyc_status`. The current reads of `staff_users.kyc_status` in `StaffUserRepository`,
`ContextMinter`, `UnifiedAuthService`, and `StaffAuthService` all become one read of `identities.kyc_status`
(joined via the identity the session already resolves). Auth gains **one fewer legacy dependency** and loses
the "staff-shaped KYC" concept entirely.

### What does Compliance own?
The verification **workflow**: NIN validation, the provider call, name-match, encrypt-and-store, state
transitions, attempt history, retry policy, the published events, and its own audit trail. It is the
**Identity-verification service** — a Platform service, not a staff/parent module.

---

## Physical model (TARGET — migrated at retirement, per the canonical-first rule)

```sql
-- Identity owns the verified fact (extends the frozen Identity aggregate — additive, not a rewrite).
ALTER TABLE identities ADD COLUMN nin                     TEXT;         -- encrypted at rest; never logged/returned
ALTER TABLE identities ADD COLUMN kyc_status              VARCHAR(20) NOT NULL DEFAULT 'unverified';
ALTER TABLE identities ADD COLUMN nin_verified_at         TIMESTAMPTZ;
ALTER TABLE identities ADD COLUMN reverification_required BOOLEAN NOT NULL DEFAULT FALSE;  -- review axis (decision 1), independent of status
ALTER TABLE identities ADD CONSTRAINT identities_kyc_status_chk
    CHECK (kyc_status IN ('unverified','pending','verified','rejected'));

-- Compliance owns the attempt history (append-only). NIN attempts only — no generic "verification kind"
-- abstraction today (decision 3); a future identity document (passport, …) is added when actually needed.
-- BVN is NOT here — it is organization/proprietor verification, a separate aggregate.
CREATE TABLE identity_verifications (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    identity_id   UUID NOT NULL REFERENCES identities(id) ON DELETE CASCADE,
    provider      VARCHAR(30) NOT NULL,          -- 'dojah' | 'stub' | …
    provider_ref  VARCHAR(120),                  -- external reference, for async resolution
    outcome       VARCHAR(20) NOT NULL,          -- 'pending' | 'succeeded' | 'failed'
    reason        TEXT,                          -- failure detail (never the credential)
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_identity_verifications_identity ON identity_verifications (identity_id, created_at DESC);
```
**Retired by this move (at migration time):** `staff_users.nin`, `staff_users.kyc_status`, `parents.nin`,
`parents.kyc_status`. **Untouched:** `schools.kyc_status` (school KYC — different aggregate).

> **Freeze note.** Adding KYC columns to `identities` is an *additive extension* establishing the canonical
> home for a fact Identity already logically owns — not a structural rewrite. It is precisely the
> "define canonical ownership" work the freeze intends, not the redesign it forbids.

---

## Commands / Queries (target API — owned by Compliance)

- **`SubmitNin(identityId, nin)`** → validates (11 digits), calls `IIdentityVerifier`, encrypts, appends an
  `identity_verifications` attempt, transitions `Identity.kyc_status` (subject to policy — below). Identity
  resolved from `_context.IdentityId` (no more `UserType`+`UserId` actor resolution) — **one path for every
  actor type**.
- **`GetVerificationStatus(identityId)`** → `kyc_status` + `reverification_required` + latest attempt
  summary (never the NIN).
- **`RequireReverification(identityId, reason)`** (ops / policy) → sets `reverification_required` **without**
  touching `kyc_status`.
- **(future async) `ResolveProviderCallback(provider_ref, outcome)`** → resolves a `pending` attempt.
- **Auth query (consumer):** read `identities.kyc_status` inline in the session lookup the request already does.

## Events — Identity publishes the facts, Compliance runs the workflow

These are **Identity** events, not Compliance events (your refinement): *Compliance performs the verification
workflow; **Identity** publishes the canonical facts about the person.* All `AggregatePastTense` (EDD-011),
auto-audited via the generic `IAuditableEvent` handler — no bespoke audit writes in Compliance.

**Published (by Identity):**
| Event | When | Typical consumers |
| --- | --- | --- |
| `IdentityVerificationSubmitted` | an attempt is recorded (status → `pending`) | audit |
| `IdentityVerificationSucceeded` | an attempt passed at the provider — the canonical "person is now trusted" fact (status → `verified`) | lift staff read-only mode; notify; anything gated on trust |
| `IdentityVerificationFailed` | an attempt failed at the provider (retryable) | notification (retry) |
| `IdentityVerificationRejected` | status → `rejected` | notification |
| `IdentityReverificationRequired` | `reverification_required` set | notification (ask the person to reverify) |

**Consumed:** `IdentityCreated` — a new Identity starts `unverified` (no attempts, no review).

In synchronous V1 a single `SubmitNin` emits `…Submitted` then either `…Succeeded` or (`…Failed` +
`…Rejected`); async or manual-review flows separate the attempt-level events (`Succeeded`/`Failed`) from the
status ones. The consumer that lifts read-only mode listens to **`IdentityVerificationSucceeded`**.

---

## Implementation plan (design-first; code later, at the actor-table retirement)

Per the canonical-first rule, **no code ships from this EDD until `staff_users`/`parents` retire** (so we
never dual-write KYC across old + new). When it does, the order is:

1. **Schema** — add the `identities` KYC columns + `identity_verifications`; `kyc_status` defaults `unverified`.
2. **Compliance service** — re-key off `_context.IdentityId`; write `identities` + append history; drop
   `ResolveActor()` (`UserType`/`UserId`). One code path for all actor types.
3. **Auth consumes** — repoint the `staff_users.kyc_status` reads (`StaffUserRepository`, `ContextMinter`,
   `UnifiedAuthService`, `StaffAuthService`) to `identities.kyc_status`. This is the one auth touch — a
   *simplification* (one read, one source), reviewed as such.
4. **Events** — publish the Identity verification events (Submitted / Succeeded / Failed / Rejected /
   ReverificationRequired); wire notification + audit.
5. **Retire** — drop `nin`/`kyc_status` from `staff_users` + `parents` (part of their table teardown).
6. **Verify** — build 0/0; full suite; migration on throwaway PG; concept grep: no person `kyc_status`
   read outside Compliance + the one Auth consumer.

## Resolved decisions

1. **Re-verification / expiry — verification does NOT expire.** Model a *review requirement* separate from
   *status*: `reverification_required` (bool) can demand another check without downgrading a `verified`
   identity. "Verified" is never retracted; only "reverify" is requested.
2. **Async providers — design async, implement sync.** V1 calls `IIdentityVerifier` (Dojah) synchronously and
   resolves inline; `provider_ref` + the attempt history already support a later `pending` → webhook →
   resolve path with no redesign.
3. **BVN — kept separate; no generic verification-kind abstraction.** NIN = Identity verification; BVN =
   organization/proprietor (banking/ownership) verification, a different aggregate. `identity_verifications`
   is NIN-only today; a future identity *document* (passport, …) is added when actually needed.
4. **Retry limits — policy, not domain.** The aggregate records attempts; a Verification Policy (max
   consecutive failures · cooldown · manual-review threshold) decides whether another is allowed. No retry
   count is hard-coded into the domain.
