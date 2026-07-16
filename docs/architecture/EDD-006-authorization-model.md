# EDD-006 — Authorization Model

**Status:** Sprint A implemented (backend-only, non-breaking)
**Supersedes:** the ad-hoc `StaffFeatureFlags` permission model
**Owner:** Platform / Authorization

---

## 1. Purpose

SchoolFlow is moving from ~13 fixed boolean **feature flags** to an open, namespaced
**capability** model. This document is the definitive reference for how authorization works:
its concepts, the token contract, the migration in flight, and the rule that keeps the old
model from creeping back.

The change matters because we are about to build ~20 more domain modules (Admissions, Finance,
Workforce, Inventory, Library, Transport, Messaging, …). If each new module adds another boolean
flag, the JWT fills with flags and authorization becomes impossible to reason about as a set.
Capabilities scale; flags do not.

---

## 2. The engineering rule

> **No new feature may introduce a JWT feature flag. All authorization must be expressed as
> capabilities.**

`StaffFeatureFlags` and `[RequireFeature]` are legacy. They survive only as a migration
compatibility layer and are being removed sprint by sprint. New endpoints use
`[RequireCapability(Capabilities.…)]`.

---

## 3. Concepts

| Concept | Definition | Code |
| --- | --- | --- |
| **Capability** | The canonical unit of permission — a dotted `resource.action` key (`attendance.record`). | `Capabilities`, `CapabilityDefinition` |
| **Capability registry** | The authoritative catalog of every capability: display name, description, owning module, and (during migration) its legacy flag. Powers admin UI, audit, discovery. | `CapabilityRegistry` |
| **Role** | A named collection of capabilities — the default set a staff member gets. | `RoleCapabilities` |
| **Permission template** | A school-defined capability set that replaces role defaults for assigned staff. *(Still flag-based; migrates in a later sprint.)* | `PermissionTemplateRepository` |
| **Per-staff override** | A per-member grant/revoke that wins over template and role. *(Still flag-based.)* | `StaffFeatureOverrideRepository` |
| **Authorization policy** | The enforcement point: `[RequireCapability]` on an endpoint. Owners bypass; parents/platform-admins are rejected. | `RequireCapabilityAttribute` |
| **Resolver** | Computes a member's effective capabilities from role → template → overrides. *(Today: flag-space, output embedded in the token. Sprint B: capability-space, server-side.)* | `StaffFeatureResolver` |
| **Token** | The JWT. Today it carries the 13 `can_*` flag claims (unchanged contract). | `TokenVendor` |
| **Workspace** | The `/o/{slug}` organization context a session operates in. Sprint D exposes `workspace.capabilities` to the frontend. | — |

### 3.1 The bridge is inverted — the flag is a property of the capability

The capability is canonical. A legacy flag has **no independent existence**: it is one nullable
field, `LegacyFlag`, on a `CapabilityDefinition`. Enforcement and token minting read the flag
*through* the capability, never the reverse. When the JWT is slimmed (Sprint C) we null the
`LegacyFlag`s and the capabilities are otherwise unaffected.

```
RoleCapabilities.For(role)      // canonical: role → capabilities
        │  project each capability → its LegacyFlag  (CapabilityRegistry)
        ▼
StaffRoleFeatures.For(role)     // legacy shim: role → 13 flags  (derived, not authored)
        │  role defaults → template → overrides  (StaffFeatureResolver, flag-space)
        ▼
13 flag claims embedded in the JWT  (TokenVendor, unchanged contract)
        │  RequireCapability(cap) → LegacyFlagFor(cap) → check claim
        ▼
Endpoint authorized
```

---

## 4. Capability taxonomy

### 4.1 Implemented (Sprint A) — 1:1 with the 13 legacy flags

Every capability below maps to exactly one legacy flag, so all are enforceable from today's
token with no ambiguity.

