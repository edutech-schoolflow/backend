# EDD-018 — Workforce (the organization-facing reference module)

**Status:** DESIGN COMPLETE. All five open decisions are resolved (see *Resolved decisions*); code is
deferred to the actor-table retirement, per the canonical-first rule. The second of the three canonical
domains closing EDD-016, and the **second reference module** after Admissions.

> Admissions proved **Platform → student-facing workflow**. Workforce proves **Platform → organization-facing
> workflow**: hiring, employment lifecycle, directory, profile, permissions — built entirely on
> **Identity → Membership → Employment → Position**, with **no `staff_users`/`staff_affiliations` in the
> runtime model.** When Workforce ships, those two legacy actor tables can be dropped.

---

## 1. Existing-state dig (what is actually there today)

### The two legacy tables and their canonical twins
| Legacy (today's runtime) | Canonical (already exists, backfilled) | Notes |
| --- | --- | --- |
| `staff_users` (global staff person: `full_name`, phone, email, `password_hash`, `kyc_status`, `nin`, `is_active`, lockout) | **`identities`** (EDD-001) — linked by `staff_users.identity_id` (0036) | `staff_users` is a **duplicate of Identity.** Name split (first/middle/last), KYC → EDD-017, lockout/verify already on Identity. |
| `staff_affiliations` (staff at a school: `role`, `position`, `employment_type`, `permission_template_id`, `status` invited/active/inactive/resigned, `invited_by`, `joined_at`) | **`memberships`** (EDD-007) + **`employments`** (EDD-009) — linked by `staff_affiliations.identity_id`/`position_id` (0037) | The belonging = Membership; the working relationship (role via Position, type, template, status) = Employment. |
| `staff_invite_tokens` (hashed token, phone, expiry, → `affiliation_id`) | *(no canonical home yet)* | The accept mechanism; re-keys onto the canonical invitation. |

**So the canonical graph is already built** (B1 memberships, C2 employments, EDD-015 templates on Employment,
B2a projector). Role change (`UpdateRoleAsync`, EDD-015 Stage 2) and status change already *sync* the
canonical tables alongside the legacy writes. Workforce's redesign is to make canonical the **only** model
and delete the legacy reads/writes.

### The product surface (today's endpoints — all keyed on `affiliationId`)
| Endpoint | Workflow |
| --- | --- |
| `GET /api/v1/school/staff` | Directory (list staff for the school) |
| `POST /api/v1/school/staff/invite` | Invite a staff member |
| `PATCH /api/v1/school/staff/{affiliationId}` | Change role/position |
| `POST …/{affiliationId}/deactivate` · `/reactivate` | Suspend / restore |
| `POST …/{affiliationId}/resend-invite` | New token + SMS for a pending invite |
| `GET /api/v1/staff/profile/me` | My own profile |
| `GET /api/v1/school/staff/permissions` | Staff with resolved permissions (the permissions UI) |

### The real domain rules to preserve (from `StaffInviteService`)
1. **Full-time exclusivity** — a person cannot be **full-time** at two schools; and a person already working
   anywhere cannot be added **full-time**. (`HasActiveFullTimeAsync` / `HasAnyActiveAsync`.)
2. **Same-school duplicate** — an `active`/`invited` slot in the same school is rejected.
3. **Re-invite a resigned slot** — an `inactive`/`resigned` slot is reused (not a new row).
4. **Invite token** — SHA-256-hashed, 72h expiry, one-time, tied to the invite; accept sets up the account.
5. **Invited-by** — recorded (`_requestContext.UserId` today → an **Identity**).

---

## 2. Product

A school builds and runs its staff, on top of the person's single global Identity.

- **Invite** a teacher/admin/bursar/… by name + phone + role + employment type; they get an SMS link, accept,
  set a password, and they're active — **reusing the Identity they may already have** (a person who is a
  parent at School A and a teacher at School B is *one* Identity, verified once).
- **Directory** of everyone at the school, with role(s), employment type, status, and verification state.
- **Change** a person's role/position; **suspend**/**restore**; **resign**/offboard; **transfer** later.
- A staff member sees **their own profile** and resolved permissions.
- A person may hold **more than one role** at a school (Teacher *and* Dean) — two Employments, one Membership.

