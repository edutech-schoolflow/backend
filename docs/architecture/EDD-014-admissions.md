# EDD-014 — Admissions (the reference module)

**Status:** 🔒 FROZEN — Parts 1–11 + UX complete. Design is done; **all 9 vertical slices (Part 11) have
shipped — the module is complete.** See **Build status** at the end for what shipped and the two honest
deferrals the maturity test surfaced.
**Role:** The first module built on the finished platform — the **reference implementation** and the
**Platform Validation** case: it may not modify the Foundation; any Foundation change it needs is a
defect, not a new concept.

> This EDD is written **existing-state first**. We understand what Admissions already is before
> redesigning it. Part 1 is facts only.

---

## Part 1 — Existing State (facts)

Admissions already exists, as a folder inside `EduTech.Students/Admissions` (not yet its own module).

**Surfaces (3 controllers):**
- `SchoolApplicationController` — `/api/v1/school/applications` — school-facing: list, get, schedule
  exam, record assessment, **admit**, **reject**. Gated with `[RequireCapability]`
  (`Capabilities.Student.Read`, `Capabilities.Admissions.Manage`).
- `ParentApplicationController` — `/api/v1/family/applications` — parent-facing: **submit**, list, get,
  **pay** (application fee). Parent portal.
- `IdentityApplicationsController` — `/api/v1/identity/applications` — identity-facing: list.

**Services / data:** `SchoolApplicationService`, `ParentApplicationService`, their repositories,
`ApplicationDtos`/`ApplicationMapper`/`ApplicationSql`. Table `applications` (migration 0023):
`school_id`, `status`, `admitted_student_id → students`, …

**Lifecycle** (`ApplicationLifecycle`, a real `StateTransitions` state machine):
`under_review → exam_scheduled → {admitted | rejected}`; `admitted`/`rejected` are terminal; illegal
moves 409.

**Events** (`Admissions/Events/AdmissionEvents.cs`): publishes `ExamScheduledEvent`,
`ApplicationAdmittedEvent`, `ApplicationRejectedEvent`; `AdmissionNotificationHandler` consumes them
to notify. Admission also drives `GuardianLinkedEvent` (→ `EnsureIdentityOnGuardianLinked`), which
creates a **pending Identity + parent Membership** — so admission already reaches the identity model.

## Part 1b — Dig assessment (keep / gap / would-violate)

**Already on the new platform (keep):**
- ✅ Authorization via `[RequireCapability]` — no legacy flag reads.
- ✅ Event-driven (publishes admission events; consumed by Notifications/Audit).
- ✅ Identity-first: admission creates a pending Identity + parent Membership through `GuardianLinked`.
- ✅ An identity-facing surface already exists (`/identity/applications`).
- ✅ A clean lifecycle state machine.

**Gaps to reach reference-implementation status (product/vocabulary, not foundations):**
- Workflow is thin vs. the target: no explicit **submitted**, no **documents/checklist** stage, no
  **offer → acceptance** step (admit is terminal). Product design — Part 4.
- Event names predate the **Event Catalog** canon: `ApplicationAdmitted` → canonical `ApplicationAccepted`;
  missing `ApplicationSubmitted`/`OfferIssued`/`OfferAccepted`/`StudentEnrolled` (EDD-011).
- Capabilities are coarse (`Admissions.Manage`, `Student.Read`); the target is fine-grained action
  capabilities (`admissions.application.review/approve/reject`, `admissions.offer.issue`, …).
- Module boundary: Admissions lives inside `EduTech.Students`. Decide whether it becomes its own
  bounded context (`EduTech.Admissions`) with the Admissions↔Students seam expressed as an event
  (`StudentEnrolled`) rather than a shared folder.

**Would violate the new architecture (watch for during redesign):** direct writes to `students`
(cross-module table access if Admissions and Students split); inventing new membership/identity
concepts instead of consuming the platform contracts; role-thinking instead of action capabilities.

