# EDD-016 — Platform Convergence (Module Canonicalization)

**Status:** OPEN epic. Supersedes the tail of B2. This is **domain migration, not platform work** —
no new auth design, no JWT/resolver/mint changes, no new platform concepts.

## The reframe that opened this epic

Retiring the JWT compatibility claims surfaced a boundary:

> **`is_owner` was *authentication state*. `affiliation_id` and `user_id` are *business identities*.**

`is_owner` was a pure authz check with a canonical equivalent (`user_type == School`), so it was a claim
swap — retired in B2d. But `affiliation_id`/`user_id` are not really "claims to drop": the **business
data models themselves** are keyed on the legacy actor ids, and the token claim is merely how each module
reads them today. So the remaining work isn't finishing auth — **auth is finished** — it's making each
business module stop using legacy identities.

| Legacy id | Lives in (domain) | Canonical target |
| --- | --- | --- |
| `attendance.submitted_by_affiliation_id`, `class_teacher_affiliation_id` | Attendance | Employment / Membership |
| Grades teacher / `submitted_by` / `approved_by` | Grades | Employment / Membership |
| `audit.actor_id` | Audit | Identity |
| staff-invite `invited_by` | Workforce | Identity |
| Fees payer / operator ids | Fees | Identity / Membership |
| Compliance actor ids | Compliance | Identity |
| Parent `parent_id` lookups | Students (Family) | Identity / Membership |

## The platform is complete (what this epic does NOT touch)

Authentication and authorization are **closed** (EDD-012 §B2, EDD-015). Canonical, live, gate-proven:
Identity · Membership · Employment · Position · Organization · Access Context (`context_id =
access_contexts.id`) · Capabilities (server-side resolver) · slim JWT · identity+context refresh. The one
compatibility claim that *was* auth state — `is_owner` — is retired. No work in this epic changes any of it.

## The rule (proactive — the concept grep, enforced forward)

> **No module may introduce a new dependency on `affiliation_id`, `user_id`, or `reference_id`.**
> Every change must satisfy: no new actor-keyed columns · no new reads of the compatibility claims ·
> canonical identities only (`identity_id` / `membership_id` / `employment_id`).

This is the forward-looking equivalent of the retirement concept grep: stop the bleeding while the
existing readers migrate.

## Per-module migration pattern (each module is an ordinary sprint)

Pre-launch V1 (no production data) means each module is a direct re-key — no backfill/dual-write window:

1. **Identify** the module's actor-keyed columns + its reads of `affiliation_id`/`user_id`.
2. **Add** canonical columns (`employment_id` / `membership_id` / `identity_id`); migrate the actor-keyed
   ones over (additive migration; for V1, cut the columns directly).
3. **Re-source** the service reads onto the canonical claim (`membership_id`/`identity_id` from the token)
   instead of `_context.AffiliationId`/`_context.UserId`.
4. **Verify** — module tests green; an equivalence check where behavior must be identical.
5. **Drop** the module's legacy columns + its compat-claim reads.

When **every** module is done: drop the `affiliation_id`/`user_id` claims from the token, then drop
`access_contexts.reference_id`, then the final concept grep declares the legacy actor identity gone.

## Module order (value + proof first; mechanical last)

1. **Attendance** — heavily exercised, `submitted_by_affiliation_id`/`class_teacher_affiliation_id` is
   *exactly* the canonicalization to prove. Do it first to establish the pattern.
2. **Grades** — same shape (teacher / submitted_by / approved_by). Second proof.
3. **Workforce** — invites, employment/role changes. Lives next to Employment already.
4. **Fees** — payer/operator identities; mostly straightforward.
5. **Compliance** — actor identities; audit-style.
6. **Audit** — isolated + mechanical *by the time the pattern is established* (don't start here — it's the
   least valuable even though it looks easiest).
7. **Students** — whatever remains (parent-by-`parent_id` lookups).

Each is bounded, independently shippable, and free of platform risk.

---

## Sprint 1 — Attendance Canonicalization (the pattern-setter)

**Status: DONE — commit `5dec4ae`.** Build 0/0 · 522 tests green · migration `0001→0060` verified on
throwaway Postgres. Outcome + the one design surprise are recorded at the end of this section.

Scoped design (Product → Domain → Physical → Commands → Plan).

### Product
Staff mark student attendance for the arms they teach; the owner may mark any. Staff also check
in/out (staff attendance), with a manual override path. None of this changes — only the *identities*
behind "who teaches this class", "who submitted", "whose attendance", and "who overrode" become canonical.

### Domain
**Canonical key = `membership_id`, not `employment_id`** (decided). `affiliation_id` in these tables is
"the staff person at this school" for scoping (who teaches this class) and attribution (who submitted /
whose check-in) — attendance's logic never uses the *position/role*. `membership_id` is the exact 1:1
canonical equivalent of `affiliation_id` (staff-at-school) and is **already in the token** (B2c.1) — so no
cross-module `employments` lookup, no new claim. *overrider* → **Identity** (`identity_id`, also in token).
Owner-vs-staff scoping is untouched (`IsSchoolOwner = user_type == School`, B2d).