Unchanged from today: exclusivity rules, the SMS invite + token, the permissions UI. What changes: the
*identities* behind all of it become canonical, and a person is never re-created per school.

---

## 3. Domain model

> **Core invariant — an Employment is the *only* source of organizational work.**
> **No runtime code may derive organizational state from Identity or Membership *directly*.** Every
> organization-specific concern — permissions, activation, invitation, directory, employment status,
> reporting relationships, workload, resignation — **must flow through an Employment.** The only legal path
> is `Identity → Membership → Employment → Position`; deriving "works here" from `Identity → Membership`
> alone (or from any parallel table) is forbidden. Permissions derive from Employment → Position; the
> directory derives from Employment; invitations *create* an Employment; activation activates it;
> resignation ends it. (Ownership is *governance*, not work — decision E.) This one rule keeps future
> features from reintroducing a parallel "staff" concept.

### The graph (canonical, already present)
```
Identity ──< Membership (Identity × School) ──< Employment (Membership × Position)
   │              │                                   │
  person       belongs to a school              a working relationship: role(=Position.slug),
  (EDD-001)    (EDD-007)                         employment_type, status, permission_template? (EDD-009/015)
```
- **Identity** owns the person (name, phone, email, password, KYC via EDD-017). Workforce **never** creates a
  "staff user" — it finds-or-creates an Identity by phone (claim-on-accept if new).
- **Membership** is the belonging edge (Identity ↔ School). One per person-school.
- **Employment** is the working relationship. Role = its Position's slug. **Multiple employments per
  membership** are allowed (`UNIQUE(membership_id, position_id)`), which is how one person holds two roles.
- **Position** is the job catalog (EDD-008); **permission template** resolves via Position (default) +
  Employment (override) (EDD-015).

### Decision A — **Invitation is an Employment in `Pending` status**, not a new aggregate
EDD-009 Employment already has `Draft/Pending/Active/Suspended/Ended`. "Invited" **is** `Pending`. So:
- **Invite** = find-or-create Identity (unclaimed if new) → ensure Membership → create Employment(`Pending`) →
  issue an invite token.
- **Accept** = claim the Identity (set password, verify phone) → Employment → `Active` (Membership active).
This avoids a parallel "invitation" table/aggregate; the invite token is the only new artifact, and it now
points at the **Employment** being accepted. (Mirrors Admissions' prospective→accepted and Identity's
claim-on-register.)

### Decision B — **`staff_users` is absorbed into Identity, with nothing left behind**
Every `staff_users` field has an Identity home: name (split), phone, email, `password_hash`, verification,
lockout, and — via EDD-017 — `nin`/`kyc_status`. Workforce reads `identities`. `staff_users` is dropped at
retirement; there is no "staff account" concept distinct from Identity.