**Headline:** the platform's maturity is *partly already proven* — Admissions was retrofitted onto
capabilities + events + identity during the earlier sprints. The redesign is about product depth,
vocabulary alignment, and the module boundary — **not** foundational plumbing. That is a good sign.

---

## Decisions (locked)

1. **Extract `EduTech.Admissions`** as its own bounded context (Layer 3). Admissions is *not* part of
   Student Management — everything before *Student* exists without a student.
2. **Build the full lifecycle** — Inquiry → Draft → Submit → Document Verification → Assessment(s) →
   Review → Decision → Offer → Accept → Enrollment → Family Workspace Ready — not admit/reject.
3. **Adopt canonical events + fine-grained capabilities now** — this is the reference implementation;
   future modules copy its vocabulary. Rename once, teach once.
4. **`AdmissionCycle` is first-class** from day one (deadlines, quotas, waiting lists, reporting).
5. **Admissions ↔ Students is event-driven**, handoff = `StudentEnrolled`.

> **Boundary principle:** *Admissions owns prospective learners; Students owns enrolled learners. The
> only transition between them is the `StudentEnrolled` domain event.* Admissions owns **decisions**,
> not students — it reaches "offer accepted / enrolled" and **publishes**; the platform (Identity,
> Membership, Students, Fees, Calendar, …) reacts. Admissions writes none of those tables.

## Part 2 — Desired Product

Admissions is the **relationship-creation engine** and the school's **first impression** — a *journey*,
not a form. For a parent it should feel like tracking an online order, surfaced in the family
dashboard (identity-first):

```
Application received ✓ · Documents verified ✓ · Assessment booked ✓ · Assessment completed ✓
· Offer issued ✓ · Offer accepted ✓ · Student enrolled ✓  →  Family Workspace Ready
```

The workflow ends not at "create student" but at **a family becoming part of the organization** — the
end state users experience is *Family Workspace Ready*, not a backend row. This works for schools,
universities, and training centres because Assessment and Decision are pluralized, not exam-shaped.

## Part 3 — Domain Model

Business aggregates (not tables). **`Enrollment` is deliberately *not* `Student`** — it is the business
act of joining an organization; the Student's academic record stays in the Students module.

- **AdmissionCycle** — a named intake (`2027/2028 Nursery`, `2027 Secondary Intake`) with deadlines,
  quotas, and status (open/closed). Applications belong to a cycle.
- **Inquiry** — pre-application interest ("I'd like to know more") → optional book-a-visit → convert to
  an Application. (Reuses the inspection/booking pattern.)
- **Application** — a prospective learner (a **Child Profile**, EDD-007) applying to a cycle: preferred
  class, guardian, fee context, draft→submitted lifecycle.
- **Assessment** — pluggable, typed: `exam · interview · observation · portfolio · external_result`;
  scheduled → completed with an outcome.
- **DocumentChecklist / Document** — its own lifecycle per document: `pending · uploaded · rejected ·
  verified`. An application progresses only when required documents are verified.
- **Decision** — richer than admit/reject: `approved · conditional · waitlisted · rejected · withdrawn`.
- **Offer** — first-class: campus, class, academic year, fee plan, scholarship, acceptance deadline,
  conditions. Issued from an approved Decision; accepted or lapsed.
- **Enrollment** — confirms the accepted offer became a real place in the organization; emits
  `StudentEnrolled`. Split from Decision on purpose: a child can be admitted yet never enroll.

## Part 4 — Workflow

```
Discover School → Inquiry → Book Visit (optional) → Application Draft → Submit →
Document Verification → Assessment(s) → Admissions Review → Decision →
Offer → Accept Offer → Enrollment → [platform events] → Family Workspace Ready
```

- **Enrollment is a platform transition, not an Admissions state** — at Accept/Enroll, Admissions
  publishes; the platform creates Identity, parent Membership, the Student (Students module), fee
  account (Fees), calendar/class assignment, notifications, audit. Admissions knows none of it.
