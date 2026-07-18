# SchoolFlow Architecture — EDD Index

Engineering Design Documents, grouped by the platform domain they belong to. The platform is being
built **foundation-first** via a strangler migration off the legacy actor tables (`school_owners`,
`staff_users`, `parents`): every sprint makes those smaller and adds no new dependency on them.

## Platform domains

### People Domain — *who, and how they relate to an organization*
| EDD | Aggregate | Status |
| --- | --- | --- |
| EDD-001 | Identity — the global person, phone-keyed | ✅ |
| EDD-007 | Membership — the belonging edge (Identity **or** Child Profile ↔ Organization) | ✅ |
| EDD-008 | Position — the job catalog an organization employs into | ✅ |
| EDD-009 | Employment — the working relationship (Membership × Organization) | ✅ |

### Organization Domain — *the organization and its structure*
| EDD | Aggregate | Status |
| --- | --- | --- |
| EDD-010 | Organization — the platform root (account/company that owns everything) | ✅ (shadow root) |
| — | OrganizationUnit — schools/campuses/departments/faculties/houses | (planned) |
| — | Academic Sessions / Calendar | (planned) |

### Authorization Domain — *what someone may do*
| EDD | Concern | Status |
| --- | --- | --- |
| EDD-006 | Capabilities — `resource.action`, roles as capability sets | ✅ |
| EDD-012 | Authentication Architecture — auth projects the platform; the B2 target spec | ✅ (spec) |
| EDD-013 | Capability Resolver — the single server-side authorization API (`context_id`→capabilities) | ✅ (B2b) |
| — | Permission Templates — resolved server-side by `ICapabilityResolver` | ✅ (via EDD-013) |
| — | Access Context — the login-read **projection** of Membership/Employment (regenerable) | ✅ (B2a) |

### Platform Domain — *cross-cutting services* (school-agnostic)
| EDD | Concern | Status |
| --- | --- | --- |
| EDD-011 | Event Catalog — the canonical messaging contract (`AggregatePastTense`, per-event contract) | ✅ |
| — | Notifications · Documents · Search · Audit | (Audit live; rest planned) |

## The Sacred Six (frozen platform tables)

`identities` · `organizations` · `memberships` · `employments` · `capabilities` ·
`permission_templates`. **`access_contexts` is deliberately *not* here** — it is a disposable
projection, regenerable from Membership/Employment/Guardian, never canonical state.

## What SchoolFlow is

An **identity-centric education platform**, not a school-management system. Identity, Organizations,
Memberships, and Capabilities are the *operating system*; a `school` is just one `OrganizationType`.
Admissions, Students, Finance, Workforce, Learning, … are **applications** running on top. Modules
never leak platform concerns back into the foundation.

## Three layers

1. **Platform Core (frozen)** — the OS: Identity · Authentication · Membership · Employment · Position ·
   Organization · Access Context · Event Catalog. Changes extremely rarely.
2. **Platform Integration** — infrastructure connecting core to apps: B2b Capability Resolver ·
   B2c JWT simplification · B2d legacy retirement. After this, authentication is ~invisible.
3. **Product Applications** — where ~90% of future work lives: Admissions, Students (SIS), Academics,
   Attendance, Assessment, Finance, Communication, Transport, Library, Health, Hostel, Store, Workforce,
   Compliance, Analytics. **None may change Layer 1.**

## Governing rules

- **Foundation freeze (until v2):** no new foundational aggregate and no structural rewrite of
  Identity / Membership / Employment / Organization / Position / Authentication. Bug fixes and
  refinements: yes. Redesigns: no. The foundation is done.
- **No authentication rewrites while the foundation is still changing** (satisfied — foundation is complete).
- **Authentication must never resolve permissions** (EDD-012): auth ends at *issue token → context_id*;
  everything after is Authorization.
- **Platform-maturity test (the real next milestone):** *can a brand-new module be built without
  modifying Identity, Membership, Employment, Organization, or Authentication?* When yes, the platform
  is mature.
- **Platform Validation Rule:** **Admissions** is the first module built entirely on the finished
  platform — its **reference implementation** (identity-first, membership creation, event-driven
  workflow, capability authorization, workspace, notifications, audit, clean aggregates). *Admissions
  may not modify the Foundation.* Any Foundation change it needs must be justified as a **defect**, not
  a new concept. If Admissions drops in cleanly, the maturity test is passed and B2c/B2d become
  mechanical cleanup. Every module starts from [MODULE-TEMPLATE.md](MODULE-TEMPLATE.md).
- **Modules depend on the platform only through published contracts** — Identity · Membership ·
  Employment · Organization · Capabilities · Events. A module reaching into another module's tables,
  repositories, or aggregates is a design smell. (Cross-cutting concerns are consumed as **platform
  services** — Notification · Storage · Search · Audit · Calendar · Identity — never as "shared
  utilities"; apps don't know where a service is implemented.)
- **Frozen vocabulary — one word per concept, everywhere** (backend · frontend · DB · API · events ·
  docs): **Identity · Organization · Membership · Employment · Position · Access Context · Capability ·
  Workspace**. Avoid synonyms — *User, Account, Employee, StaffUser, SchoolUser, Tenant, Permission,
  RoleAssignment* — unless a genuinely distinct concept. Governs **new** code and the target state;
  legacy synonyms (`staff_users`, `school_owners`, `TenantRepository`) are being *retired* by the
  strangler (through B2d), not renamed in place.

## Roadmap

```
Platform Foundation ✅  (Identity · Membership · Position · Employment · Organization · Event Catalog)
        ↓
Platform Integration    B2a ✅ Access Context projection · B2b ✅ Capability Resolver · B2c JWT slim · B2d legacy retirement
        ↓
Core Product Modules    Admissions → Students → Academics → Finance → Workforce → …   (vertical slices:
        ↓                Domain · Repository · Events · API · Frontend · Tests — no foundation changes)
Platform Services · Marketplace / Ecosystem
```

**Sequencing note:** after **B2b**, build **Admissions** on the new platform *before* B2c — it validates
the abstractions and surfaces missing seams while the legacy compat layer is still present, turning B2c
into low-risk cleanup rather than a leap of faith.