### Decision C — **Full-time exclusivity is an Employment invariant**
The rule "at most one active full-time Employment per Identity across all schools" moves from an ad-hoc
`staff_affiliations` query to an **Employment-level check** at invite/hire (over the person's Employments).
It is a property of *employment*, not of a legacy table.

### Decision D — **API keys move from `affiliation_id` → `employment_id`**
The directory/role/suspend endpoints identify a staff engagement by its **Employment** (a person may have
two). The directory row carries `identityId` + `membershipId` + `employmentId`. **FE-affecting** (the staff
pages re-key), acceptable pre-launch — and the natural consequence of "a person can hold two roles."

### Decision E — **Owner is an Organization concept (governance), not an Employment**
Employment answers *"what work does this person perform?"*; ownership answers *"who controls this
organization?"* — different relationships. An owner may never teach, never appear in the directory, never
draw payroll. So ownership stays on **Organization** (`Organization.owner_membership_id`, EDD-010); an owner
*may optionally* also hold an Employment (an owner who is also Principal). This makes the eventual
`school_owners` retirement **clean** — replaced by a canonical Organization ownership model, not by
pretending ownership is a job. Workforce lists an owner only if they *also* hold an Employment.

### What Workforce owns vs. consumes
- **Owns:** the invitation workflow + token, the directory/profile *queries*, the staff lifecycle
  *orchestration* (hire/suspend/restore/resign/transfer/role-change), the permissions-view projection.
- **Consumes (platform contracts):** Identity (find/create/claim), Membership, Employment, Position,
  Capability (authz), Notification (SMS), Event Catalog, and **Identity KYC** (EDD-017 — reads
  `Identity.kyc_status`; never re-verifies).

---

## 4. Workflows

**Invite → Accept → Active**
```
Owner/admin ──invite(name,phone,role,type)──▶ [validate · exclusivity · duplicate]
   ├─ find-or-create Identity (by phone; unclaimed if new)
   ├─ ensure Membership (Identity × School)
   ├─ create Employment(Position=role, type, status=Pending)      ── event: StaffInvited
   └─ issue invite token (hash, 72h) ──SMS──▶ invitee
invitee ──accept(token, password)──▶ claim Identity · Employment→Active · Membership active
                                                                    ── events: StaffInviteAccepted, EmploymentActivated
```
**Role/position change** → `Employment.AssignPosition` (+ template resync) — the EDD-015 sync becomes the
*only* write. **Suspend/restore** → `Employment` `Active↔Suspended`. **Resign/offboard** → `Employment`→`Ended`
(Membership ended if no other active employment). **Transfer** (future) → end here, hire there (or
`MoveOrgUnit` once org units exist). **Resend invite** → new token for a `Pending` employment.

---

## 5. Events (Event Catalog, EDD-011 — `AggregatePastTense`)
| Event | Owner | When |
| --- | --- | --- |
| `StaffInvited` | Workforce | invite created (Employment `Pending` + token) |
| `StaffInviteAccepted` | Workforce | token accepted, Identity claimed |
| `EmploymentActivated` | Employment (exists, EDD-011) | Employment → `Active` |
| `EmploymentSuspended` / `EmploymentEnded` | Employment | suspend / offboard |
| `EmploymentPositionChanged` | Employment | role/position change |
**Consumed:** `IdentityVerified`/`IdentityVerificationSucceeded` (EDD-017 — reflect KYC in the directory).
Auto-audited via the generic `IAuditableEvent` handler.

## 6. Capabilities (authorization)
Existing `[RequireCapability]` gates, unchanged in spirit: `staff.view` (directory/profile), `staff.invite`,
`staff.manage` (role/position), `staff.deactivate`. Authorization keeps resolving server-side via
`ICapabilityResolver` (context → Employment → Position → template) — **no `staff_affiliations` read**, which
the canonical resolver already achieves (B2d).

## 7. Physical model (TARGET — at retirement)
No new business tables — Workforce runs on the canonical graph. Two changes only:
```sql
-- The invite token points at the Employment being accepted (was: affiliation_id).
ALTER TABLE staff_invite_tokens ADD COLUMN employment_id UUID REFERENCES employments(id) ON DELETE CASCADE;
-- (drop affiliation_id after cutover)

-- invited_by becomes an Identity (attribution), on Employment.
ALTER TABLE employments ADD COLUMN invited_by_identity_id UUID REFERENCES identities(id);
```
**Retired by this module:** `staff_users`, `staff_affiliations` (and their `nin`/`kyc_status`, which went to
Identity in EDD-017). Directory/profile become **queries** over `identities × memberships × employments ×
positions` (the `StaffDirectoryRow` already exposes `membership_id` from EDD-016 Sprint 1 — extend to
`identity_id` + `employment_id`).

## 8. Commands / Queries (owned by Workforce)
- `InviteStaff(schoolId, name, phone, role, employmentType)` → Identity+Membership+Employment(Pending)+token.
- `AcceptInvite(token, password)` → claim Identity, Employment→Active.
- `ResendInvite(employmentId)` · `ChangeRole(employmentId, role, position)` · `Suspend(employmentId)` ·
  `Restore(employmentId)` · `Resign(employmentId)`.
- `ListDirectory(schoolId)` → rows of `{identity, membership, employment(s), position, kyc_status}`.
- `GetMyProfile()` → the caller's employments at the school + resolved permissions (keyed on
  `_context.IdentityId`/`MembershipId`, **not** `AffiliationId`/`UserId`).