- Sub-lifecycles: Application `draft→submitted→in_review→decided→offered→accepted→enrolled` (+ withdrawn);
  Document `pending→uploaded→(rejected|verified)`; Assessment `scheduled→completed`; Decision as above;
  Offer `issued→(accepted|lapsed|declined)`. Each guarded by a `StateTransitions` machine.

## Part 5 — Platform contracts (the maturity test)

Consumes **only** published contracts + services: Identity · Membership · Organization · Child Profile
(EDD-007) · `ICapabilityResolver` · Notifications · Audit · Storage (documents) · Event Publisher.
It reads **no** other module's tables and requires **zero Foundation changes**. The Students seam is
`StudentEnrolled` only.

## Part 6 — Events

**Published:** `InquiryCreated · ApplicationSubmitted · DocumentVerified · AssessmentScheduled ·
AssessmentCompleted · ApplicationReviewed · OfferIssued · OfferAccepted · OfferDeclined ·
ApplicationRejected · StudentEnrolled`.
**Consumed:** `GuardianLinked · IdentityRegistered · PaymentReceived`.
Reconcile today's `ApplicationAdmitted/ExamScheduled` into this canon (EDD-011). `OfferAccepted` and
`StudentEnrolled` are the events the platform reacts to.

## Part 7 — Capabilities (actions, not roles)

`admissions.cycle.manage · admissions.application.read/review/assign · admissions.document.verify ·
admissions.assessment.schedule/record · admissions.decision.approve/reject/waitlist ·
admissions.offer.issue` — registered in `CapabilityRegistry`, gated via `[RequireCapability]`.

## Part 8 — Physical Model (tables, no SQL yet)

All tables are organization-scoped and **org-type-neutral** (schools · universities · training centres ·
tutors — EDD-010). FKs point only at Admissions' own tables and platform contracts (`organizations`,
`child_profiles`, `identities`) — never another module's tables.

| Table | Owner aggregate | Key FKs | Lifecycle / status |
| --- | --- | --- | --- |
| `admission_cycles` | AdmissionCycle | `organization_id` | `draft → open → closed → archived` |
| `inquiries` | Inquiry | `organization_id`, `cycle_id?`, `converted_application_id?` | `new → contacted → visit_booked → converted → closed` |
| `applications` | Application | `cycle_id`, `organization_id`, `child_profile_id`, `guardian_identity_id?` | `draft → submitted → in_review → decided → offered → accepted → enrolled` (+ `withdrawn`) |
| `application_documents` | Application (owned) | `application_id` | per doc: `pending → uploaded → (rejected \| verified)` |
| `assessments` | Assessment | `application_id` | `scheduled → (completed \| cancelled)`; `type ∈ exam/interview/observation/portfolio/external_result` |
| `assessment_results` | Assessment (owned) | `assessment_id` | recorded outcome/score |
| `decisions` | Decision | `application_id`, `decided_by` | `outcome ∈ approved/conditional/waitlisted/rejected/withdrawn` (append-only) |
| `offers` | Offer | `application_id`, `decision_id`, `class_id?` | `issued → (accepted \| declined \| lapsed \| withdrawn)` |
| `enrollments` | Enrollment | `application_id`, `offer_id`, `organization_id`, `child_profile_id` | `active → cancelled`; emits `StudentEnrolled` |

**Invariants (the data-model questions, answered):**
- **Multiple offers per application: yes, over time; at most one *non-terminal* offer** (partial unique
  index on `application_id WHERE status = 'issued'`). Decline/lapse/withdraw is terminal for that offer;
  the school may then **issue a new offer** (so decline-then-later-accept works via a re-offer).
- **Offer expiry:** `acceptance_deadline` → a sweep lapses `issued` offers past it.
- **Offer belongs to a cycle** transitively (via `application.cycle_id`), never directly.
- **One active application per `(child_profile_id, cycle_id)`** (partial unique on non-`withdrawn`). A
  child applying to two campuses/programs = two applications (different cycles), not two offers on one.
