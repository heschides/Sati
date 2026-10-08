<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Comprehensive Assessment and Person-Centered Plan

### Assessment scope is waiver-agnostic

The Comprehensive Assessment describes a MaineCare member with IDD and/or ASD who
receives case management. It is not a Section 21 form or a Section 29 form. Waiver and
level-of-care determinations live in Classification so the same assessment can support
Section 21, Section 29, and a future Lifespan Waiver without duplicating the person's
story or creating waiver-shaped answers.

### Assessment cadence is intake plus annual, due 60 days before PCP

> Superseded 2026-09-14: the annual CA is due 90 days before the target effective
> date and available 120 days before it. The text below records the temporary 60-day design.

An assessment is required at intake and annually. The annual assessment due date is 60
days before the PCP anniversary. Sati's existing `Form` deadline/reminder system remains
the canonical scheduler. The new migration updates a legacy default setting of 120 to
60; existing generated form rows require a separate inspected reconciliation.

### One team assessment, with dissent preserved explicitly

The ordinary answer represents the team's assessment. Sati does not force separate
"person says," "guardian says," and "case manager says" fields on every question.
Where participants disagree, the record preserves a differing perspective, its author,
discussion/resolution, and whether disagreement remains. The main answer must not be
rewritten to conceal dissent.

### Question-specific response design instead of a universal support scale

Different questions use response structures appropriate to the subject. Support is not
a single ordinal level. Where support characteristics are relevant, setup/environment,
prompting/coaching, hands-on assistance, another person completing part or all of an
activity, and variation by situation may be selected together.

`No support currently needed`, `Not applicable`, and unavailable-answer dispositions are
exclusive alternatives. `Varies` requires at least one concrete support selection and an
explanation. Every question must also provide practical guidance describing why it is
asked, what a valid answer contains, realistic examples, and answers to avoid.

### No silent blanks and no false completion

Every question must be substantively answered or assigned an explicit disposition such
as not applicable, declined, unable to assess, or follow-up required. A follow-up-required
answer blocks completion. The assessment cannot be submitted as complete while required
content remains unresolved.

### Needs are reusable domain objects

Material needs and broader support, skill, access, health/safety, relationship,
autonomy/rights, and planning needs are stored separately from narrative answers. A need
may reference a provider, PCP goal, desired result, action, and resolution history.
Changing a provider must not erase the underlying need. Approved documents retain a
snapshot even when directory information later changes.

### Authorship follows caseload ownership

Only the case manager assigned to the consumer may author that consumer's assessment.
A supervisor who carries a caseload may author assessments for those assigned consumers,
but supervisory status does not permit rewriting another case manager's answers.
Supervisors review by flags, comments, returns, and wholesale approval.

### Assessment requires supervisor approval; OADS approval begins at PCP

The Comprehensive Assessment ends with supervisor approval. OADS Resource Coordinators
do not approve the assessment. The approved assessment informs the PCP; the PCP is the
document submitted for OADS review and wholesale approval. Resource Coordinators may
flag particular PCP sections and return the plan but do not silently edit the submitted
record.

### Live profile, immutable approved documents

The consumer profile is the live source of truth. Assessments and PCPs may draw from it,
but an approved or signed document version is immutable. Profile changes may update live
context and create PCP change candidates; they never silently rewrite the operative plan.
Predefined rules classify impact, the case manager confirms applicability, and important
changes require supervisor review.

### Physical signatures retain both artifacts

Both Comprehensive Assessments and PCPs will support generated PDFs, physical signatures,
and upload of signed scans. Sati retains the generated PDF and signed scan against the
same exact version. Any substantive later edit creates a new version and signature cycle.

### PCP meeting and authorized-services record

The PCP records all meeting participants and assumes the assigned case manager organized
the meeting unless explicitly changed. Authorized services belong in the PCP. A future
top-level Providers directory will supply provider information, but each approved PCP
retains its own authorization/provider snapshot.

### Compliance and billing gaps are permanent

> Clarified 2026-09-14: the due day itself is billable; blocking begins the next day.
> Ordinary late completion does not repair the gap, but an explicit Admin recovery decision may
> release selected eligible notes after compliance is restored.

There is no grace period after a PCP or 90-day review deadline. At midnight after the due
date, newly submitted case notes are retained as service documentation but are permanently
unbillable. Later completion does not restore billability for the gap. Supervisors and
higher roles may override an overdue assessment solely for PCP submission, with a reasoned
audit record; this does not imply a billing override.