## 9. Implementation plan — staged slices (Design → Implement → Verify → Flip → Retire)
Built additive-first (the EDD-017 pattern): canonical columns/writes land reader-less, runtime flips to them,
then legacy is deleted — never a dual-write window.

1. **Slice 1 — canonical Workforce schema** ✅ **DONE (migration `0065`).** Adds `staff_invite_tokens.employment_id`
   (the invite token re-keyed onto the Employment being accepted) + `employments.invited_by_identity_id` (invite
   attribution as a canonical Identity), with backfills (affiliation→membership→employment; `invited_by`→identity
   via staff_users). **No runtime readers** → additive, no dual-write. V1 has no data so the backfill is a
   release no-op. Verified on throwaway PG: `0001→0065` clean + both backfills map correctly on a seeded graph.
2. **Slice 2 — invitation workflow on Employment.** *Dig finding: today the invite creates only a
   `staff_user` (no `identity_id`) + an `affiliation`; there is **no** Identity/Membership/Employment until
   **accept** — so this slice builds the canonical **onboarding path at invite**, not just a column wire.*
   **Approach (mirrors the existing `GuardianLinked → EnsureIdentityOnGuardianLinked` handler, respecting
   module boundaries — Workforce can't reference `EduTech.Identity` directly):** Workforce raises
   **`StaffInvited`**; an Identity-context handler runs `IIdentityRepository.EnsurePendingAsync`(Identity) +
   `IMembershipRepository.EnsureActiveAsync`(Membership, `staff`) + creates **Employment(`Pending`)** (a new
   repo method — the current `EnsureFromAffiliationAsync` hardcodes `'active'`) + links `token.employment_id`.
   **Accept** claims the Identity + `Employment(Pending)→Active` (the accept's `EnsureFromAffiliationAsync`
   upsert already flips `ON CONFLICT DO UPDATE status='active'`). Full-time exclusivity becomes an Employment
   check. **Risk is contained** — additive dual-write; the legacy onboarding path is unchanged (Slice 3 reads
   are still legacy), so a canonical-side bug cannot break onboarding. Mirror legacy until cutover.
3. **Slice 3 — runtime reads.** Directory / profile / employment / role lookups read
   `identities × memberships × employments × positions` (canonical becomes authoritative); the last
   `_context.AffiliationId`/`UserId` readers (`StaffProfileService`) move to `IdentityId`/`MembershipId`.
4. **Slice 4 — remove dead plumbing.** Delete the now-unused `staff_affiliations`/`staff_users` repo methods,
   joins, DTO fields, adapters. No behavior change.
5. **Slice 5 — retirement.** Drop `staff_users` + `staff_affiliations` (+ mirrors + compat code); concept grep
   → 0. **EDD-017 Slice 5 happens here** — the staff KYC mirror disappears with `staff_users`.

## 10. Resolved decisions
| # | Decision | Resolution |
| --- | --- | --- |
| 1 | Invite state | **`Pending`** — an invite is an organizational action (the school has decided to hire; the Employment exists; the person hasn't accepted). Not `Draft` (which implies indefinite editing). The token claims the pending Employment. |
| 2 | Membership timing | **Create at invite-time** — an Employment belongs to a Membership, so if the Employment exists the Membership must too. Stable graph from the start; accept only links the Identity and flips Employment → `Active`. (Mirrors Admissions: create the relationship first, activate later.) |
| 3 | Directory | **One person, Employments nested** — the canonical owner is the Identity; a person may be Teacher + ICT Coordinator + Club Adviser (three Employments, one human). The UI reflects the person; queries may still page by Employment internally. |
| 4 | Transfers | **Defer** until Organization Units exist — don't invent OUs early. For V1 a transfer is *End Employment A → Create Employment B*; when the OU module lands, Workforce consumes `MoveOrgUnit`. |
| 5 | Owner | **Organization concept, not an Employment** (decision E) — ownership is governance, not work; it stays on Organization, and an owner may *optionally* also hold an Employment. |
