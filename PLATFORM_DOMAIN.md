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
| Tenant | Agency | Provider organization | Not a tenant |
| Owns | Notes, billing, caseload, compliance forms, assessments | Service delivery documentation against an authorization | Waiver decisions |
| Reads across tenants | Yes, by relationship | Yes, by relationship | Yes, by authority |

Upekkha is not a tenant because it has no consumers of its own. It is still a **system of record**:
it owns the decision rows, which then flow into Sati. Reviewing and deciding are different things,
and Upekkha does both.

## Two kinds of cross-tenant access

The first draft of this document said no product reads across tenants except OADS. That was wrong,
and the correction is structural rather than a detail. There are two mechanisms and they must not be
built as one.

**Authority** is granted, programme-scoped and exceptional. Someone outside a tenant is given a
named capability to reach in for oversight, for a bounded time, audited on every use.
`PlatformOperator` and the OADS Resource Coordinator are the two holders.

**Relationship** is derived, record-scoped and ordinary. A case manager authorises a service; a
provider documents delivering it; the case manager reads that documentation. Nobody grants this and
nothing about it is exceptional. It is the normal operation of the platform, and it is what makes
Karuna worth building at all.

Conflating them would be a serious mistake in both directions. Modelling relationship reads as
authority grants would mean provisioning a grant per case manager per provider per person, which
collapses under its own weight. Modelling authority as a relationship would give OADS access by
being adjacent to a record rather than by a decision someone made and can revoke.

### The authorization is the join, not the person

A provider documents against an **authorization**, and the case manager who owns that authorization
reads that documentation. The join is the authorization, not the person.

That distinction bounds the read precisely. "Any case manager who has ever served this person" is a
much wider door than "the case manager whose authorization this documentation was written against",
and the wider door is the one that leaks when caseloads transfer, when a person moves agency, or
when an old episode closes. `CaseloadTransferRules` already exists in Sati and would have to be
taught about Karuna if the join were the person; joining on the authorization means transfer moves
the authorization and the visibility follows it.

## The OADS decision flows as a snapshot, not as a live read

Upekkha owns the decision. Sati receives it and keeps an immutable snapshot of what it was told,
alongside a link to the owning row.

The reason is billing reproducibility, not tidiness. If billability depends on an authorization and
Sati reads Upekkha's decision live, then an amendment in Upekkha silently changes the answer to a
question Sati already answered. A claim generated in March becomes unexplainable in June, and
`BillingComplianceGate` cannot be asked why it passed. `CLAUDE.md` already requires immutable
versions and amendments rather than silent overwrites for submitted clinical and financial records,
and `ProfessionalClaimSnapshot` is the same pattern applied once already.

So: Upekkha never writes into Sati's tables, and Sati never gates a claim on a live cross-product
read. Sati snapshots the decision at receipt, records when and from which version, and an amendment
in Upekkha arrives as a new snapshot rather than a mutation of the old one. `AGENDA.md` already
insists on preserving the distinction between assessment facts, supervisor attestation, plan, OADS
decision, classification and authorization; separate rows on separate sides of the product boundary
is what makes that distinction survive contact with an amendment.

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

## Sharing defaults, and the one place the default cannot hold

Provider documentation for a shared person is visible to the case manager and to OADS by default,
with the ability to disable some sharing. That is right for the ordinary case and it cannot be right
for all of it, because `DECISIONS.md` already settled the harder half:

> A profile answers who someone is, never what they agreed to. [...] Deriving any of it from stored
> data would manufacture a consent nobody gave.

The agency release form records three categories separately, and it records them because they are
separately protected: substance use treatment, mental and behavioural health treatment, and HIV
status. Substance use records fall under 42 CFR Part 2, which is stricter than HIPAA and governs
redisclosure specifically. A default that shares them until somebody turns sharing off is a
disclosure decided by a default rather than by a consent anybody gave, which is the exact thing that
decision refuses.

So the model has two layers, and the difference between them is who decides.

**Ordinary documentation is default-open across the relationship.** A case manager reading the
documentation of a service they authorised is care coordination inside an existing treatment
relationship, not a third-party disclosure. No consent artefact gates it. This is the common case
and it should stay frictionless.

