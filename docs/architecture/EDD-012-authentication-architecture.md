# EDD-012 — Authentication Architecture

**Status:** Specification (defines the target the B2 sprints implement toward)
**Domain:** Platform Integration
**Thesis:** Authentication owns nothing. It **projects** the platform:
`Identity → Membership → Employment → Organization → Access Context → JWT`.

> **The contract for the entire authentication system:** `access_contexts` is **not** a domain model.
> It is a **disposable projection** built entirely from canonical aggregates (Identity, Membership,
> Employment, Organization). It may be **deleted and rebuilt at any time without loss of business
> data.** If that is ever untrue, the projection is wrong — not the aggregates.

> **The token invariant (the defining rule of the redesign): the JWT never identifies a business
> actor.** It identifies only the authenticated **Identity**, the entered **Context**, the current
> **Organization**, and the current **Membership**. Business actors — Owner, Staff, Parent, Student —
> are **derived** from canonical aggregates, never encoded as token identity. There is exactly one
> authenticated thing: an **Identity**. Everything else is a context. The pipeline is therefore
> `Authentication → Identity Token → Access Context → Authorization` — there is no "owner
> authentication", "staff authentication", or "parent authentication"; those concepts do not exist.

This is the definitive spec for the login pipeline. It documents the pipeline as it **is** today and
the **target** it converges to across B2a–B2d, so that afterwards Authentication is as stable as
Identity itself.

---

## 1. Vocabulary

- **Identity** — the global person (phone-keyed). Authentership: *who is this person?*
- **Identity Session** — an authenticated session **not yet in any workspace**. Today: the
  `IssueIdentity` token (`identity` user_type, `identity_id` + phone, opens no portal). Issued when a
  person has **0** contexts (onboarding hub) or **many** (must pick one). It proves *authentication*,
  not *authorization to a workspace*.
- **Access Context** — one entry of "this identity may operate in this workspace as X". Today a row in
  `access_contexts` (`identity_id`, `type` owner|staff|parent, `reference_id` = **legacy actor id**,
  `organization_id → schools`, `status`). It is a **projection / read model** (EDD-007) — regenerable,
  never canonical state, never one of the Sacred Six.
- **Context Session** — an authenticated session **inside a workspace**: the per-context token minted
  when an identity enters a context.
- **Workspace** — the entered organization surface (`/o/{slug}`), scoped by the Context Session.

---

## 2. Login lifecycle (password → workspace)

```
POST /api/v1/auth/login  (phone + password)
   → verify Identity password (lockout/attempts on the identity)
   → build the Access Context list for the identity
        ├─ 0 contexts  → Identity Session + onboarding hub (create/claim an org)
        ├─ 1 context   → auto-enter it
        └─ many        → Identity Session + context picker → POST /select-context
   → EnterContext(chosen): mint the Context token + a rotating refresh token
   → client lands in the workspace (/o/{slug}) or the parent home
```

`EnterContext` today branches by context type — owner → `IssueSchoolOwner`, staff →
`IssueStaffScoped` (after resolving features), parent → `IssueParent` — each also issuing a refresh
token keyed on the **legacy** actor (`AuthActorTypes.SchoolOwner/Staff/…`, actorId = the silo id).

**Context selection** is therefore: login enumerates contexts; the client either auto-enters (1),
hits the onboarding hub (0), or calls `select-context` with a `contextId` (many).

---

## 3. The JWT

### Today (per-portal, actor-centric)
- **Per-portal signing keys** (`school`/`staff`/`parent`/`identity`/`platformAdmin`) and a per-portal
  `user_type`.
- References the **legacy actor id**: `ownerId` / `staffUserId` + `affiliationId` / `parentId`.
- The staff scoped token **embeds the 13 feature flags** (`features` dict).
- `identity_id` and `context_id` are **already** present as claims (the migration threaded them) —
  but `context_id` today equals the legacy actor id (owner_id / affiliation_id).

