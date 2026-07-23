# EDD-011 — Event Catalog

**Status:** Sprint implemented (catalog + `EmploymentActivated` rename)
**Domain:** Platform · the canonical **messaging contract**

---

## 1. Why this exists

Domain events are **not** implementation detail — they are the **language of the platform**.
Admissions, Notifications, Audit, Search, and Analytics all react to the same events; if each module
coins its own names, the system drifts into inconsistency. This catalog is the dictionary every module
speaks, defined **once, before** those modules are built. (It also precedes B2 auth finalization: login
becomes a projection expressed *in* these events — fix the words before rebuilding the consumer.)

## 2. Conventions

- **Naming:** `AggregatePastTense` in **business** language, never implementation
  (`EmploymentActivated`, not `AffiliationRowUpdated`). One event = one meaningful business fact.
- **Envelope:** every event derives from `DomainEvent` — `EventId` (dedupe), `OccurredAt`,
  `CorrelationId` (ties one operation's events together), `ActorIdentityId` (who caused it).
- **Audit:** an event that implements `IAuditableEvent` (`Action`, `EntityType`, `EntityId`,
  `Summary`, `Metadata`) is written to the per-school audit trail **automatically** by the generic
  `AuditLogHandler` — the publisher supplies only the facts.
- **Versioning & compatibility:** each event has a **Version**. Changes are **additive only**
  (new optional fields); a breaking change mints a new version, never mutates a shipped one. Consumers
  must tolerate unknown fields. `EventId` makes delivery idempotent.
- **Ownership:** exactly one aggregate owns (publishes) each event; everyone else only consumes.

## 3. Contract shape

Every entry below is: **Name · Owner · When · Payload · Consumers · Version · Compat**.

## 4. Built events (v1 — live today)

### Organization / School
- **SchoolActivated** · Owner: School (→ Organization) · When: platform admin approves KYC ·
  Payload: `SchoolId` · Consumers: ProvisionCalendar, ProvisionClasses · v1 · additive-only.
  *(Canonical target: `OrganizationActivated` once the org root is read; recorded, not renamed now.)*

### Employment
- **EmploymentActivated** · Owner: Employment · When: a working relationship becomes active (today: a
  staff invite is accepted) · Payload (v1): `AffiliationId · SchoolId · StaffUserId · Role · StaffName` ·
  Consumers: Audit (+ future: Notifications, Access Context reconciliation, Analytics) · v1 ·
  **planned v2 payload:** `EmploymentId · MembershipId · OrganizationId · PositionId · OccurredAt`
  (when the publish moves into the Employment context).

### Student
- **StudentLifecycle** · Owner: Student · When: withdraw / re-admit / transfer / revert ·
  Payload: `StudentId · Action · Summary · Metadata(before-state)` · Consumers: Audit · v1.
  *(Canonical target: split into `StudentWithdrawn` / `StudentReadmitted` / `StudentTransferred` when
  the Students module is formally built.)*

### Admissions
- **ExamScheduled** · Owner: Admissions · When: entrance exam scheduled for an application ·
  Consumers: AdmissionNotification · v1.
- **ApplicationAdmitted** · Owner: Admissions · When: an application is admitted ·
  Consumers: AdmissionNotification · v1. *(Canonical target: `ApplicationAccepted`.)*
- **ApplicationRejected** · Owner: Admissions · When: an application is rejected ·
  Consumers: AdmissionNotification · v1.

### Identity / Membership
- **GuardianLinked** · Owner: Admissions/Membership · When: admitting a student links a guardian phone ·
  Payload: `SchoolId · Phone · FirstName? · LastName?` · Consumers: EnsureIdentityOnGuardianLinked
  (ensures a pending Identity + parent Membership) · v1.

## 5. Reserved events (named, NOT built)

The canonical vocabulary future aggregates/modules must use — reserved so nothing reinvents it.

| Owner | Reserved names |
| --- | --- |
| Identity | `IdentityRegistered · PhoneVerified · IdentityLocked · IdentityUnlocked` |
| Membership | `MembershipCreated · MembershipActivated · MembershipSuspended · MembershipEnded` |
| Employment | `EmploymentCreated · EmploymentSuspended · EmploymentEnded · EmploymentTransferred · EmploymentPositionChanged · EmploymentManagerChanged` |
| Organization | `OrganizationCreated · OrganizationRenamed · OrganizationSuspended · OrganizationArchived · OrganizationActivated · OrganizationOwnershipTransferred` |
| Admissions | `ApplicationSubmitted · ApplicationReviewed · ApplicationAccepted · ApplicationRejected · StudentEnrolled` |
| Finance | `InvoiceIssued · PaymentReceived` |

Each is built by its owning aggregate/module sprint, at which point it graduates to §4 with a full
Payload/Consumers/Version row. Where a built v1 name differs from its canonical target (SchoolActivated
→ OrganizationActivated; ApplicationAdmitted → ApplicationAccepted; StudentLifecycle → split), the
rename lands in that module's sprint — not here.

## 6. Non-goals

No event-infrastructure change; no building of reserved events; no renaming of live module events
(their sprints own that); no physical move of event classes into owning projects; no auth/JWT/B2 work.