- **Enrollment ≠ Student:** an `enrollment` row records joining the org; the Student is created by the
  Students module on `StudentEnrolled`. An accepted offer may never enroll (no payment / no-show).
- Documents gate progress: an application cannot leave `in_review` toward a positive decision while a
  *required* document is not `verified`.

Indexes: `admission_cycles(organization_id, status)`, `applications(cycle_id, status)`,
`applications(child_profile_id)`, `offers(application_id, status)`.

## Part 9 — Commands & Queries (the module's public contract)

**AdmissionCycle** — `OpenCycle · CloseCycle · SetQuota · ArchiveCycle` · `GetCycle · ListCycles ·
CycleStats`.
**Inquiry** — `CreateInquiry · BookVisit · ConvertToApplication · CloseInquiry` · `GetInquiry · ListInquiries`.
**Application** — `StartApplication(draft) · SubmitApplication · WithdrawApplication · AssignReviewer` ·
`GetApplication · ListApplications · SearchApplications · GetTimeline`.
**Document** — `RequestDocument · UploadDocument · VerifyDocument · RejectDocument` · `ListDocuments`.
**Assessment** — `ScheduleAssessment · RescheduleAssessment · RecordResult · CancelAssessment` ·
`GetAssessment · ListAssessments`.
**Decision** — `RecordDecision(approved/conditional/waitlisted/rejected/withdrawn)` · `GetDecision`.
**Offer** — `IssueOffer · AcceptOffer · DeclineOffer · WithdrawOffer · (ExpireOffer, system)` ·
`GetOffer · ListOffers`.
**Enrollment** — `EnrollStudent · CancelEnrollment` · `GetEnrollment`.

Each command maps 1:1 to an API endpoint gated by a Part-7 capability; queries are the read models.

## Part 10 — Database Mapping & Migration Strategy

**Aggregate → persistence:**
- One table per aggregate root; **owned entities** in child tables (`application_documents`,
  `assessment_results`) — loaded with the root, never addressed cross-module.
- **JSON** only for genuinely schemaless bags (offer `conditions`, assessment `metadata`); everything
  queried/filtered is a column (status, dates, FKs).
- Every state change publishes its Part-6 event; the read models (timeline, cycle stats) are projections.

**Migration strategy (coexist, then converge — the extraction is Part-11 work):**
- New migrations `0048…` create `admission_cycles / inquiries / assessments / assessment_results /
  decisions / offers / enrollments` and evolve the existing `applications` (0023) to the fuller
  lifecycle + `cycle_id` + `child_profile_id`. Additive first; the current 4-state flow keeps working.
- The existing `applications.admitted_student_id → students` FK is **superseded** by the `enrollments`
  row + `StudentEnrolled` event (the event boundary replaces the direct FK). The column itself is
  **retired at legacy decommission**, not in Slice 9 — the legacy `EduTech.Students/Admissions` flow is
  still the live path and still writes it (see Build status).
- Backfill: existing `admitted` applications → a default cycle + an `enrollment` row, so history is preserved.

## Part 11 — Implementation Plan (the last design artifact — then code)

Stand up **`EduTech.Admissions`** (Layer 3) and build it as **independent vertical slices**, each a
full `Domain · Repository · Events · API · Tests` (+ Frontend later) that is buildable, testable, and
deployable on its own. The existing `EduTech.Students/Admissions` flow keeps working and is migrated in
across the slices; the Admissions↔Students seam becomes the `StudentEnrolled` event (the
`applications.admitted_student_id` FK retires with it).