### Target (slim, context-centric) — B2c
```
identity_id · membership_id · context_id · organization_id · role
```
- **No legacy actor ids.** **No feature flags.** **No capabilities.** One signing key, one `user_type`.
- **Capabilities are deliberately NOT in the token.** The server is authoritative; serializing
  hundreds of capabilities into every token is wrong. Instead `context_id → CapabilityResolver`
  resolves them per request — so a permission change takes effect **immediately, without re-minting**.

---

## 4. Capability resolution

### Today
`StaffFeatureResolver.Resolve(role, templateFeatures, overrides)` computes the effective 13 flags at
**context-entry** (role defaults → school permission template → per-affiliation overrides), baked into
the staff JWT. Owners bypass (`isOwner`). `[RequireCapability]`/`[RequireFeature]` read the flag claims
(EDD-006 Sprint A moved the *language* to capabilities while keeping the flag bridge).

### Target — B2b
A server-side `CapabilityResolver`, keyed on the request's `context_id`, **completely actor-neutral**:
```
context_id → Access Context → Membership → Employment → Position → Permission Template → Capabilities
```
with a per-request cache. `[RequireCapability]` asks the resolver, not the token. The resolver knows
**nothing** about Staff / Owner / Parent / Teacher / Bursar / Principal — those are emergent from
Position + Membership, never branched on. The 13 flag claims disappear from the JWT (B2c). Owner-bypass
becomes a capability set on the owner Position.

> **Boundary invariant: Authentication must never resolve permissions.** Authentication ends at
> *issue token → context_id*. Everything after that — turning `context_id` into capabilities — is
> Authorization. No capability, feature flag, or permission is ever written into the token or resolved
> by the auth layer. This prevents the slow rot of "let's just put this permission in the token."

---

## 5. Silent refresh

- A refresh token is issued alongside every access token, **rotating** with a **family** (reuse of a
  rotated token invalidates the family — theft detection). Refresh resolves actor → identity and
  re-mints the current context token.
- **Today** the refresh is keyed on `(legacyActorType, actorId)`; refresh paths call
  `GetIdentityIdForActorAsync` to recover the identity.
- **Target** (B2c/d): refresh is keyed on `(identity_id, context_id)` — no legacy actor. Silent
  refresh re-mints the slim context token; because capabilities are resolved server-side, a refresh
  automatically reflects the latest permissions.

---

## 6. `/identity/me` vs `/context/me`

- **`/identity/me`** (+ `/identity/home`) — the **Identity Session** view: who the person is and the
  set of contexts/family they can enter. School-agnostic. Answers *"who am I, and where can I go?"*
- **Context-scoped `/me`** — today served by the per-portal endpoints (`/staff/auth/me`,
  `/school/auth/me`): the **entered-context** view (this workspace, this role). Target: a single
  `/context/me` derived from the slim token's `context_id`, answering *"who am I **here**?"* including
  the resolver's capabilities for this context.

---

## 7. Parent home vs `/o/{slug}`

