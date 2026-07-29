# SchoolFlow Backend — V1 Production-Readiness Review

**Reviewer:** senior-architect pass (code-grounded). **Scope:** `EduTech 1.0/src` (17 projects), migrations
0001–0065, auth/authz, the convergence work (EDD-016) and the canonical-domain implementations (EDD-017
shipped Slices 1–4; EDD-018 Slice 1). **Verdict below is honest about a codebase mid-migration by design.**

## Executive summary

**The architecture is sound and the foundation is genuinely clean.** Dependency direction is correct, SQL
is fully parameterized, migrations are transactional/idempotent/tracked, authorization is centralized and
server-side, and the canonical model (`Identity → Membership → Employment → Position`) is real, not
aspirational. The convergence sprints (Attendance/Grades/Fees/Audit) and Identity-KYC runtime canonicalization
are done and test-proven (525 tests green).

**This is not "done," and that's expected.** Two domains (Workforce, Family) are still on legacy actor tables
(`staff_users`/`staff_affiliations`, `parents`) by plan — EDD-018/019 are designed, partially implemented, and
on a disciplined retirement path. So "V1 readiness" splits into: (a) a small set of *fix-before-launch*
operational items, and (b) the *finish-the-migration* roadmap that's already scoped.

**No P0 architectural defects found.** The findings are operational hardening + known migration debt.

---

## Prioritized findings

### Agreed launch priority (post-review): operational hardening, then **freeze the foundation**, then build features.
The remaining canonicalization (actor-table retirement) proceeds **feature-driven** — implement Workforce/Family
because "schools can onboard staff/families," not to "delete `staff_users`." Ceremonial cleanup (concept greps,
`reference_id`/compat-claim deletion) is postponed until the product is stable.

### P1 — Address before / at launch

**P1-2 · N+1 in the staff-permissions endpoint — ✅ FIXED.**
`StaffProfileService` (permissions view) fetched overrides **per active staff member** in a `foreach` (one
query per staff; a 100-staff school = 100 round-trips). Now batched into a single
`GetForAffiliationsAsync` (`WHERE affiliation_id = ANY(@Ids)`), mirroring the existing template cache.
Build 0/0; 525 tests green. *(Follow-up investigated & clear: the Grades report-card path is **already
batched** — `NotifyGuardiansAsync` reads targets in one query and only the SMS *dispatch* loops (inherently
per-recipient); the `grade_entries`/behavioral inserts are bounded write loops in a single transaction.
Optimizing those to multi-row `unnest` inserts is a P3 nice-to-have, not a launch issue. The scoping indexes
were added anyway (P2-3, migration `0066`). No further P1 perf debt.)*

**P1-3 · Secrets: hardcoded DB password in `appsettings.json` — ✅ FIXED.**
The base `appsettings.json` shipped `Password=admin123`. Replaced with a `REPLACE_WITH_DB_PASSWORD_VIA_ENV`
placeholder + a `_comment` requiring prod to supply `ConnectionStrings__Default` via env/secrets manager. No
real credential is committed; local dev remains self-contained in `appsettings.Development.json`. (JWT signing
keys were already correctly absent from the base file — env-only in prod; dev keys isolated in Development.json.)

**P1-4 · Auto-migrate on startup + horizontal scaling — ✅ MITIGATED.**
`DatabaseMigrator` runs pending migrations at startup (transactional, tracked, baseline-aware) and is gated by
`Database:AutoMigrate` — **the recommended prod strategy remains: flip it OFF and run migrations as a pre-deploy
CI/CD step.** As defense-in-depth if it's left on across instances, migration now acquires a Postgres session
**advisory lock** (`pg_advisory_lock`) so concurrent migrators serialize — the first migrates, the others block
then find the schema up to date; the lock auto-releases on connection close, even on failure.

### P2 — Soon after launch

