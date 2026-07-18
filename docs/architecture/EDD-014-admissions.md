# EDD-014 — Admissions (the reference module)

**Status:** DRAFT — Part 1 (the dig) complete; Parts 2–7 + UX Journey to design next.
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

## Part 2 — Desired Product *(to design)*
If SchoolFlow launched today, what should Admissions *feel* like? (Product, not implementation.)

## Part 3 — Domain Model *(to design)*
Aggregates as business concepts (illustrative: Application · Applicant · AdmissionCycle · Offer ·
Decision · DocumentChecklist) — not tables.

## Part 4 — Workflow *(to design — the most important section)*
`Discover → Create application → Upload documents → Pay fee → Assessment → Review → Decision → Offer →
Acceptance → Guardian linked → Membership created → Student enrolled.`

## Part 5 — Platform contracts *(the maturity test)*
Consumes only: Identity · Membership · Organization · `ICapabilityResolver` · Notifications · Audit ·
Storage · Event Publisher. Nothing beneath those public contracts. **No Foundation changes.**

## Part 6 — Events *(to design)*
Published: `ApplicationSubmitted · AssessmentScheduled · AssessmentCompleted · ApplicationReviewed ·
OfferIssued · OfferAccepted · StudentEnrolled`. Consumed: `GuardianLinked · IdentityRegistered ·
PaymentReceived`. (Reconcile the current `Admitted/Rejected/ExamScheduled` into this vocabulary.)

## Part 7 — Capabilities *(to design — actions, not roles)*
`admissions.application.read/review/assign/approve/reject · admissions.offer.issue ·
admissions.assessment.schedule · …` — registered in `CapabilityRegistry`.

## Part 15 — UX Journey *(to design)*
The parent/officer narrative end to end (see MODULE-TEMPLATE §15).