Two different surfaces that coexist by design:
- **Parent home** (`/identity/home`) — school-**agnostic**: a parent belongs across many schools
  (their children's), so home aggregates the family, not one workspace. A parent membership's context
  is org-scoped but the home view spans them.
- **`/o/{slug}`** (`/organizations/{slug}`) — a specific **workspace** entered by slug. Today the slug
  lives on `schools` (school-as-org); post-EDD-010 it is the Organization's slug, so `/o/{slug}`
  resolves an Organization and the Context Session scopes into it.

They never conflict: home is the identity's cross-workspace surface; `/o/{slug}` is one workspace.

---

## 8. The target pipeline

```
Identity ──auth──▶ Identity Session
   │                    │ (0 → onboarding · 1 → auto · many → pick)
   ▼                    ▼
Membership ─┐     select context (context_id)
Employment ─┼──▶ Access Context (projection) ──▶ Context Session (slim JWT: id·membership·context·org·role)
Organization┘                                          │
                                                       ▼  per request
                                          CapabilityResolver(context_id) ──▶ [RequireCapability]
```

**Invariant (from the platform sequencing):** Authentication may consume canonical aggregates or
projections derived from them, but **never** legacy actor tables. When no login path needs
`school_owners` / `staff_affiliations` / `parents`, the strangler is finished.

---

## 9. The B2 sprints (each isolated; foundation is frozen)

- **B2a — Access Context Projection.** `access_contexts` is rebuilt as a projection of
  Membership/Employment/Organization instead of the silos, via a single `AccessContextProjector`. JWT
  and frontend **unchanged**. Proven **rebuildable** (drop → rebuild → byte-identical).
  **Projection invariants:**
  1. `DELETE FROM access_contexts` loses no business data.
  2. Idempotent — running the projection twice yields identical rows.
  3. Order-independent — owner/staff/parent in any order → identical result (set-based SQL).
  4. No business rules in the projector — they live in Membership/Employment/Organization.
  5. Runs synchronously / after a lifecycle change / nightly — same output.
  *Existence, status, and organization come only from canonical aggregates; the legacy actor table is
  dereferenced solely to fill `reference_id` (the login-compat pointer), which B2c removes.*
- **B2b — Capability Resolver.** Introduce the server-side, **actor-neutral** resolver
  (`context → membership → employment → position → template → capabilities`); `[RequireCapability]`
  consults it. Authentication is untouched (auth never resolves permissions — see §4). Flags still in
  the token for now (removed in B2c).
- **B2c — JWT Slimming.** Split into three single-invariant milestones (see Appendix A for the full
  contract diff + retirement proof):
  - **B2c.1 (done) — Canonical Identity Token.** Additive: `membership_id` + `organization_id` join the
    projection (`access_contexts.membership_id`) and the context token, superseding the legacy actor id.
  - **B2c.2 (done) — Token Cleanup.** The 13 permission flags leave the token (authorization is fully
    server-side); dead `[RequireFeature]` deleted. Retirement proof kept `is_owner` (13 business
    consumers), `affiliation_id` (staff scoping), `user_type` (portal/UX), `school_id` (tenant), `role`.
  - **B2c.3 — Authentication Simplification.** The "owner / staff / parent authentication" split
    collapses to a single authenticated Identity (the token invariant above). Sequenced as **four
    independent commits**, each with its own rollback point — this is the lockout surface, so nothing
    bundles:
    - **B2c.3a — Signing-key unification.** The 5 per-portal signing keys → **one**. Purely
      cryptographic; bearer schemes + claims + refresh unchanged. (In-flight access tokens signed with
      the old keys fail validation and silently refresh — refresh tokens are opaque DB rows, unaffected;
      no hard logout.) *Accept:* every path's token validates, refresh still works, schemes unchanged.
    - **B2c.3b — Bearer-scheme unification.** The per-portal bearer schemes (`StaffAuth`/`SchoolAuth`/
      `ParentAuth`/`IdentityAuth`/`PlatformAdminAuth`) → **one `Bearer`**; portal policies re-expressed to
      behave identically (gate on `user_type` within the single scheme). No claim/refresh change.
    - **B2c.3c — Refresh identity re-key.** `refresh_tokens` keyed on `(identity_id, context_id)` instead
      of `(actor_type, actor_id)`; `GetIdentityIdForActorAsync` retired. The biggest behavioral change —
      own rollback point. *Accept:* refresh · logout · context-switch · revoked-family theft detection ·
      concurrent refresh all behave identically.
    - **B2c.3d — Compatibility retirement.** Only after 3a–3c: drop `access_contexts.reference_id`, the
      legacy refresh columns + lookup code, and the Transitional claims (Appendix B). Final cleanup.
    `user_type` stays throughout (UX/portal semantics, not authorization).
  The pure-minimal `id · membership · context · org · role` target is the *asymptote*; the retained
  claims above are re-sourced (owner-ness, staff scoping, tenant) as their consumers migrate.
- **B2d — Legacy Retirement.** Remove `school_owners` / `staff_affiliations` / `parents` from the
  login pipeline. Auth now consumes only canonical aggregates + projections.

After B2, Authentication is "finished" — future work is product capability, not architectural rewrite.
```
FOUNDATION ✅ → PLATFORM INTEGRATION (B2a→B2d) → PLATFORM STABILIZATION → BUSINESS MODULES
```

---

## Appendix A — JWT contract diff (B2c.1 + B2c.2)

The permanent record of what changed in the token and **why**. Every removed claim carries a
retirement proof: **no runtime reader, no frontend dependency, no middleware dependency, no refresh
dependency.** This exists so a future maintainer never silently reintroduces a removed claim.

### Staff scoped context token

```
BEFORE (pre-B2c)                          AFTER (B2c.1 additive + B2c.2 removal)
  user_id                                   user_id
  user_type                                 user_type
  is_owner                                  is_owner                 ← KEPT (load-bearing)
  school_id                                 school_id
  active_school_id                          active_school_id         (inert; swept in B2c.3)
  affiliation_id                            affiliation_id           ← KEPT (load-bearing)
  role                                      role
  employment_type                           employment_type          (inert; swept in B2c.3)
  kyc_status                                kyc_status               (inert; swept in B2c.3)
  phone                                     phone
  identity_id                               identity_id
  context_id                                context_id
  + 13 permission flags (can_*)             + membership_id          ← NEW (B2c.1, canonical identity)
                                            + organization_id        ← NEW (B2c.1)
                                            − 13 permission flags    ← REMOVED (B2c.2)
```

### Added (B2c.1, additive)
- **`membership_id`** — the canonical identity of the context (Membership, EDD-007), from
  `access_contexts.membership_id` (written by the projector). Supersedes reliance on the legacy actor id.
- **`organization_id`** — the organization the context operates in (today `== school_id`; the
  forward-canonical claim as `schools → organizations` FK-repoint lands later).

### Removed (B2c.2) — retirement proof
- **The 13 permission flags (`can_*`)** — *runtime readers:* NONE. Enforcement is
  `[RequireCapability] → ICapabilityResolver(context_id)` (B2b); the only flag-claim reader was the
  `[RequireFeature]` attribute, which had **0 live call sites** and is deleted. *Frontend:* never
  decodes the JWT (opaque bearer; reads `/me` responses) — no dependency. *Middleware:*
  `RequestResponseLoggingMiddleware` reads only `user_type` — no dependency. *Refresh:* reads the
  `refresh_tokens` table (`actor_type`/`actor_id`), never JWT claims — no dependency.

### Kept — retirement proof **FAILED** (still consumed; deliberately NOT removed)
- **`is_owner`** — consumed by 13 business services via `IEduTechRequestContext.IsOwner` as an
  owner-discriminator (Attendance, Grades, Fees, StaffAttendance, StaffProfile, SchoolBranding).
  Re-sourcing owner-ness onto capability/context is a cross-module change — a later refinement.
- **`affiliation_id`** — scopes staff actions (`CurrentAffiliation()` in Attendance / Grades /
  StaffProfile). Re-sources onto Membership/Employment later.
- **`user_type`** — portal/UX semantics (Identity Home / Family Home / Workspace routing + the portal
  authorization policies), **not** authorization. Deliberately retained: capabilities decide access,
  `user_type` decides UX.
- **`school_id`** — the tenant binding (`TenantRepository`). Retained until the `schools→organizations`
  FK-repoint.
- **`role`** — `[RequireRole]` gates + business role checks.

### Deferred to B2c.3 (Authentication Simplification)
- Sweep the residual **inert** claims (proven 0 readers, left until the mint paths are unified):
  `active_school_id` (redundant with `school_id`), `employment_type`, `kyc_status`, `subdomain`,
  `school_status`.
- Remove the **vestigial `features` parameter** + the mint-time `StaffFeatureResolver` resolution
  (`StaffFeatureResolver` itself stays — `StaffProfileService` still uses it for the permissions UI).
- One signing key / one bearer scheme / portal-policy cleanup; refresh re-keyed on
  `(identity_id, context_id)`; drop `access_contexts.reference_id`.
- **`[RequireRole]` → `[RequireCapability]`**: blocked in B2c.2 because capability resolution is
  flag-derived (`CapabilityResolution` grants staff capabilities only through a `LegacyFlag`). A
  "leadership" capability needs either a new flag (forbidden — no new JWT flags) or resolver support for
  flag-less role-default capabilities — an authorization change to live endpoints, not token cleanup.
  It belongs with the capability-model work, not this milestone.

---

## Appendix B — JWT claim taxonomy (the living contract)

Every claim in a context token falls into one of three categories. The point is that no one mistakes a
**compatibility** or **transitional** claim for the permanent contract, or adds a new reader of one.

> **Rule:** new code reads only **Canonical** claims (or resolves authorization from `context_id`). Never
> add a reader of a Compatibility or Transitional claim. A Compatibility claim retires when its last
> reader migrates to its replacement; a Transitional claim retires in B2c.3.

### 1. Canonical — permanent platform concepts

The stable long-term contract; the slim token trends toward exactly these.

| Claim | Meaning |
| --- | --- |
| `identity_id` | the person (Identity, EDD-001) — *who* |
| `membership_id` | the context's canonical belonging (Membership, EDD-007) — the token's canonical identity |
| `context_id` | the entered Access Context; the key `ICapabilityResolver` resolves authorization on |
| `organization_id` | the organization the context operates in — *where* |
| `school_id` | the tenant binding today (`TenantRepository`); **converges with `organization_id`** at the `schools→organizations` FK-repoint (same value now) |
| `role` | the context's role — UX + the `[RequireRole]` gates |
| `user_type` | portal/UX discriminator (Identity Home / Family Home / Workspace) — **not** authorization |

### 2. Compatibility — retained because live code still reads them

Each has a documented reader set, a retirement sprint, and a replacement mechanism.

| Claim | Remaining readers | Retires | Replacement |
| --- | --- | --- | --- |
| `is_owner` | 13 business sites via `IEduTechRequestContext.IsOwner` (Attendance, Grades, Fees, StaffAttendance, StaffProfile, SchoolBranding) | capability-model work (post-B2c) | owner context → capabilities (owner Position grants all) / an explicit ownership policy |
| `affiliation_id` | `AttendanceService`, `GradeService`, `StaffProfileService` (`CurrentAffiliation()` — scope staff actions to their own arm/records) | when staff scoping re-sources off the legacy actor | `membership_id` / active Employment |
| `user_id` | `IEduTechRequestContext.UserId` (the legacy actor id — staff_user / owner / parent id — used widely as "the current actor") | **B2d** (legacy actor retirement) | `identity_id` + `membership_id` |

### 3. Transitional — DEPRECATED; retained only because the mint paths aren't yet unified

Proven **0 readers** (no runtime / frontend / middleware / refresh reader — see Appendix A). Kept only
because removing them re-touches the refresh/legacy mint signatures; **swept in B2c.3**.

| Claim | Note |
| --- | --- |
| `active_school_id` | redundant with `school_id` |
| `employment_type` | 0 readers |
| `kyc_status` | 0 readers (the "read-only mode without a DB hit" it was meant to back was never wired) |
| `subdomain` | 0 readers |
| `school_status` | 0 readers |

*(`phone` also rides the token as an incidental contact claim — not part of the identity/authorization
contract; it is not slimming-relevant.)*
