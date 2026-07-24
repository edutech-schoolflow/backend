# EDD-015 — Canonical Authorization Model (Position / Employment permissions)

**Status:** Active. The prerequisite for **B2d Phase 1** (EDD-012). Continues **EDD-006** (flags → capabilities).

**Why this exists:** authorization state still lives on the **legacy actor** row. `staff_affiliations`
owns `permission_template_id` and `staff_feature_overrides`, so authorization is *not yet fully expressed
by the canonical aggregates* — and `CapabilityResolver` + the token mint must keep reading
`staff_affiliations` until it is. That is the real blocker to B2d's "no auth/authz code references a
legacy actor concept." This EDD moves permission ownership onto the aggregates that already exist.

## The model

Authorization is expressed by *who someone is* → *where they belong* → *what assignment they hold* →
*what that assignment allows* → *how it's customized*:

```
Identity → Membership → Employment → Position → PermissionTemplate → Employment overrides → Capabilities
```

- **Position** defines what a role *normally* grants — organizational policy.
  `positions.permission_template_id` (the role default; nullable — a position may grant nothing by default).
- **Employment** is *this person's assignment* and may override the default.
  `employments.permission_template_id` (nullable). (e.g. a Teacher acting as Principal gets an
  Employment-level override, no Position change.)

> **Effective permission template (the precedence rule — never leave it implicit):**
> ```
> effective_template = Employment.permission_template_id ?? Position.permission_template_id
> ```
> Employment is authoritative; Position supplies the default. Exactly one fallback, no deeper chain.
> Employment feature overrides then apply on top of the effective template (as `staff_feature_overrides`
> do today).
- **Employment overrides** — per-assignment feature tweaks, an **explicit table** (not columns / not a
  blob), so later `EmploymentPermissionGranted` / `Revoked` events map cleanly to rows:
  `employment_feature_overrides(employment_id, feature_key, enabled)`.
- **Role** needs no new storage — it is `Employment → Position.slug` (already canonical, 0046/0037).
- **Multiple employments** fall out naturally: Teacher + Sports-Coordinator = two employments, each with
  its own position/template/overrides. Membership stays singular; the assignment carries the duties.

**Overrides stay flag-native for now (feature_key), deliberately.** The DB permission model is flag-based
end to end — `permission_templates.features` is a `{flag: bool}` JSON, `staff_feature_overrides` is keyed
on `feature_key`. So `employment_feature_overrides` mirrors that (a clean 1:1 re-key onto Employment),
preserving behavior and staying consistent with templates. Renaming the flag layer to native capabilities
is the remaining **EDD-006** work (flag retirement) — a *separate* effort from legacy-**actor** retirement,
done after B2d so the two don't entangle.

## Target resolver

```
context_id (= access_contexts.id)
  → access_contexts.type + membership_id
  → active Employment (membership_id, status='active')
  → Position (role = slug) + PermissionTemplate (Employment.template ?? Position.template)
  → employment_feature_overrides
  → CapabilityResolution.Resolve(type, role, templateFeatures, overrides)
```

owner → all; parent/none → ∅ (unchanged). No `reference_id`, no `staff_affiliations`.

## B2d.1 — four additive stages (each its own commit; behavior-preserving until Stage 4 verifies)

- **Stage 1 — Canonical permission model (schema + backfill).** Migration `0058`: add
  `positions.permission_template_id`, `employments.permission_template_id` (nullable), and
  `employment_feature_overrides`. Backfill from `staff_affiliations` / `staff_feature_overrides` (1:1 via
  `staff_user → identity → membership → employment`). **No readers change** — the resolver/mint still read
  `staff_affiliations`. Verify on throwaway PG.
- **Stage 2 — Dual-write.** Every permission mutation (template assignment, overrides, role change) writes
  **both** the canonical tables and the legacy `staff_affiliations` / `staff_feature_overrides`. Behavior
  identical; the two stores stay in lockstep.
- **Stage 3 — Re-source reads (the legacy actor TABLES only).** `CapabilityResolver` and the mint stop
  reading `staff_affiliations` / `school_owners` / `parents`; they read the permission model + actor
  details through `AccessContext → Membership → Employment → Position` (owner/staff details via `schools`
  by `organization_id`). Legacy reads become **fallback only**. **The `reference_id` / `context_id`-claim
  flip is explicitly NOT bundled here** — `reference_id` is a column on the canonical projection, so the
  resolver/mint may keep using it transitionally as the context key; behavior changes follow a *proven*
  read-model change, not accompany it (see below).
- **Stage 4 — Verification.** Prove **identical** capability sets, authorization decisions, and mint output
  (canonical vs legacy path, across role-only / template / override cases; owner=all; parent=∅). Grep proves
  no auth/authz reader touches `staff_affiliations` / `school_owners` / `parents`.

**Deferred to a separate step (after Stage 3 is production-proven):** flip the JWT `context_id` claim to
`access_contexts.id` and repoint the resolver/mint context key onto it, so `reference_id` can be dropped.
Ordering is deliberate — resolver reads canonical → mint reads canonical → both proven in production →
*then* repoint the claim. Only after that do B2d.2 (coexistence) and B2d.3 (deletion) proceed.

## Non-goals

No flag→capability rename (EDD-006, later). No deletion of `staff_affiliations` / `reference_id` / actor
columns (B2d.2/.3). No payroll/leave/attendance on Employment (Workforce). No manager-cycle / org-unit FK
work. `user_type` unchanged.