### Physical model (migration 0060 — V1 has no data, so add-canonical / drop-legacy directly)
| Table (module) | Drop | Add |
| --- | --- | --- |
| `attendance_records` (Attendance) | `submitted_by_affiliation_id → staff_affiliations` | `submitted_by_membership_id → memberships` (NULL = owner) |
| `classes` + `class_arms` (Students/Classes) | `class_teacher_affiliation_id → staff_affiliations` | `class_teacher_membership_id → memberships` |
| `staff_checkins` (Workforce/StaffAttendance) | `affiliation_id → staff_affiliations`, `overridden_by` (actor) | `membership_id → memberships`, `overridden_by_identity_id → identities` |

### File map (the real surface)
- `EduTech.Attendance/AttendanceRepository.cs` — `submitted_by`, `ClassTeacherAffiliationId` DTO,
  markable-arm scope (`class_teacher_affiliation_id = @AffiliationId`).
- `EduTech.Attendance/AttendanceService.cs` — `CurrentAffiliation()` → **`CurrentMembership()`**
  (`_context.MembershipId`); scope check + `submittedBy`.
- `EduTech.Students/Classes/ClassRepository.cs` — class/class_arm teacher **reads (JOIN staff_affiliations
  → memberships)** + **writes** (`UPDATE/INSERT class_teacher_*`).
  - **FE-AFFECTING (resolved):** the class-teacher assignment is an API contract, not server-only.
    `SetClassTeacherRequest` + `ClassTeacherResponse` carry `affiliation_id`, and the staff directory
    (`StaffDirectoryRow`) exposes `AffiliationId` — all three move to `membership_id`, and the frontend
    (staff-directory picker + class-teacher assign) sends/reads `membership_id`. So this sprint is
    **backend + FE contract**, which is exactly why it's the first convergence *sprint*, not a quick edit.
- `EduTech.Workforce/StaffAttendance/StaffAttendanceRepository.cs` + `StaffAttendanceService.cs` —
  `staff_checkins.affiliation_id` → `membership_id`; `overridden_by` = `_context.IdentityId`.
- Tests: AttendanceServiceTests, StaffAttendanceServiceTests, Class repo/service tests.

### Plan
1. Migration 0060: repoint the four column locations (memberships FK) + `overridden_by_identity_id`.
2. Classes: teacher read/write → `class_teacher_membership_id`; assign-teacher takes a `membership_id`
   (resolve from the staff directory).
3. Attendance service/repo: `CurrentAffiliation` → `CurrentMembership` (`_context.MembershipId`); scope +
   `submitted_by` on `membership_id`.
4. StaffAttendance service/repo: `membership_id` + `overridden_by_identity_id`.
5. Verify: module tests green; scoping equivalence (owner sees all arms; a teacher sees only arms whose
   `class_teacher_membership_id` is their membership) on throwaway PG.
6. Exit: Attendance + Classes-teacher-assignment + StaffAttendance make **zero** reads of
   `_context.AffiliationId` / `_context.UserId` and hold no `staff_affiliations` FK (concept grep → 0).

**Not in this sprint:** dropping the `affiliation_id`/`user_id` *claims* or `reference_id` — those wait
until *every* module is done (the claims still serve the not-yet-migrated modules).

### Outcome + the pattern-setting learning

Delivered as committed: `attendance_records.submitted_by_membership_id`, `classes`/`class_arms`
`class_teacher_membership_id`, `staff_checkins.membership_id` + `overridden_by_identity_id`; services
re-sourced onto `_context.MembershipId` / `_context.IdentityId`; the teacher-name JOIN moved
`staff_affiliations→staff_users` ⟶ `memberships→identities`; `MembershipActiveAsync` validates "active
staff" as *a membership with an active employment* (the projector's own definition); the staff directory
now exposes `membership_id` so the class-teacher picker can send it.

**The surprise (a rule for every later sprint): a shared column drags its other readers into the sprint.**
`class_teacher` is a **Classes** column, but **Grades** also reads it for grade-entry scoping. Dropping
`class_teacher_affiliation_id` would have broken Grades, so Grades' *class-teacher* scoping migrated in
this sprint too (`GradeService`/`GradeRepository` primary-tier reads → `class_teacher_membership_id` /
`@Mem`). Grades' *own* `submitted_by` and its *subject-teacher* path stay affiliation-keyed for the Grades
sprint. **Takeaway:** scope a convergence sprint by *column ownership*, and migrate **all** readers of a
migrated column together — never leave a dropped column with a stale reader in another module.

**FE contract that changed:** `SetClassTeacherRequest` / `ClassTeacherResponse` and the staff-directory
picker now carry `membership_id` (was `affiliation_id`). Acceptable pre-launch; the frontend updates
separately. This is the first convergence unit that touched an API contract, not just server internals.
