# EDD-009 — Employment

**Status:** Sprint C2 implemented (data + domain + write-routing)
**Domain:** People · **Sacred Six** member
**Builds on:** EDD-007 Membership, EDD-008 Position

---

## 1. Definition

> **Employment represents an active or historical working relationship between a Membership and an
> Organization.**

Not "employee", not "staff", not "owner" — a *working relationship*. It references Membership
(belonging) and Position (role), and owns the working relationship **only**.

**Employment must remain boring.** If it ever accumulates payroll, leave, performance, attendance, or
recruitment, the battle is lost — those are Workforce-**business** modules that reference Employment,
never live inside it.

---

## 2. Invariants

1. Employment always belongs to exactly one Membership, and cannot exist without one.
2. Employment always belongs to exactly one Organization, and cannot exist without one.
3. Employment owns the working relationship **only** — never payroll, leave, attendance,
   performance, or recruitment.
4. Status is one of exactly **five**: `Draft · Pending · Active · Suspended · Ended`. No others, ever.
5. Status changes are business events (append-only in meaning); reactivating an Ended employment
   records the transition.
6. Position, manager, and org-unit are mutated **only** through the aggregate's intent methods
   (`AssignPosition` · `ChangeManager` · `MoveOrgUnit`), never by a service writing columns directly.
7. Manager (reporting) chains must be acyclic — `A→B→C→A` is invalid. *(Documented now; the self-loop
   is guarded in the aggregate; deep-cycle enforcement is a later sprint.)*

---

## 3. Where it sits — the composability chain

```
Identity → Membership → Employment → Position → Permission Template → Capabilities → Workspace
```

Everything composes: a person (Identity) belongs (Membership), works (Employment) in a role
(Position), whose template resolves what they can do (Capabilities) inside a Workspace. Employment is
one link — deliberately small — in that chain.

---

## 4. Schema (`employments`, migration 0046)

`membership_id → memberships` · `organization_id → schools` (→ organizations, Sprint D) ·
`position_id → positions` (nullable) · `organizational_unit_id` (nullable, FK deferred:
faculties/campuses/departments/houses/divisions) · `manager_employment_id → employments` (self) ·
`employment_type` · `status` · `started_at` · `ended_at`.

- **`UNIQUE (membership_id, position_id)`** — allows two concurrent jobs (Teacher **and** Dean) while
  preventing a duplicate identical job. `organization_id` is derivable from the membership, so it
  stays out of the key.
- **`employment_type`**: `full_time · part_time · contract · temporary · volunteer · intern ·
  consultant` (mirrors `EmploymentTypes`).
- **`status`**: the five, and only the five.

The canonical table is the "physical merge" migration 0038 anticipated: it is **backfilled from**
`staff_affiliations` (staff) and `school_owners` (owner), each joined to its membership via
`(identity_id, school_id, kind)`. Status map: `active→active · invited→pending · else→ended`
(a deactivated/resigned affiliation is `ended`, consistent with EDD-007 deactivate-ends-membership;
`suspended` is reserved for a future explicit leave-of-absence flow).

---

## 5. Aggregate & context

`EduTech.People/Domain/Employment.cs` — the aggregate with the five-status lifecycle
(`Activate/Suspend/End`, idempotent) + the mutation intent methods (invariant 6). Lives in the
**People** foundation context beside Position. `IEmploymentRepository` drives it:
`EnsureFromAffiliationAsync` / `EnsureFromOwnerAsync` (scoped one-row backfill),
`EndByAffiliationAsync`, and the `GetForMembership` / `ListForOrganization` reads. Auth and Workforce
route the live lifecycle (staff-accept, owner register+verify, staff deactivate/reactivate) through
this context — beside the EDD-007 membership writes — so the canonical table stays current.

*(Strangler note: `EnsureFrom…` reads the legacy silos to build the canonical row — a temporary
coupling removed when the silos retire.)*

---

## 6. Events — reserved, not yet built

The permanent vocabulary (built in the Event Catalog sprint):

```
EmploymentCreated · EmploymentActivated · EmploymentSuspended · EmploymentEnded ·
EmploymentTransferred · EmploymentPositionChanged · EmploymentManagerChanged
```

Decision recorded: the current Staff-owned `EmploymentStartedEvent` is **superseded by
`EmploymentActivated`, published by the Employment context** — the swap (and its audit/subscriber
wiring) lands with the Event Catalog. C2 leaves `EmploymentStartedEvent` in place.

---

## 7. Deferred (not designed out)

- **C5** — re-point readers off `staff_affiliations`/`school_owners`, then retire the silos.
- Building the employment events (→ Event Catalog).
- **Position history** (`EmploymentPositionHistory`) — the aggregate does not preclude it.
- `organizational_unit_id` population + FK, and manager-cycle enforcement (→ Organization sprint).
- No login/JWT/`access_contexts`/`school_owners`-retirement changes.
