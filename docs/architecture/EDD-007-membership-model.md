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

## 5. Sprint split — data migration vs authentication architecture

Sprint B crosses a boundary that deserves two levels of care, so it is split:

### B1 — Membership becomes canonical  (data layer; low risk; `access_contexts` untouched)

1. ✅ **Migration `0045`** — widen `memberships.kind` to the adult set (`parent, staff, owner,
   vendor, governor, pta, volunteer, alumni`); backfill `staff`/`owner` from the silos
   (`parent` already backfilled in 0037). *Applied + verified against a throwaway Postgres.*
2. ✅ **Live write completion** — the staff-accept and owner-register flows now write their
   `staff`/`owner` membership inline (mirroring the existing parent write), so the canonical edge
   stays complete for new records. *Verified.*
3. ⬜ **Introduce the `EduTech.Membership` bounded context** — the aggregate + repository that owns
   adult membership lifecycle (create → active → ended). Adult lifecycle operations read and write
   **Membership as the source of truth**; the write logic begins moving **out of**
   `AuthContextRepository` (Auth) into this context.
4. Student edge = `students` (no structural change). `child_identity_links` and the
   `students`→`child_profiles` field normalization remain **deferred**.

At the end of B1, Membership is canonical, but authentication still reads the legacy projection.

### B2 — Rebuild the authentication projection  (login pipeline; high risk; plan-first)

Only after B1:

1. Redesign `access_contexts` as a **projection of Membership** (later + Employment + Guardian):
   `context_id · membership_id · organization_id · kind · role · status` — **no** `owner_id`,
   `parent_id`, or `affiliation_id`.
2. Move token minting / login onto the projection. Target JWT:
   `identity_id · membership_id · context_id · organization_id · role · capabilities` (everything
   else is loadable).
3. Verify the projection can be **fully rebuilt from canonical data** (drop → rebuild from
   memberships → done).

The login pipeline is `Membership → Access Context → Authentication → Workspace`; a mistake there
means people can't sign in. Hence B2 gets its own EDD/plan and is not bundled with B1.

---

## 6. Invariants & boundaries

- **Access Context is a projection, not canonical state, and not an aggregate.** Like a search index
  or dashboard summary, it is disposable and regenerable from Membership/Employment/Guardian. It is
  therefore **not** one of the sacred tables.
- **Authentication consumes the projection; it does not build it.** The projection is owned by a
  `ContextProjection` derived from Membership — `Membership → ContextProjection → Authentication` —
  so Auth stays focused on sessions and tokens, not membership rules. (`AuthContextRepository` does
  not own this logic long-term.)
- **The strangler invariant for auth:** *Authentication may consume canonical domain models or
  projections derived from them, but must never depend directly on legacy actor tables.* If any auth
  flow still needs `school_owners`, `staff_affiliations`, or `parents`, the strangler isn't finished.

**The Sacred Six** (frozen platform tables): `identities`, `organizations`, `memberships`,
`employments`, `capabilities`, `permission_templates`. Note `access_contexts` is **not** here — by
design, it is regenerable.

---

## 7. Non-goals (explicitly deferred)

- Employment aggregate / `employments.membership_id` → Sprint C.
- `schools` → `organizations` rename → Sprint D (`memberships.organization_id` re-points then).
- `access_contexts` redesign + token/login re-point → **Sprint B2** (its own plan).
- `child_identity_links` and the `students`→`child_profiles` field normalization → later.
- Retiring `parents` / `staff_affiliations` / `school_owners` → as consumers move onto memberships.