**P2-1 · Authorization is opt-in, not fail-closed.** Coverage is good today (management controllers carry
`[RequireCapability]`; only true auth entry-points — `AuthController`, `StaffInviteController` (send-otp/accept),
`AdminAuthController` — are anonymous, which is correct). But there's no global **fallback deny** policy, so a
*future* endpoint added without `[RequireCapability]` is open by default. *Recommendation:* add a fallback
`RequireAuthenticatedUser` policy + a test asserting every non-`[AllowAnonymous]` endpoint resolves a capability.

**P2-2 · Finish the actor-table retirement (the tracked migration debt).** The concept grep is *clean of
surprises* — `reference_id` (6 files), `staff_affiliations` (10), and the `affiliation_id`/`user_id` JWT compat
claims (8 in `TokenVendor`) are **all** documented transitional couplings on the EDD-018/019 → final-retirement
path. Nothing unexpected leaked in. This is debt-by-design; the risk is only *schedule*, not correctness.

**P2-3 · Missing index on the teacher-scoping column — ✅ DONE (migration `0066`).** The Attendance
markable-arms and Grades gradeable-arms queries filter `class_arms`/`classes.class_teacher_membership_id`
(`WHERE … = @Mem`). Added **partial** indexes (`… WHERE class_teacher_membership_id IS NOT NULL`) so they
stay small (most rows are NULL / owner-taught). The subject-teacher and staff-checkin membership lookups were
already covered by their `UNIQUE` constraints; the other 0060–0065 canonical columns aren't filtered.
Verified: `0001→0066` applies cleanly, both indexes present with the correct definition.

### Accepted tradeoff (documented, **not** a launch blocker)

**Cross-module table reads.** Attendance/Grades/Fees read Students-owned tables directly via raw SQL
(`students`, `classes`, `class_arms`, `subjects`, `child_profiles`). For a **modular monolith this is a
legitimate engineering tradeoff** as long as ownership is documented, *writes* stay encapsulated in the owning
module, and the Students schema evolves deliberately (all true today). Introducing read views/APIs purely for
purity before V1 would add complexity with little user benefit. *Action:* **document** the cross-module read
dependencies (which modules read which Students tables) so the schema's blast radius is explicit — do **not**
refactor before launch.

### P3 — Housekeeping

- **`Auth → Workforce` layer inversion** — Auth (integration layer) depends on Workforce (product) for the
  legacy staff tables. Disappears when EDD-018 moves staff onto `Identity/Membership/Employment` (Auth already
  references those). No action; resolves with the migration.
- **`employment_type` dead mint param** — the parallel-to-KYC vestigial param (`_ = employmentType` in
  `TokenVendor`) — a one-line Appendix-B cleanup whenever the mint signatures are next touched.
- **`students`/`child_profiles` name duplication** — the child's name lives on both `child_profiles` and
  `students`; EDD-019 collapses this (Child Profile as the source). Tracked.

---

## Per-area notes

### 1. Architecture & structure — **strong**
Clean dependency direction (`Shared` is the leaf-base; foundation contexts → `Shared` only; product modules →
`Shared` (+`Identity` for Students) only; `Api` is the composition root). No cross-module *code* coupling
(`InternalsVisibleTo` is limited to `Auth`/tests). The canonical graph is real. **The only structural debt is
P1-1 (data-level coupling).**

### 2. Auth / Authz — **canonical work is clean**
Server-side authorization via `ICapabilityResolver` (context → Employment → Position → template); slim JWT
(claims are identity + context, no embedded authorization — verified by `JwtContractTests`); `context_id =
access_contexts.id`; identity+context refresh. EDD-017 made KYC runtime-canonical with a *stronger-than-a-matrix*
proof (no login decision consumes person KYC; Identity is the single source; legacy is a proven-ignored mirror).
Remaining legacy is tracked (P2-2).

