# EDD-007 — Membership Model

**Status:** DRAFT — canonical data model for review (no code yet)
**Principle:** make the data model canonical first; code follows.
**Sequence:** Sprint B of the platform roadmap (Membership) — Employment (Sprint C) builds on this.

---

## 1. Purpose & the canonical rule

Define **Membership** as the canonical **organizational belonging edge**.

> **Membership is the universal organizational belonging edge. The *member* may be an Identity
> (adults and authenticated students) or a Child Profile (students without identities).**

Belonging is independent of *acting*. Adults and older students act, so they belong **as an
Identity**. A three-year-old in Nursery 1 genuinely belongs to the school but has no phone, email,
or login — they belong **as a Child Profile**. Forcing every member to be an authenticated identity
would break on that child. So the subject of a membership is polymorphic by design; that is the one
deliberate exception to "everything hangs off Identity," and it is what makes the model correct.

Membership becomes one of SchoolFlow's core, frozen aggregates. Employment, Enrolment, and Access
Context all build on it.

---

## 2. The two subjects

| Member subject | Who | Physical edge (already exists) |
| --- | --- | --- |
| **Identity** | parent · staff · owner · vendor · governor · pta · volunteer · alumni · authenticated student | `memberships` (0037), identity-keyed |
| **Child Profile** | students without a login (nursery/primary, and any un-claimed student) | `students` (0017 + `child_profile_id` in 0022) — this *is* the student membership |

A Child Profile may later gain an Identity (secondary/university, or a parent claiming on their
behalf) via an optional link — nothing else changes.

---

## 3. Canonical model — mostly already built

The decade-model is ~80% present in the schema. This sprint **recognizes and names** it, and fills
the adult-membership gaps; it does not invent new tables.

```
                       Organization (schools)
                              ▲
             ┌────────────────┴─────────────────┐
     Identity ──< memberships >           Child Profile ──< students >──  (= student membership)
     (adults +      kind: parent|staff|      (child_profiles)   status: active|withdrawn
      auth students) owner|vendor|governor|                     admission date, class arm
                     pta|volunteer|alumni
                              │                        │
                    (kind=staff) → Employment          └─ Enrolment (student_enrollments, per year)
                       [Sprint C: employments.membership_id]
                              │
     ChildProfile ─< parent_children >─ Parent Identity        (= Guardian Relationship, exists 0022)
     ChildProfile ─< child_identity_links >─ Identity          (optional, NEW/deferred — see §5)
```

Mapping to existing tables:

| Canonical concept | Table | Status |
| --- | --- | --- |
| Child Profile (aggregate) | `child_profiles` (0022) | ✅ exists |
| Guardian Relationship | `parent_children` (0022) | ✅ exists |
| **Student Membership** | `students` (0017/0022) | ✅ exists — child_profile_id + school_id + status |
| Enrolment (academic, per year) | `student_enrollments` (0033) | ✅ exists |
| **Adult Membership** | `memberships` (0037) | ✅ exists — but `kind` too narrow + staff/owner not backfilled |
| Employment (specialization) | `staff_affiliations` → `employments` | ⬜ Sprint C (`membership_id`) |
| Child → Identity link | `child_identity_links` | ⬜ new, deferred (§5) |

**Specializations reference the membership; they never duplicate the belonging edge.** Employment
(staff) will carry `membership_id`; Enrolment is the student membership's per-year academic placement;
`access_contexts` projects *from* memberships + student memberships + employments, retiring its
references to the legacy silos. Each conversion shrinks `parents` / `staff_affiliations` /
`school_owners` — the strangler bar goes down.

Lifecycle events (feed the Sprint E Event Catalog): `MembershipCreated`, `MembershipActivated`,
`MembershipEnded` (adult); the student edge already emits admission/withdrawal events.

---

## 4. Decision — RESOLVED

Student is **not** an identity-based membership. **Child Profile is a first-class aggregate**, and
the student membership references the Child Profile (it already does, as `students.child_profile_id`).
Identity is an *optional capability* attached later, never a prerequisite for being a student.

Rejected: making `memberships` polymorphic with a nullable `identity_id`. Keeping the identity-based
`memberships` table clean and the child-based `students` table separate — two physical edges, one
conceptual Membership — is simpler and truer than a nullable union column.

Future-proof by construction: nursery students never have identities, university students almost
always do — both are just a Child Profile with (or without) a linked Identity. The model doesn't
change across the Nursery → Primary → Secondary → University range.

---

## 5. Migration sketch (additive, idempotent) — this sprint is light

Next migration number: **0045**. The heavy lifting was already done in 0017/0022/0033/0037.

1. **Widen `memberships.kind`** to the canonical adult set (`parent, staff, owner, vendor, governor,
   pta, volunteer, alumni`); keep `UNIQUE(identity_id, school_id, kind)`.
2. **Backfill adult memberships** from the silos (idempotent): `staff` from `staff_affiliations`,
   `owner` from `school_owners`. (Parent memberships were already backfilled in 0037.)
3. **Name the student edge**: document/verify `students` as the Student Membership (no structural
   change required). Optional later cleanup: move denormalized child fields
   (`first_name`/`dob`/`gender`) off `students` onto `child_profiles` — **deferred**, not in 0045.
4. `child_identity_links` (child_profile_id, identity_id, linked_at) — **deferred** until student
   login is a real feature; nothing needs it yet.
5. No renames, nothing deleted; legacy silos keep working.

Domain code this sprint: a `Membership` aggregate over the adult edge + a thin read of the student
edge; re-point `access_contexts` writers to derive from memberships. Employment stays in Sprint C.

---

## 6. Non-goals (explicitly deferred)

- Employment aggregate / `employments.membership_id` → Sprint C.
- `schools` → `organizations` rename → Sprint D (`memberships.organization_id` re-points then).
- `child_identity_links` and the `students`→`child_profiles` field normalization → later.
- Retiring `parents` / `staff_affiliations` / `school_owners` → as consumers move onto memberships.
- Access Context stays a lightweight read model (EDD-003); it gains membership sources, it does not
  become a rich domain.
