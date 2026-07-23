# EDD-008 — People Domain (Position + Employment)

**Status:** DRAFT — design/deliverables for review (no code yet)
**Principle:** make the data model canonical first; keep foundation aggregates small.
**Sequence:** Sprint C. Revised order — **Position → Employment** (Position first).

---

## 1. Reframe: not "Employment extraction" — the People Domain

Employment is not a person, and not belonging. It is *one kind* of relationship. The People
domain is larger than Employment, and every branch is just a specialization of Membership:

```
                 Identity
                     │
                Membership            (I belong)
                     │
     ┌───────────────┼───────────────┬─────────── …
     │               │               │
 Employment       Student          Parent          Governor · Vendor · Alumni …
 (I work here)  (I learn here)  (my child is here)
     │               │
  Position       Child Profile
```

Employment stops being special — it is the "I belong **and I work here**" specialization.
`Student` is already `students`/`child_profile` (EDD-007); `Parent` is a membership kind. Sprint C
adds the **working** branch: **Position** and **Employment**.

---

## 2. Already in the schema (recognition, not greenfield)

| Concept | Table | Status |
| --- | --- | --- |
| Position | `positions` (0037) — `school_id` NULL = platform-global, else org-owned; slug, name, is_academic; ~18 seeded incl. `owner` | ✅ exists |
| Employment (staff) | `staff_affiliations` (+ `position_id`, 0037) | ✅ exists, becomes Employment |
| Employment (owner) | `school_owners` (+ `position_id`→'owner', 0038) | ✅ exists, becomes Employment(position=owner) |

So Sprint C **promotes** existing tables into aggregates; it does not invent them.

---

## 3. Position aggregate

Position is org-owned (with platform-seeded defaults) and deserves its own aggregate — it is the
hinge between Organization, Employment, and Capabilities, not a lookup table.

```
Organization ──< Position >──< Employment
```

- Fields: `id, organization_id (nullable = global default), slug, name, is_academic, status`.
- **Owner is a Position, not a table or a role.** Ownership can transfer: owner retires →
  employment ends → a new employment (position=owner) begins, and nothing structural breaks.
- **Capabilities wiring (ties to EDD-006):** `Position → PermissionTemplate → Capabilities`. The
  staff `role` string becomes a Position; the capability set is resolved from the Position's
  template. (Actual resolver re-point is later auth work — Sprint C only makes Employment reference
  Position.)

Then Workforce stops having Staff/Teacher/Owner "types" — they are all Employments with different
Positions. The People UI counts positions (Principal 2 · Teachers 42 · Finance 5 · …).

---

## 4. Employment aggregate — deliberately SMALL

Employment answers exactly one question: *what is this person's working relationship with this
organization?* It references Membership (belonging) and Position (role) — it does not contain them.

**Owns:** `employer (organization) · worker (identity/membership) · position · department ·
reports_to · employment_type · start · end · status`.

**Does NOT own** (separate domains — Employment must not know they exist):
`salary/payroll → Finance · leave → Workforce Ops · performance → Performance · recruitment →
Recruitment · attendance → Attendance · training → Training`.

Keeping it small is what keeps it stable and freezable (it is one of the Sacred Six).

**Lifecycle:** `Draft → Pending → Active → Suspended → Ended`.

**Foundation vs Business:** Employment is **foundation**; Workforce (recruitment, leave,
performance, attendance, payroll, training) is **business** built on top. Employment ⊂ platform;
Workforce ⊂ domain.

---

## 5. Sprint C deliverables (define before code)

- **C1 — Position aggregate** (promote `positions`): aggregate + repo; org-owned + global defaults.
- **C2 — Employment aggregate** (the small working relationship above), referencing membership_id +
  position_id.
- **C3 — Employment lifecycle**: Draft → Pending → Active → Suspended → Ended.
- **C4 — Employment events**: `EmploymentCreated · EmploymentActivated · EmploymentTransferred ·
  EmploymentEnded · PositionChanged · ManagerChanged`. (`EmploymentStartedEvent` already exists —
  reconcile/rename into this set.)
- **C5 — Projection updates**: readers of `staff_affiliations` begin reading `employment` through
  projections (incremental; no big-bang rename).
- **C6 — No login / JWT / AuthContext changes.** That is B2. Owner-as-Position is the *model*;
  physically retiring `school_owners` is staged later (it is load-bearing for auth/KYC/registration).

Order within C: **Position first**, then Employment (Employment references Position).

---

## 6. Revised roadmap

```
Membership ✅ (B1)
      ↓
Position            ← NEW: gives Organization something real to own, keeps Position from
      ↓               decaying into a lookup table, and aligns with capability-driven authz
Employment
      ↓
Organization        (Positions · Departments · Campuses · Sessions · Calendars)
      ↓
Event Catalog
      ↓
Authentication Finalization (B2: access_contexts projection + slim JWT + capability resolver)
```

Without Position, Organization is mostly a renamed `schools` table. With Position, Organization
becomes a real domain — which is why Position comes before both Employment and Organization.

---

## 7. Non-goals (deferred)

- Login / JWT / `access_contexts` changes → B2.
- Physical retirement of `school_owners` / `staff_affiliations` → staged strangler follow-ups as
  readers move onto Employment.
- Payroll, leave, performance, recruitment, attendance, training → their own Workforce-business
  modules; Employment stays ignorant of them.
- `schools` → `organizations` rename → Sprint D.