| Capability | Module | Legacy flag |
| --- | --- | --- |
| `student.read` | Students | `can_view_student_records` |
| `attendance.record` | Attendance | `can_mark_student_attendance` |
| `grades.enter` | Grades | `can_enter_grades` |
| `grades.exam.submit` | Grades | `can_submit_exam_papers` |
| `classes.view_mine` | Classes | `can_view_my_classes` |
| `fees.manage` | Fees | `can_manage_fees` |
| `fees.invoice.view` | Fees | `can_view_invoices` |
| `admissions.manage` | Admissions | `can_manage_admissions` |
| `school.overview.view` | School | `can_view_school_overview` |
| `staff_attendance.board.view` | Workforce | `can_view_staff_attendance_board` |
| `permissions.manage` | Workforce | `can_manage_permissions` |
| `store.view` | Store | `can_view_store` |
| `store.manage` | Store | `can_manage_store` |

### 4.2 Target taxonomy (deferred to Sprint B)

Finer-grained capabilities the current token can't express — they require the server-side
resolver, which can grant a capability the JWT doesn't carry. Examples of the intended shape:

```
student.read           student.write
attendance.record      attendance.view
fees.invoice.view      fees.invoice.create      fees.invoice.approve
assessment.publish
```

Naming convention: `resource.action`, optionally `resource.sub.action`. A capability names a
*thing you can do*, never a screen or a role.

---

## 5. Enforcement semantics

`[RequireCapability(cap)]` (see `RequireCapabilityAttribute`):

1. Unauthenticated → `401`.
2. `is_owner = true` → **allow** (owners hold every capability implicitly).
3. `user_type` is parent or platform-admin → `403` (wrong portal).
4. Resolve `CapabilityRegistry.LegacyFlagFor(cap)`:
   - a real flag → allow iff that claim is `"true"`, else `403`;
   - `null` (a Sprint-B, flag-less capability) → `403` (deny; today's token can't grant it).

This is behaviorally identical to `[RequireFeature]` for the 13 mapped capabilities — the
migration changed the *language*, not the runtime decision.

**Not to be confused with** `FeatureGateAttribute` / `IFeatureFlagService`: that is the
per-school **module on/off toggle** (returns `503` when a module is disabled) and is unrelated
to staff permissions.

---

## 6. Roadmap

| Sprint | Scope | Breaking? |
| --- | --- | --- |
| **A** ✅ | Capabilities canonical in the backend: vocabulary, registry, `RoleCapabilities`, `[RequireCapability]`, all call sites migrated. Token & frontend untouched. | No |
| **B** | `CapabilityResolver` server-side (per-request cache, keyed identity + context). Grants flag-less capabilities. Templates/overrides move to capability-space. | No |
| **C** | Slim the JWT: drop the 13 flag claims from `TokenVendor`; null the `LegacyFlag`s; delete `StaffFeatureFlags` / `StaffRoleFeatures` / `[RequireFeature]`. | Token contract |
| **D** | Frontend consumes `workspace.capabilities` instead of JWT flags (~39 files). | Frontend, in lockstep with C |

The strangler invariant: every sprint removes legacy surface and adds no new dependency on it.

---

## 7. Files (Sprint A)

- `EduTech.Shared/Authorization/CapabilityDefinition.cs`
- `EduTech.Shared/Authorization/Capabilities.cs`
- `EduTech.Shared/Authorization/CapabilityRegistry.cs`
- `EduTech.Shared/Authorization/RoleCapabilities.cs`
- `EduTech.Shared/Auth/RequireCapabilityAttribute.cs`
- `EduTech.Shared/Constants/StaffRoleFeatures.cs` — now a projection of `RoleCapabilities`
- `EduTech.Shared/Constants/StaffFeatureFlags.cs`, `EduTech.Shared/Auth/RequireFeatureAttribute.cs` — legacy shims
- 13 controllers — `[RequireFeature]` → `[RequireCapability]`

Guardrail tests: role→flag projection parity (`RoleCapabilities` must reproduce the legacy
role defaults exactly), `RequireCapability` branch behavior, and registry integrity (1:1 flag
coverage, every constant registered once).
