# EDD-014 — Admissions (the reference module)

**Status:** DESIGN — Part 1 (dig) + Parts 2–7 & UX designed; 5 decisions locked. Next: tables/
migrations, commands/queries, and the module-extraction implementation plan (define-first, then code).
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

## Part 15 — UX Journey

Parent discovers a school → inquires, optionally books a visit → starts an application, saves a draft,
resumes later → submits → uploads documents, sees each verified (or told what's missing) → applicant is
assessed → admissions reviews → an offer arrives with class/fees/deadline → parent accepts → the child
becomes a student and **the family workspace opens**. The officer side mirrors it: triage a cycle,
request missing documents, schedule assessments, review, decide, issue offers, watch acceptances.