**Specially protected categories are default-closed and travel only on a recorded release.** Not a
toggle, and not a setting anybody administers: the signed release that already exists in Sati is the
evidence, and the categories it names are the categories that travel. If no release covers them,
they do not cross, and the reader is told that something exists and is withheld rather than being
shown a gap that looks like an absence.

**Suppression is two different things and must not be one switch.** A provider marking a document
not-yet-shareable because it is a draft is operational, reversible and needs no evidence. A category
withheld for want of consent is regulatory, evidenced by the absence of a release, and no user may
override it. Building both as one "share" flag would let an operational click do a regulatory job.

**This needs legal review before it is built, not after.** Whether Part 2 data reaches OADS at all
is a question about regulatory oversight authority, and whether provider-to-case-manager flow is a
disclosure or an internal treatment use depends on how the organizations relate under HIPAA.
`REGULATORY_CONCERNS.md` already lists cross-agency access and specially protected records as
counsel questions. This design assumes the conservative answer until someone qualified says
otherwise, because the conservative answer is the one that is safe to relax later.

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
**The authorization**, and **the sharing policy that decides what crosses a tenant boundary**.

**Sati.** Notes and the service timeline. Billing, claims and remittance. Caseload and transfer.
Compliance forms and annual cycles. Comprehensive assessments and person-centred plans. Provider
directory entries. Everything the `Sati.Contracts` classification put in the case-management column.

The last two platform entries are the ones that could not have been found by looking at Sati alone,
and they are why this document had to come before the extraction.

**The authorization** looks like a Sati concept right up until Karuna documents against it and
Upekkha decides it. It is the join for every relationship read, it is what an OADS decision resolves
into, and it is what a caseload transfer moves. Left in Sati, Karuna would have to reference a
sibling product to know what it is delivering, which is the exact coupling this restructure exists to
remove.

**The sharing policy** decides whether a given reader may see a given record across a tenant
boundary, and it has to have one owner. `CLAUDE.md` calls a rule enforced two different ways a
defect rather than a convenience, and a disclosure rule enforced two different ways is a defect with
a regulator attached. It belongs beside `TenantAccess`, not inside any product.

Karuna and Upekkha will each add their own product column. The platform column is the intersection,
and it stays the intersection only if a test enforces the direction, which is stage one of the
restructure plan.

## Answered 2026-09-07

1. **Karuna documentation flows back to the case manager.** Answered yes. This is what produced the
   relationship mechanism above; it is ordinary operation, not an authority grant.
2. **An OADS decision is a record in Upekkha that flows into Sati.** Answered: Upekkha owns it, Sati
   snapshots it. Upekkha is a system of record, not only a reviewer.
3. **Provider documentation for a shared person is visible to case managers and OADS by default,
   with some sharing disableable.** Answered, with the two-layer qualification above: the default
   holds for ordinary documentation and cannot hold for the three specially protected categories.

## Open questions

These need domain knowledge or counsel the repository does not contain. None blocks the restructure
stages that can start now.

1. **Which waiver programmes scope an OADS authority?** `WaiverType` exists on `Person` already and
   Lifespan Waiver support is a roadmap item. Authority scope needs the programme list to be stable.
2. **Do Karuna providers see each other's documentation for a shared person?** Two providers may
   serve the same waiver member. Answer 3 covers case managers and OADS but not provider-to-provider,
   which has no authorization joining the two and so no relationship under the model above. The
   default should probably be no.
3. **Does a Part 2 category reach OADS at all?** Regulatory oversight is a different basis from care
   coordination, and the answer may differ from the case-manager answer. Counsel question.
4. **What happens to visibility when an authorization ends?** Documentation written under a closed
   authorization still exists. Whether the case manager keeps reading it, and for how long, is a
   retention question that `OPERATIONS.md` and legal hold both touch.
5. **What does a case manager see when a category is withheld?** Being shown nothing and being shown
   "something exists and is withheld" are different, and only the second is honest. The second may
   itself disclose the existence of protected treatment, which under Part 2 can be the disclosure.
