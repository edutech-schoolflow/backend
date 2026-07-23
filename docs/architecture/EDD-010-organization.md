# EDD-010 — Organization

**Status:** Sprint D implemented (shadow root: table + aggregate + backfill)
**Domain:** Organization · **Sacred Six** member · **platform root**
**Builds on:** EDD-007 Membership

---

## 1. Definition

> **Organization is the platform root — the account/company that owns everything.**

Like Slack's Workspace (which owns channels, members, apps, permissions — everything hangs off one
root), the Organization owns Schools, Memberships, Employments, Positions, and branding. It is **not**
one of those children. Identity answers "who is this person?"; Organization answers "who is this
institution?" — and it stays as **boring as Identity**, owning only institutional identity.

Today the codebase treats a `school` *as* the organization (`schools.slug` = the org slug,
`access_contexts.organization_id → schools`, the onboarding wizard). That is backwards. EDD-010
creates the real root and links each school *up* to it.

---

## 2. The aggregate — stay boring

```
Organization: id · name · slug · type · status · owner_membership_id? · created_at
```

- **`type`** — `school · university · training_centre · tutor · corporate · ngo`. Only `school` is
  used today; the enum makes SchoolFlow an **education platform**, not a school-management system, for
  ~zero cost.
- **`status`** — `active · suspended · archived` (lifecycle: Create → Rename → Suspend → Archive →
  Activate).
- **`owner_membership_id`** — the owner as a **Membership** (EDD-007), not a `school.owner_id`.

**Not** in Organization: academic sessions, students, invoices, calendars, curriculum. Branding
(logo, colours, domain, timezone, currency, country) is Organization-owned *eventually* but stays on
`schools` for now — kept out to keep the root boring.

---

## 3. Invariants

1. An Organization has a name, a slug, and a valid type.
2. The slug is stable — it is the URL identity; renames change the name only.
3. Organization owns institutional identity **only** — never the children's business data.
4. The owner is expressed as a Membership, never a raw actor id.

---

## 4. School → OrganizationUnit (the future)

`schools` is overloaded. The direction:

```
Organization → OrganizationUnit → { Primary School · Secondary School · Campus · Training Centre · … }
                               → { Departments · Faculties · Houses }
```

A "Divine Wisdom Group" with Nursery/Primary/Secondary, or Lagos/Abuja/Ibadan campuses, is **one
organization** with many units. This is also where `employments.organizational_unit_id` (EDD-009)
finally gets its home. Not built now — Organization first, units later.

---

## 5. Shadow root — the strangler discipline

Sprint D creates the root and backfills it (one org per existing school, 1:1, keyed on the school's
unique slug; `owner_membership_id` from the owner membership). It is a **shadow root**: nothing
re-points to it yet.

- `memberships` / `employments` / `positions` / `access_contexts` keep referencing `schools`.
- The school-as-org auth/onboarding layer (`AuthContextRepository` org methods,
  `OrganizationOnboardingController`) is untouched.
- The `EduTech.Organization` aggregate + repository exist and are unit-tested, but have no production
  reader yet.

Re-pointing FKs, moving branding, cutting the owner over to `organization.owner_membership_id`, and
making onboarding organization-first are **later, isolated sprints** — so each migration stays
focused and reversible.

---

## 6. Events — reserved, not yet built

`OrganizationCreated · OrganizationRenamed · OrganizationSuspended · OrganizationArchived ·
OrganizationActivated` (built in the Event Catalog sprint).

---

## 7. Non-goals (Sprint D)

No FK re-pointing; no change to the school-as-org auth layer; no keep-current wiring for newly
created schools (the migration backfills all existing schools; convergence for new ones lands with
the onboarding-org-first migration); no branding move; no School→OrganizationUnit reframe; no
auth/JWT/workspace/frontend/permissions changes.
