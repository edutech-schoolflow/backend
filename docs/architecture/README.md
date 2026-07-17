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
| — | Permission Templates — position → capability sets | (partial) |
| — | Access Context — the login-read **projection** of Membership/Employment (regenerable) | (B2) |

### Platform Domain — *cross-cutting services* (school-agnostic)
Events / Event Catalog · Notifications · Documents · Search · Audit.

## The Sacred Six (frozen platform tables)

`identities` · `organizations` · `memberships` · `employments` · `capabilities` ·
`permission_templates`. **`access_contexts` is deliberately *not* here** — it is a disposable
projection, regenerable from Membership/Employment/Guardian, never canonical state.

## Sprint sequence

```
Membership (B1) ✅ → Position (C1) ✅ → Employment (C2) ✅ → Organization (D) ✅ →
Event Catalog → Authentication Finalization (B2: access_contexts projection + slim JWT +
capability resolver)
```

Then domain modules — Admissions first — are built on the frozen platform.