### 3. Workforce — **mid-migration, on a sound plan**
Still runs on `staff_users`/`staff_affiliations` (that's the point of EDD-018). Slice 1 (canonical schema) is
done + verified; Slices 2–5 are designed and de-risked (additive dual-write; legacy path untouched, so
migration bugs can't break onboarding). The invitation-creates-`Employment(Pending)` work is the pattern-setter
and correctly the next focused effort. Directory/invite/exclusivity logic is coherent today.

### 4. Students / Family — **designed (EDD-019), not yet implemented**
`parents` is a legacy actor table; `parent_children` is the guardian relationship but thin; `child_profiles.parent_id`
is redundant with it. EDD-019 (Guardian Relationship as the center) is design-complete and closes the deferred
Fees payer. This is the last canonical domain; readiness = "clear plan, not yet built."

### 5. Core modules (Attendance / Grades / Fees / Admissions) — **converged**
Actor-keyed columns are canonical (membership/identity) and test-proven; Admissions is the reference module
(9 slices, zero foundation changes). The live gap is P1-1 (they read Students' tables) + P1-2 (Grades N+1).

### 6. Infrastructure — **production-grade**
Migrations: transactional per-file, idempotent (`IF [NOT] EXISTS`), tracked (`schema_migrations`), baseline-aware.
Repositories: Dapper with parameterized SQL throughout, snake_case mapping via `DapperConfiguration`, explicit
transaction scopes (`DbTransactionScope`) at multi-write sites. Events: `IDomainEventPublisher` observer +
auto-audit via `IAuditableEvent`. DI is per-module `Add…Module(...)` extensions. Only infra caveat is P1-4
(startup migration + scaling).

### 7. Security & performance — see P1-2/P1-3/P2-1/P2-3
SQL injection: **none** (fully parameterized; the one dynamic table name — `ComplianceRepository.Table()` — maps
a *validated* `user_type` to a fixed name, not user input). Input validation present at service boundaries. The
performance items are the N+1 (P1-2) and the scoping index (P2-3).

---

### 8. Observability & operations — **strong** (one gap, fixed)
Deeper pass on the areas the first review under-covered:
- **Background jobs — production-grade.** **Hangfire** runs recurring jobs (`Cron.Daily(3)`
  `identity-reconciliation`, calendar roll-forward) and a queued `SendSmsJob` (`[Queue("notifications")]`) —
  scheduling, retries, and persistence come with it, so SMS never blocks a request and jobs survive restarts.
- **Error handling + alerting — well-designed.** `GlobalExceptionMiddleware` separates expected
  `AppErrorException` (business 4xx) from genuine 500s and **Slack-alerts only the real 500s**
  (`HttpSlackNotifier` when `Slack:WebhookUrl` is set, else `LoggingSlackNotifier` — graceful degradation).
- **Logging** — `RequestResponseLoggingMiddleware` with **secret redaction** (password/otp/token/nin/bvn/…).
- **The gap (now ✅ FIXED): `/health` didn't probe Postgres** — only Redis (and only when configured). A
  readiness probe that ignores the DB would route traffic to an instance that can't serve requests. Added
  `PostgresHealthCheck` (`SELECT 1`, always registered, tagged `db`) alongside the Redis check. Build 0/0;
  525 tests. *(Optional next: tag Redis/DB checks `ready` vs a lightweight `live` probe so liveness ≠ readiness.)*

## Bottom line

Ship-readiness is **operational, not architectural.** The pre-launch list is short and now shorter: ✅ N+1
(done) → env-only prod secrets (P1-3) → migration-on-deploy not on-boot (P1-4) → the scoping index (P2-3): one
small hardening sweep. **Then freeze the foundation.** The cross-module reads are a documented, accepted
modular-monolith tradeoff, not a blocker.

After that, the highest-return work is **product, not cleanup**: Workforce and Family ship next **as features**
(staff/family onboarding), and the actor-table retirement falls out as a *consequence* — never pursued as an
end in itself. The canonical foundation is clean; the remaining legacy is *scheduled, feature-driven debt*,
not risk. The architecture is no longer the bottleneck — delivering the workflows schools use is.
