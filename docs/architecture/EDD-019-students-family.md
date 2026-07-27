# EDD-019 — Students & Family (the family-side canonical domain)

**Status:** DESIGN (in progress — no code until complete, per the canonical-first rule). The **last** of the
three canonical domains closing EDD-016; retiring it clears the way for the final legacy retirement.

> The dig confirms the hypothesis: **"parent" is not an aggregate — it is a role a person plays toward a
> child.** The center of this domain is the **Guardian Relationship** (Identity ↔ Child), not a `parents`
> account. This gives the platform its symmetry:
> ```
> Organization side:  Identity → Membership → Employment → Position
> Family side:        Identity → Guardian Relationship → Child Profile → Student (membership)
> ```
> Both start at **Identity**; one flows into *work*, the other into *family*.

---

## 1. Existing-state dig (what the runtime reveals)

| Legacy / current | What it really is | Canonical target |
| --- | --- | --- |
| `parents` (0004: `full_name`, phone, email, `password_hash`, `payment_pin_hash`, `nin`/`kyc_status`, `identity_id`) | a **login actor** = a person + parent-specific bits. `identity_id` (0036) already links it to Identity; `nin`/`kyc_status` already claimed by EDD-017. | **Identity** (person) + a thin **Parent Profile** (`payment_pin`, comms prefs). *No "parent account."* |
| `parent_children` (0022: `parent_id`, `child_profile_id`, `relationship` mother/father/guardian, `is_primary`) | the **guardian relationship** — but **thin**: relationship + primary only. | **Guardian Relationship** (the central aggregate) — grows real authority. |
| `child_profiles` (0022: name, dob, gender, medical, `parent_id` owner) | the **Child Profile** (a first-class non-Identity person, EDD-007). `parent_id` single-owner is **redundant** with `parent_children`. | **Child Profile** (drop the single `parent_id`; ownership = `is_primary` on the relationship). |
| `students` (0017 + `child_profile_id` 0022: school, class_arm, admission#, status) | the **Student Membership** (Child Profile × School). | **Student** = a Child-Profile membership (EDD-007). Mostly canonical already. |
| `payments.parent_id` (→ `parents`) + `parents.payment_pin_hash` | **fee responsibility / payer** | attaches to the **Guardian Relationship** (who pays for which child), payer resolved to an **Identity**. |

**Scattered surface today** (no "Family" context exists): parent profile in `EduTech.Auth/Parent/*`, payer in
`EduTech.Fees/ParentFee*`, child in `child_profiles`, plus `GuardianLinkedEvent` +
`EnsureIdentityOnGuardianLinked` (the platform *already* creates the Identity from the guardian relationship —
the canonical direction). EDD-019 consolidates these into one Family/Students domain.

**What does NOT exist yet (net-new domain — EDD-019 defines it):** custody, emergency-contact priority,
pickup authorization, consent, explicit fee responsibility. The current model only knows *relationship* +
*is_primary*.

---

## 2. Product

A guardian manages their children and their schooling from one place, on their single Identity.

- A person is linked to a child as a **guardian** (mother/father/guardian/…), with a **type** and authority
  (primary, custody, pickup, emergency priority). A child may have **many** guardians; a guardian **many**
  children; across **many** schools.
- A child becomes a **Student** at a school (enrolment) — the Child Profile's membership there.
- Guardians see the **Family Workspace**: their children, each child's schools, fees, reports, attendance,
  compliance visibility — all gated by the guardian relationship.
- **Fee responsibility** is explicit: a guardian relationship carries who pays for that child.

Unchanged: the child data, enrolment, the parent-facing views. What changes: *parent* stops being an account;
guardianship becomes a first-class, richer relationship; the payer stops being "the parent."

---

## 3. Domain model

> **Core invariant — a Guardian Relationship is the *only* source of family authority over a Child.**
> Family-workspace access, payment responsibility, consent, communication, pickup rights, emergency-contact
> standing, and compliance visibility **all derive from it**. No other aggregate may independently assert
> "this person is this child's parent." (The family-side twin of Workforce's "an Employment is the only
> source of organizational work.")

### The aggregates
- **Child Profile** — the child as a person (identity-less; a first-class member, EDD-007). Owns child-scoped
  facts (name, dob, gender, medical). **Drops** its single `parent_id` — ownership is `is_primary` on a
  relationship.
- **Guardian Relationship** *(central)* — `Identity (guardian) × Child Profile`, carrying the **authority**:
  `relationship_type`, `is_primary`, `has_custody`, `pickup_authorized`, `emergency_priority`,
  `fee_responsible`, and consent flags. This is the family-side analog of **Employment**.
- **Student** — `Child Profile × School` (the school membership: class/arm, admission#, status). Largely
  canonical already (`students.child_profile_id`).
- **Parent Profile** — a **thin** Identity-scoped projection for *guardian preferences* (`payment_pin`,
  communication prefs), **not** a person and **not** required. A guardian without preferences has no row.
- **Family Workspace** — a **view**, not a table: the transitive closure of a guardian's relationships →
  children → students. "Family" is the *projection*, so we don't invent a hard Family table that's really
  just the set of relationships.

### Why the Guardian Relationship, not a "Family" aggregate (decision — open for review)
Custody/pickup/emergency/consent are **per (guardian, child)** facts, not per-household — divorced parents,
a grandparent guardian, a child with guardians in two homes. Modelling them on the *relationship* handles all
of it; a "Family" table would force an artificial grouping the real world resists. So: **Child Profile is the
child; the Guardian Relationship is the authority; Family is a workspace view.**

---

## 4. Workflows
- **Add child** → create Child Profile → link the creating guardian (`is_primary`, `fee_responsible`).
- **Link / invite a co-guardian** → Guardian Relationship (+ Identity find-or-create, reusing
  `GuardianLinkedEvent → EnsureIdentityOnGuardianLinked`; claim-on-accept if unclaimed).
- **Enrol** → Student (Child Profile × School) — already the `StudentEnrolled` path (Admissions/Students).
- **Fee responsibility** → set `fee_responsible` on a relationship; the payer is that guardian's Identity.
- **Pickup / emergency / consent** → attributes on the relationship, edited by an authorized guardian.

## 5. Platform contracts consumed
Identity (find/create/claim), **Child Profile** (EDD-007), Membership/Student, Notification, Event Catalog,
and **Identity KYC** (EDD-017 — a guardian's verification is read, never performed here). Family **does not
know how KYC works** — it consumes `Identity.kyc_status`.

## 6. Events (EDD-011)
| Event | Owner | When |
| --- | --- | --- |
| `GuardianLinked` | Family (exists) | a Guardian Relationship is created → ensure guardian Identity |
| `ChildProfileCreated` | Family | a new Child Profile |
| `StudentEnrolled` | Students (exists) | Child Profile becomes a Student at a school |
| `FeeResponsibilityAssigned` | Family | a relationship is marked `fee_responsible` |
**Consumed:** `IdentityVerified` (EDD-017 — reflect guardian KYC). Auto-audited.

## 7. Capabilities
Family-workspace access is **relationship-derived**, not a school capability: a guardian may act on a child
**iff** an active Guardian Relationship exists (+ the specific authority flag for the action —
`pickup_authorized`, `fee_responsible`, custody for consent). School-staff access to family data stays
capability-gated (`students.view`, etc.).

## 8. Physical model (TARGET — at retirement)
```sql
-- Guardian Relationship grows real authority (was: relationship + is_primary only).
ALTER TABLE parent_children RENAME TO guardian_relationships;         -- name follows the concept
ALTER TABLE guardian_relationships ADD COLUMN guardian_identity_id UUID REFERENCES identities(id); -- was parent_id → parents
ALTER TABLE guardian_relationships ADD COLUMN has_custody        BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE guardian_relationships ADD COLUMN pickup_authorized  BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE guardian_relationships ADD COLUMN emergency_priority SMALLINT;      -- NULL = not an emergency contact
ALTER TABLE guardian_relationships ADD COLUMN fee_responsible    BOOLEAN NOT NULL DEFAULT FALSE;
-- (drop parent_id after cutover)

ALTER TABLE child_profiles DROP COLUMN parent_id;                     -- redundant with the relationship

-- Fees payer becomes canonical (the deferred EDD-016 item).
ALTER TABLE payments ADD COLUMN payer_identity_id UUID REFERENCES identities(id);  -- was parent_id → parents

-- Optional thin Parent Profile for guardian preferences (only if a guardian sets any).
CREATE TABLE parent_profiles (
    identity_id      UUID PRIMARY KEY REFERENCES identities(id) ON DELETE CASCADE,
    payment_pin_hash TEXT,
    comms_prefs      JSONB,
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```
**Retired by this module:** `parents` (person → Identity; pin/prefs → `parent_profiles`; NIN/KYC already
Identity via EDD-017), `child_profiles.parent_id`, and the deferred `payments.parent_id` +
`ParentFeeService`'s `IdentityId ?? UserId` fallback (payer resolves to the guardian **Identity**).

## 9. Commands / Queries
- `AddChild(guardianIdentityId, childDetails)` · `LinkGuardian(childId, identityOrPhone, type, authority)` ·
  `SetFeeResponsibility(relationshipId)` · `SetPickupAuthorized/SetCustody/SetEmergencyPriority(relationshipId)`.
- `EnrolStudent(childId, schoolId, …)` (existing path).
- `GetFamilyWorkspace()` → keyed on `_context.IdentityId` (**not** `parent_id`/`UserId`): the guardian's
  relationships → children → students → fees/reports.
- **Fees consumes:** payer = the `fee_responsible` guardian's Identity (removes the last Fees `UserId` read).

## 10. Implementation plan (design-first; code at the actor-table retirement)
1. **Guardian Relationship** — rename `parent_children → guardian_relationships`; `guardian_identity_id`;
   add authority columns; migrate `is_primary`/`relationship`.
2. **Child Profile** — drop `parent_id`; ownership reads from `is_primary`.
3. **Family Workspace + Parent auth** — repoint `EduTech.Auth/Parent/*` + the family queries onto
   `identities × guardian_relationships × child_profiles × students`, keyed on `IdentityId`; `parent_profiles`
   for pin/prefs.
4. **Fees payer** — `payments.payer_identity_id`; `ParentFeeService` resolves the `fee_responsible` guardian
   Identity (closes the deferred EDD-016 Fees item + the `IdentityId ?? UserId` fallback).
5. **Retire `parents`** — after the above; concept grep → 0.
6. **Verify** — build 0/0; suite; throwaway-PG migration; grep of the runtime for `parents`/`parent_id` = 0.

## 11. Open decisions (for review before code)
1. **Aggregate root** — Child Profile + Guardian Relationship central, **Family = view** (my lean, §3), vs a
   hard **Family** aggregate. Does anything need to be true per-household rather than per-relationship?
2. **Parent Profile** — thin projection (pin/prefs only, my lean) vs a fuller aggregate (payment methods,
   notification routing, saved beneficiaries)?
3. **Fee responsibility placement** — on the **Guardian Relationship** (per child, my lean) vs a Family-level
   payer? (Split custody often means "each parent pays for their child".)
4. **Consent model** — booleans on the relationship (photo, trips, medical) now, or a first-class Consent
   aggregate with audit/expiry? (Leaning simple flags for V1; grow later.)
5. **Emergency contacts** — always a guardian (relationship flag, my lean) vs allowing **non-guardian**
   emergency contacts (a grandparent who isn't a guardian) as their own light entity?
6. **`is_primary` vs `has_custody`** — are these distinct (primary = default contact; custody = legal), or
   collapse to one? (Leaning distinct.)
