# SatiLogica — platform domain design

*Status: design, nothing implemented. Written 2026-09-07. This is the document
`PLATFORM_RESTRUCTURE_PLAN.md` waits on: what belongs to the platform cannot be decided from one
product, so the shape of Karuna and Upekkha is settled first and the code boundary is derived from
it. Two decisions were taken on 2026-09-07 and are recorded in `DECISIONS.md`.*

---

## Why this comes before the restructure

A platform extracted from a single product is that product's leftovers. `Sati.Contracts` holds
`UserPermissions` beside `BillingRules` today precisely because nothing ever had to tell them apart.
Karuna and Upekkha are what tell them apart, so their design has to exist first even though neither
ships for a long time.

The design is already half-written and scattered. `AGENDA.md` carries an OADS Resource Coordinator
role with cross-agency review of person-centred plans. `DECISIONS.md` separates organization,
directory entry and tenant explicitly "when Karuna arrives". `CLAUDE.md` records cross-tenant
incident telemetry on a separate identity. Those are Karuna and Upekkha fragments found while
building Sati. This document gathers them.

## The three programs

| | Sati | Karuna | Upekkha |
|---|---|---|---|
| Who uses it | Case-management agency staff | Service-provider organization staff | OADS reviewers |
| Tenant | Agency | Provider organization | Not a tenant; an authority |
| Owns | Notes, billing, caseload, compliance forms, assessments | Service delivery documentation against an authorization | Nothing; it reviews and decides |
| Reads across tenants | No | No | Yes, scoped |

Upekkha being an authority rather than a tenant is the load-bearing distinction. OADS does not have
its own consumers; it reviews other tenants' records and records decisions about them.

## Decision 1 — one tenancy model, authority-scoped reads

Membership and authority are separate things.

- **Membership** is which tenant a user belongs to. Every user has exactly one. It scopes everything
  they create and almost everything they read. This is what `AgencyActor` carries today.
- **Authority** is a named, granted, time-bounded capability to reach beyond one's own tenant for a
  stated purpose. It is provisioned outside tenant user-management, confined to an allowlist of
  routes, and audited on every use.

### What exists today, and why it does not generalise

`PlatformOperator` is already a cross-tenant identity and already proves most of the pattern: it is
provisioned outside agency workflows, confined by a path allowlist, and every cross-tenant view is
audited. `API_SECURITY_AUDIT.md` calls its containment sound.

But it is a `UserRole` value on an ordinary `User` row with a non-nullable `AgencyId`, and it is
contained **by subtraction**: five queries carry `Role != "PlatformOperator"` so it does not appear
in agency lists, chat member lists or user management, and ten places branch on the role at all.
Containment by subtraction works for one role that must be excluded from everything. It is the
wrong shape for OADS, which must be *included* in specific things across many tenants, and every
inclusion is a place the exclusion list has to be reasoned about. Adding the Resource Coordinator as a second
excluded role would mean every one of those filters grows a second clause, and the site somebody
forgets is a disclosure.

So the platform introduces authority as its own concept rather than extending the exclusion list.
`PlatformOperator` becomes the first authority rather than a role, which also removes its
non-nullable `AgencyId` fiction.

### The shape

An authority grant names the holder, the authority, the scope it reaches, why it was granted, who
granted it, and when it expires. Reads made under it are audited with the authority recorded, not
merely the user, so an access log can answer "under what claim was this read?" and not only "who
read it?".

Two authorities are known now. `PlatformOperator` reaches incident and health telemetry across all
tenants and no clinical data at all. The OADS Resource Coordinator reaches submitted assessments and
person-centred plans for the waiver programme it is granted, in the agencies that submitted them,
and only while the review is open.

Nothing about Sati's ordinary agency isolation relaxes. `TenantAccess` keeps refusing caller-supplied
scope values; authority is a second gate after that one, never a bypass of it.

## Decision 2 — a platform person registry that products link to

