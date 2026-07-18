# Module Design Template

Every product module (Layer 3) answers these **before** any code. This keeps the architecture
consistent as the codebase grows and enforces the platform contract: a module depends on the platform
only through **published contracts** (Identity · Membership · Employment · Organization · Capabilities ·
Events) and platform **services** (Notification · Storage · Search · Audit · Calendar), and **never**
modifies the Foundation or reaches into another module's tables/repositories/aggregates.

> **Admissions is the reference implementation.** "How should I build a module in SchoolFlow?" →
> point at Admissions. It must demonstrate identity-first design, membership creation, event-driven
> workflow, capability authorization, the workspace model, platform notifications, audit, and clean
> aggregates.

Copy this file per module (e.g. `EDD-0xx-admissions.md`) and fill every section.

---

## 1. Purpose
The one job this module does. One paragraph.

## 2. Aggregate(s)
The domain aggregate(s) it owns, with invariants. (Owns its own tables — nothing else's.)

## 3. Database tables
New tables (migration numbers). FKs point only at the module's own tables and platform contracts
(`identities`, `organizations`, `memberships`, `employments`) — never another module's tables.

## 4. Commands
The write operations (intent → state change), each with its guard/validation.

## 5. Queries
The reads / read models it exposes.

## 6. Domain events
Events it **publishes** (canonical `AggregatePastTense` names — reserve in the Event Catalog) and
events it **consumes**. No cross-module calls that aren't events or published contracts.

## 7. Capabilities
The capabilities it gates on (via `[RequireCapability]` / `ICapabilityResolver`) and any new
capability keys it introduces (registered in `CapabilityRegistry`).

## 8. Workflows
The lifecycle/state machine and the event-driven steps.

## 9. External integrations
Third parties (payments, SMS, KYC, …) consumed **through platform services**, not directly.

## 10. UI surfaces
The screens/workspaces it adds (`/o/{slug}/…`), and which persona/context sees them.

## 11. APIs
The endpoints (route, verb, capability, request/response shape).

## 12. Reporting
Metrics/exports it needs, and the read models behind them.

## 13. Future extensions
Where it will grow — reserved but not built now.

## 14. Non-goals
What it explicitly does **not** own (and which module/service does).

---

### Platform-contract checklist (must all be "yes" before merge)
- [ ] No change to Identity / Membership / Employment / Organization / Access Context / Auth.
- [ ] Depends on the platform only through published contracts + services.
- [ ] Does not read another module's tables/repositories/aggregates directly.
- [ ] Authorizes via `ICapabilityResolver` only; no permission logic of its own.
- [ ] Vertical slice: Domain · Repository · Events · API · Frontend · Tests.
- [ ] Uses the frozen vocabulary (Identity · Organization · Membership · Employment · Position ·
      Access Context · Capability · Workspace) — no synonyms.