**Slices (deploy after each):**
1. **AdmissionCycle** — module scaffold + cycle CRUD/lifecycle.
2. **Inquiry** — pre-application interest + book-a-visit + convert.
3. **Application** — draft → submit → withdraw; migrate the existing applications flow in.
4. **Documents** — checklist + per-document verification lifecycle.
5. **Assessment** — typed assessments + results.
6. **Decision** — approved/conditional/waitlisted/rejected/withdrawn.
7. **Offer** — issue/accept/decline/withdraw/expire.
8. **Enrollment** — enroll/cancel (the platform-transition point).
9. **StudentEnrolled event** — the handoff; Students consumes it and creates the Student; supersede the
   direct FK (the column retires at legacy decommission, not here).

**Per-slice gate (the maturity test, enforced):** zero changes to Identity / Membership / Employment /
Organization / Access Context / Authentication; depends only on published contracts + services;
build + tests green; migration verified on throwaway Postgres.

**Conventions:** migrations `0048…`; per-org tables are tenant-scoped by `school_id → schools(id)`
(the `TenantRepository` convention — the operational organization scope; renames/re-points to
`organizations` with the rest of the platform in the FK-repointing sprint). Capabilities start on the existing coarse
`Admissions.Manage`/`Student.Read` (which the resolver already grants) and refine to the fine-grained
`admissions.*` set (registered in `CapabilityRegistry`, bridged to those flags) in a dedicated step.

> **EDD-014 is now FROZEN.** No Parts 12+. Everything needed to build exists. Further change is code
> + tests, not design.

## Build status — the module is complete (the maturity test passed)

All 9 slices shipped as `EduTech.Admissions` (Layer 3), one migration + one vertical slice each
(`0048…0055`). **The reference-module thesis is proven:** across the whole build — including the finale,
the cross-module Admissions → Students handoff — **not one Foundation source file changed.**
`EduTech.Admissions` references **only** `EduTech.Shared`; every capability came from published contracts
and services. The single cross-module coupling is one event.

**Slice 9 (the finale) — the handoff, as built:** `EnrollmentService` raises `StudentEnrolled` (a Shared
Event-Catalog contract, added additively in Slice 8) carrying everything Students needs. The **Students**
module consumes it via `EnrollStudentOnStudentEnrolled : IDomainEventHandler<StudentEnrolled>`, which
creates the Student by reusing the same `StudentRepository.CreateAsync` machinery as manual enrolment
(parent + child profile + admission number, one transaction). Admissions never writes a Student; Students
never reads Admissions tables. *Admissions owns prospective learners; Students owns enrolled learners; the
event is the only bridge.*

**Two honest deferrals the maturity test surfaced** (findings, not blockers):
1. **`admitted_student_id` is superseded, not yet dropped.** The legacy `EduTech.Students/Admissions`
   flow is still the live path and still writes the FK, so both coexist. The column retires when the
   legacy flow is decommissioned (a later convergence step), not in Slice 9 — ripping it out now would
   break the live path for no gain.
2. **`StudentEnrolled` should *guarantee* class + DOB + gender for fully-automatic creation.** A Student
   needs all three; the current Admissions model leaves them optional on the offer/application, so when
   any is missing the handler logs and defers to data completion rather than forging a half-formed
   record. Tightening the accepted-offer/application to require them is a small follow-up to the
   Admissions model. Relatedly, the applicant *is* a Child Profile (EDD-007) but the platform does not
   yet publish a Child-Profile-creation contract, so applicant details are carried inline on the event
   with a nullable `child_profile_id` — the seam to close when that contract exists.

Verification each slice: clean `dotnet build` 0/0, full suite green (**501 tests**), migration applied on
throwaway Postgres, and the zero-Foundation-change gate (diff grep) enforced.

## Part 15 — UX Journey

Parent discovers a school → inquires, optionally books a visit → starts an application, saves a draft,
resumes later → submits → uploads documents, sees each verified (or told what's missing) → applicant is
assessed → admissions reviews → an offer arrives with class/fees/deadline → parent accepts → the child
becomes a student and **the family workspace opens**. The officer side mirrors it: triage a cycle,
request missing documents, schedule assessments, review, decide, issue offers, watch acceptances.