This mirrors the Organization decision in `DECISIONS.md` exactly, and for the same reason.

- The **registry** holds platform-wide canonical identity: name, birth date, and external
  identifiers. Nothing clinical, nothing programmatic.
- Each product keeps its **own local record** with its own columns and its own lifecycle.
- The local record carries a nullable link to the registry.
- **Reconciliation is a link, never a swap.** No product's rows are repointed when a match is made,
  so no foreign key is rewritten and no history is disturbed.

The same human is a consumer in Sati, a service recipient in Karuna and a waiver member in Upekkha.
Those are three local records and one registry entry.

### What this needs from Sati now

`Person.MaineCareId` already exists, so the identifier that matters most is being captured. That is
the `Provider.Npi` lesson already applied once. Two gaps remain, and both are the kind that cannot
be fixed retroactively:

1. `MaineCareId` has no validation or uniqueness constraint comparable to the NPI check digit and
   filtered unique index. Match quality later is entirely a function of capture quality now.
2. `Person.BirthDate` is non-nullable and `FirstName`/`LastName` are nullable, which is the wrong way
   round for a matching key.

The registry table itself is deliberately **not** created yet, on the same reasoning that refused to
add a column pointing at a table that does not exist.

## What changes in Sati now

Three things, all consequences of decisions above rather than new features.

1. **Do not build the OADS Resource Coordinator as an agency user.** `AGENDA.md` already says to add
   the role "and narrow capabilities rather than relying on menu visibility". It should be the first
   authority, not the second excluded role. Building it the current way and migrating later means
   rewriting the exclusion filters and the audit records both.
2. **Strengthen `MaineCareId` capture** before the registry needs it: format validation, a filtered
   unique index per agency, and a decision about what to do with the rows that already disagree.
3. **Stop widening the exclusion pattern.** Any new `Role != "PlatformOperator"` filter is another
   site to change when authority lands. There is a bounded number today; the number should not grow.

None of the three requires the restructure to have started, and all three get harder the longer they
wait. That is what "decisions in Karuna and Upekkha affect decisions in Sati" means concretely.

## What this makes platform, and what it leaves to Sati

Derived from the above rather than guessed:

**Platform.** Identity and authentication. Tenancy and membership. Authority grants and the audit of
their use. The person and organization registries. Audit events and record versions. Documents,
signatures and envelope protection. Incidents and health telemetry. Chat. Legal hold and retention.

**Sati.** Notes and the service timeline. Billing, claims and remittance. Caseload and transfer.
Compliance forms and annual cycles. Comprehensive assessments and person-centred plans. Provider
directory entries. Everything the `Sati.Contracts` classification put in the case-management column.

Karuna and Upekkha will each add their own product column. The platform column is the intersection,
and it stays the intersection only if a test enforces the direction, which is stage one of the
restructure plan.

## Open questions

These need domain knowledge the repository does not contain. None of them blocks the restructure
stages that can start now.

1. **Does Karuna receive work from Sati, or only publish to it?** A case manager authorises a
   service; a provider documents delivering it. If documentation flows back for the case manager to
   read, that is a cross-product read between two tenants and needs its own authority, not just a
   shared registry.
2. **Is an OADS decision a record in Upekkha or in Sati?** `AGENDA.md` insists on preserving the
   distinction between assessment facts, supervisor attestation, plan, OADS decision, classification
   and authorization. Which side of the product boundary the decision row lives on determines
   whether Upekkha is a reviewer of Sati's records or a system of record in its own right.
3. **Do Karuna providers see each other's documentation for a shared person?** Two providers may
   serve the same waiver member. The registry makes that visible to the platform; whether it is
   visible to them is a policy question with regulatory weight.
4. **Which waiver programmes scope an OADS authority?** `WaiverType` exists on `Person` already, and
   Lifespan Waiver support is a roadmap item. Authority scope needs the programme list to be stable.
