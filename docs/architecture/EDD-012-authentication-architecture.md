# EDD-012 — Authentication Architecture

**Status:** Specification (defines the target the B2 sprints implement toward)
**Domain:** Platform Integration
**Thesis:** Authentication owns nothing. It **projects** the platform:
`Identity → Membership → Employment → Organization → Access Context → JWT`.

> **The contract for the entire authentication system:** `access_contexts` is **not** a domain model.
> It is a **disposable projection** built entirely from canonical aggregates (Identity, Membership,
> Employment, Organization). It may be **deleted and rebuilt at any time without loss of business
> data.** If that is ever untrue, the projection is wrong — not the aggregates.

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
A server-side `CapabilityResolver`, keyed on the request's `context_id`:
```
context → employment → position → permission template → capabilities
```
with a per-request cache. `[RequireCapability]` asks the resolver, not the token. The 13 flag claims
disappear from the JWT (B2c). Owner-bypass becomes a capability set on the owner position.

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
- **B2b — Capability Resolver.** Introduce the server-side resolver (`context → position → template →
  capabilities`); `[RequireCapability]` consults it. Flags still in the token for now (removed next).
- **B2c — JWT Slimming.** Token becomes `identity_id · membership_id · context_id · organization_id ·
  role`; one signing key / `user_type`; refresh re-keyed on identity + context. Capabilities and
  legacy actor ids leave the token.
- **B2d — Legacy Retirement.** Remove `school_owners` / `staff_affiliations` / `parents` from the
  login pipeline. Auth now consumes only canonical aggregates + projections.

After B2, Authentication is "finished" — future work is product capability, not architectural rewrite.
```
FOUNDATION ✅ → PLATFORM INTEGRATION (B2a→B2d) → PLATFORM STABILIZATION → BUSINESS MODULES
```
