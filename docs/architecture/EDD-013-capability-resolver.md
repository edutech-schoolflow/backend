# EDD-013 — Capability Resolver (B2b)

> **Golden rule: Authorization is derived, never embedded.** Authentication identifies the workspace
> (`context_id`); Authorization derives capabilities at request time, server-side, off the token.

**Status:** Implemented (B2b) · **Domain:** Platform Integration / Authorization

---

## 1. Why

Authentication is complete; Access Contexts are canonical (B2a). The last coupling was
**authorization**: the JWT carried the 13 `can_*` flags (computed by `StaffFeatureResolver` at
token-mint) and `[RequireCapability]` read those claims. That embeds authorization in the token —
so a permission change required re-minting, and every check trusted a stale snapshot. B2b moves
authorization **server-side, behind one API**, keyed on `context_id`. The JWT is untouched (the flags
stay as dead compatibility data until B2c).

## 2. The single authorization API

`ICapabilityResolver` (in `EduTech.Shared/Authorization`) is the **only** authorization API of the
platform. Every module — Admissions, Finance, Library, … — asks the same question and **never**
queries positions, permission templates, or overrides directly:

```csharp
await resolver.HasCapabilityAsync(contextId, Capabilities.Student.Read, ct);
await resolver.GetCapabilitiesAsync(contextId, ct);   // CapabilitySet
resolver.Invalidate(contextId);
```

One authoritative service means future capabilities — delegated administration, temporary/emergency
access, time-bound roles — change **one component**, not every module.

## 3. Resolution (byte-identical to today's decisions)

Input is **only** `context_id`. Consumers are fully actor-neutral.

```
context_id → access_contexts.type
   owner  → ALL capabilities                         (replaces the is_owner bypass)
   staff  → staff_affiliations(role, template) + permission_templates.features
            + staff_feature_overrides
            ⇒ effective flags (role default → template → override)
            ⇒ map enabled flags → capabilities (via CapabilityRegistry)
   parent / unknown → ∅
```

The staff path mirrors `StaffFeatureResolver.Resolve` exactly, so the granted set is identical to what
the JWT flags grant today. The pure rule lives in `CapabilityResolution.Resolve(...)`; the DB reads
live in `CapabilityResolver`.

**Honest scope note:** the resolver's *consumers* (the attribute, every module) are fully
actor-neutral — no `if staff/owner/parent`. The resolver's *internals* still branch on
`access_contexts.type`, because permission data lives in type-specific tables (owner has no template;
template/overrides sit on `staff_affiliations`). That internal branch collapses when template/overrides
migrate onto Employment/Position — a **later** refinement, not B2b (the foundation is frozen).

## 4. Enforcement

`RequireCapabilityAttribute` is now an `IAsyncAuthorizationFilter`: it reads the `context_id` claim,
resolves `ICapabilityResolver` from `RequestServices`, and allows iff `HasCapabilityAsync`. It reads
**no** capability flag, `is_owner`, or `user_type`. Owner resolves to all, parent/admin to ∅ — same
allow/deny as before (the parent "staff only" message becomes the generic 403).

## 5. Cache

`IMemoryCache`, key `cap:{contextId}`, TTL ~30s — checks are constant, changes are rare.
`Invalidate(contextId)` is called at the reachable permission-mutation sites (role change, staff
deactivate/reactivate). Event-driven invalidation (`EmploymentActivated` exists;
`PermissionTemplateChanged`/`CapabilityOverrideChanged` are reserved in EDD-011) is wired as those
events graduate; the short TTL covers the gap. Permission changes take effect **without re-minting**.

## 6. Success criteria (met)

JWT / frontend / login / refresh unchanged · `CapabilityResolver` is the single authorization source of
truth · `RequireCapability` no longer reads JWT flags · `StaffFeatureResolver` reduced to the
token-mint compatibility adapter · permission changes take effect without reissuing tokens · consumers
fully actor-neutral · the 13 flags remain in the JWT only as compatibility for B2c removal.

## 7. Non-goals (later)

No JWT change / flag-claim removal (B2c) · no login/refresh/middleware/frontend/`access_contexts`
change · no legacy-actor retirement (B2d) · no data-model change (template/overrides stay on
`staff_affiliations` for now).
