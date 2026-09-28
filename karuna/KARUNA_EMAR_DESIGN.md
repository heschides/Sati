# Karuna — electronic medication administration record (eMAR) design

*Status: design only; nothing implemented. Written 2026-09-28. Read `KARUNA_DESIGN.md` first,
especially §1.3 (clinical facts versus claims), §4 (access and shared devices) and §5 (time). No RN,
pharmacist, licensing reviewer or counsel has reviewed this document. **An RN clinical advisor must
review the workflows in §3–§10 before any synthetic build is shown to an agency as a medication
system, and before any real use.***

---

## 1. What the eMAR is, and what it is not

**It is** the organization's record of what was ordered, what was due, and what actually happened
at each dose — by whom, when, and why not when it was not given — with the controls that make that
record trustworthy: verified orders, derived schedules, eligibility, counts, and routing of anything
that went wrong.

**It is not** a clinical decision-support system. Karuna does not check drug interactions,
therapeutic duplication, dose ranges or cross-sensitivities, and it does not calculate doses. The
prescriber and the dispensing pharmacy are responsible for those. Software that performs them can fall
within FDA device definitions depending on its function; adding any of it needs regulatory review
first (`KARUNA_REQUIREMENTS.md` §4.10). Every screen that might be mistaken for such a check says
what it does and does not compare.

**The governing principle** (from `KARUNA_DESIGN.md` §1.3): *the record says what happened; rules
decide what happens next.* The eMAR prevents mistakes **before** the fact — the dose row shows that
it was already given, the PRN row shows that the maximum was reached, the screen shows the person's
photo. It never refuses to record, **after** the fact, a dose that was given. A refused truthful
entry does not un-give the medication; it removes the only evidence that it was given.

---

## 2. Concepts

| Term | Meaning |
|---|---|
| **Medication product** | What is administered: an RxNorm concept (ingredient, strength, dose form), optionally an NDC for the dispensed package, and a controlled-substance schedule. |
| **Order** | A prescriber's instruction for one person: product, dose, route, schedule or PRN parameters, hold parameters, instructions, start and stop. Immutable once active. |
| **Order change set** | The atomic replacement of one or more active orders by new ones (discontinue + start), with one source document and one verification. |
| **Scheduled dose** | A derived, never-stored occurrence of a scheduled order on a local date, with a stable identity. |
| **Administration record** | The append-only fact of what happened for a scheduled dose or a PRN event. |
| **Exception flag** | A system classification attached to an administration record or to an undocumented dose (late, omitted, double, ineligible administrator, over PRN limit, …). |
| **Medication rule set** | The site's versioned policy: windows, follow-up intervals, count cadence, renewal periods, witness requirements (`KARUNA_DESIGN.md` §6). |

---

## 3. Orders

### 3.1 Content

An order records: the person; the product (RxNorm concept, free-text fallback flagged as uncoded);
dose amount and unit; route; the schedule (§4.1) or PRN parameters (§8); hold parameters (for
example "hold if pulse below 60", with the vital sign that must be captured); administration
instructions; start and stop instants; prescriber identity; the **source** (written order, pharmacy
label, phone/verbal order, hospital discharge summary); the source document artifact; transcriber;
and flags for psychotropic and controlled status.

### 3.2 Lifecycle

`MedicationOrderLifecycle` owns the transition table.

| From | To | Who | Condition |
|---|---|---|---|
| Draft | PendingVerification | Transcriber (`Nursing`, or `DirectSupport` where the site rule set allows transcription) | Order-entry validation passes (§3.7) |
| PendingVerification | Active | Verifier, **not the transcriber** | Verifier qualification (§3.4); source document attached |
| PendingVerification | Draft | Verifier | Returned with reason |
| Active | OnHold | `Nursing` | Prescriber hold instruction recorded |
| OnHold | Active | `Nursing` | Prescriber resume instruction recorded |
| Active, OnHold | Discontinued | `Nursing` or verifier | Discontinue instruction recorded; effective instant |
| Active | Expired | System | Reached stop instant or maximum validity (§3.6) |
| Active | Superseded | Order change set | Replacement order became Active in the same transaction |

- **An active order is never edited.** A dose change, time change or route change is a change set:
  the new order and the discontinuation of the old one take effect at one instant, verified once.
  Doses already documented under the old order keep pointing at it.
- **An order is not administrable until Active.** Draft and PendingVerification orders appear on the
  MAR as "awaiting verification" so staff know a change is coming, without a record button.

**Rejected:** editing an active order in place. It silently rewrites what the MAR said at 8:00 AM,
which is precisely what a surveyor reads.

### 3.3 Transcription and verification

Most group homes receive orders as pharmacy labels, faxed prescriber orders or discharge paperwork.
Karuna's first release has no pharmacy interface, so every order is transcribed by a person and
verified by a second person against the source document. Verification is a checklist the verifier
affirms — person, product, strength, dose, route, frequency and times, PRN parameters, start/stop,
allergies reviewed — recorded as an append-only `OrderVerification` row, like Sati's attestations.

### 3.4 Who may verify

A **qualification**, not a capability (`KARUNA_DESIGN.md` §4.3): the site rule set names which
credentials may verify (proposed default: RN or LPN; MQ-2 asks whether CRMA-level verification is
acceptable anywhere). The verifier must hold it at the instant of verification, evaluated by
`StaffQualificationRules`.

### 3.5 Phone and verbal orders

Recorded with the receiving person, time and read-back affirmation; the receiver must hold the
qualification the rule set requires to take a verbal order (MQ-3). A phone order may become Active
after verification, carrying a **written-confirmation due** date (proposed from 10-144 ch. 113: five
working days, via `BusinessDayCalendar`). An overdue confirmation is a nurse-queue item, not an
automatic discontinuation.

### 3.6 Renewal and expiry

`OrderRenewalRules`, from the site rule set: maximum validity (ch. 113: 12 months), psychotropic
reissue period (ch. 113: 3 months). Renewals due in the next 30 days appear on the nurse queue.
Expiry at the limit is a system transition, never a silent continuation; an expired order's doses
stop appearing, and the nurse queue says why.

### 3.7 Order-entry validation (planning — may refuse)

Order entry is planning, so it is gated:

- PRN orders require indications, a maximum per 24 hours and a minimum interval.
- Psychotropic PRN orders require the symptoms, exact dose, interval and 24-hour maximum (ch. 113).
- An antipsychotic ordered PRN-only is refused where the rule set adopts ch. 113's prohibition.
- Hold parameters must name a capturable measurement.
- An ingredient that matches a recorded allergy (§3.8) requires the verifier's explicit acknowledgment
  with a reason; it does not block, because the prescriber may have decided knowingly.

### 3.8 Drug identity and the allergy reminder

Products are identified by RxNorm concepts so that ingredient matching is possible and uncoded text
is visible as such. Licensing and update cadence for the RxNorm data Karuna ships need review (MQ-7).

The allergy reminder compares **the order's ingredients** to **allergies recorded as ingredients**.
It does not check drug classes, cross-sensitivities or uncoded allergies. Its text says exactly that,
every time, and it never displays "no allergy conflicts" — only "no ingredient match with coded
allergies; the pharmacy's review is authoritative". An empty result from a partial check must not
read as safety.

---

## 4. The schedule

### 4.1 Schedule kinds

- **Fixed local times** on every day, on days of the week, or every N days from an anchor date.
- **Interval** schedules (every 6 hours) anchored to the order's start **instant**, not to wall-clock
  times, so they stay six real hours apart across daylight-saving changes.
- **Tapers** as an ordered list of segments (dose and schedule per date range) within one order.
- **One-time** doses at an instant.
- **PRN** orders have no schedule (§8).

### 4.2 Scheduled-dose identity

`MedicationScheduleRules.DosesFor(order, localDate, timeZone)` derives scheduled doses. Each has a
stable identity — `(OrderId, LocalDate, SlotKey)`, where the slot key is the order's configured slot
(for fixed times) or the interval index (for interval schedules) — and a resolved instant.

Doses are **derived, never stored**, following Sati's "the pending list is derived, never stored" and
annual-identity decisions. Only administration records are stored, and each references the dose
identity it documents. An order change after documentation cannot orphan a record; a derived dose that
no longer exists because an order was discontinued simply stops appearing.

**Rejected:** materializing a row per future dose. It creates thousands of rows that must be rewritten
on every order change, and a stale materialized dose is a MAR showing a medication that was stopped.

### 4.3 Daylight saving

For fixed local times:

- A time that does not exist (02:30 on the spring-forward date) resolves to the first valid instant
  after the gap (03:00), and the dose row says so.
- A time that occurs twice (01:30 on the fall-back date) resolves to its first occurrence.

Interval schedules are unaffected because they are anchored to instants. Both transition dates have
required tests (§17).

### 4.4 Absence

A `PresenceInterval` (leave of absence, hospital stay, day program away from the site) comes from
the person record. Scheduled doses falling in an absence are derived as **Away** rather than due, and
the medication handoff for the absence is recorded (§12.2). Doses due during a hospital stay are not
the organization's to document.

---

## 5. The state of a dose

Derived by `AdministrationWindowRules` from the dose instant, the rule-set window, the current
instant and any administration record. Proposed default window: 60 minutes either side of the
scheduled time (MQ-1 asks the agency and advisor to set it).

| State | Meaning |
|---|---|
| Upcoming | Before the window opens. |
| Due | Inside the window, no record. |
| Documented — on time / early / late | A record exists; timing classified against the window. |
| Undocumented | Window closed, no record, still within the documentation grace period. Visible on the house screen and the nurse queue. |
| Omission suspected | Grace period ended, no record. Raises an exception flag and a medication-error event-report **draft**. |
| Away | Person absent (§4.4). |

"Undocumented" and "omitted" are deliberately different: a dose given and not yet documented is a
documentation problem; a dose not given is a medication error. Only a human can say which, and the
omission draft asks exactly that question.

---

## 6. Recording an administration

### 6.1 Outcomes

| Outcome | Required |
|---|---|
| Given | Actual instant, dose given, route; site of application for patches and injections; hold-parameter measurement if the order has one |
| Refused | Reason; re-offer attempts where the rule set requires them |
| Held | Which hold parameter and the measured value, or the clinical instruction relied on |
| Not available | Reason; creates a pharmacy follow-up item |
| Self-administered, observed | Per the person's self-administration assessment |
| Away | Only when an absence exists; links to the handoff record |
| Given by another party | For example a day program; who and the source of the report |

### 6.2 Prevention before the fact

The dose screen shows, before any button: the person's photo, name and date of birth; an allergy
banner; the order exactly as verified; the dose state; **and any existing record for this dose**
("Given by Dana R. at 8:02 AM"). For a PRN it shows the last dose, the count in the rolling 24 hours
and the next permitted instant. For a hold parameter it asks for the measurement first. This is the
intended prevention layer, and it only works online, which is one reason the first release is
online-first (`KARUNA_DESIGN.md` §13.3).

### 6.3 What the server refuses

Only integrity failures: the user lacks reach or capability; the order belongs to another person or
organization; the stated instant is in the future beyond a small tolerance; the revision is stale on a
correction; the idempotency key (the record's own id) already exists with different content.

**Everything else is accepted and classified.**

### 6.4 Classification and routing

`MedicationErrorClassifier` attaches exception flags. Each flag has a routing rule in the rule set:
nurse queue, immediate notification, and/or a medication-error event-report draft.

| Flag | Condition |
|---|---|
| Possible double administration | A second Given for the same dose identity or a PRN inside its minimum interval |
| Over PRN limit | Rolling-24-hour count or amount exceeded |
| Outside window | Given early or late beyond the window |
| Omission suspected | §5 |
| Wrong dose | Dose given differs from the order |
| Inactive order | Given against a Discontinued, Expired, OnHold or unverified order |
| Hold parameter not met | Given with a measurement inside the hold range, or without the required measurement |
| Ineligible administrator | §7 |
| Wrong person | Recorded on one person, then corrected to another (§6.5) |

Classification never decides reportability. A medication-error draft asks the reviewer whether it
"affected health or safety" (the Chapter 12 criterion) and records the answer and basis.

### 6.5 Corrections

An administration record is never edited or deleted. `AdministrationCorrection` is an append-only row
referencing the original, with a kind — **documentation error** (the dose was given; the entry was
wrong: wrong time, wrong person's record) or **retraction** (the record should not exist) — a reason,
the corrector, and the corrected values. The MAR shows the original struck through with the correction
beside it, as a paper MAR would. A correction that moves a record to another person is itself a
wrong-person flag, because it means a record sat on the wrong chart.

### 6.6 Witnesses

Where the rule set requires a second person (for example insulin dose verification, or a controlled
substance wastage), the witness authenticates on the same device for that act (`KARUNA_DESIGN.md`
§4.6). The witness's identity is recorded as a separate authenticated participant, never typed in.

---

## 7. Who may administer

`MedicationAdministrationEligibility.Evaluate(staff, person, order, instant)` returns **Eligible**
with its basis, or **NotEligible** with reasons. Bases, as rule-set data from Section 21.10 and the
site rule set:

- a current credential: RN, LPN, CNA-M, CRMA (with the biennial refresher where required);
- a **person-specific delegation**: an RN-trained, dated, expiring authorization for this staff member
  to give this person a named medication or class by a named route (for example a rescue seizure
  medication, insulin, an epinephrine auto-injector), with the delegating nurse and training evidence.

Before the fact, an ineligible user sees why ("CRMA expired 2026-09-01") and is offered the truthful
records they can make: "not given — no eligible staff present", which notifies the on-call nurse and
supervisor. If an ineligible person did give the medication — an emergency, a staffing failure — they
can record it, and the record carries the ineligible-administrator flag and an event-report draft.
The system does not pretend it did not happen.

**Rejected:** hiding the record button from ineligible users entirely. In the emergency case it
produces either no record or a record in someone else's name.

---

## 8. PRN medications

- The administration requires an **indication** chosen from the order's list and a short
  pre-assessment.
- `PrnLimitRules` computes, over a rolling 24 hours from the proposed instant, the dose count, total
  amount and minimum-interval compliance, and shows them before recording. Violations after the fact
  are flags (§6.4).
- Every PRN creates a **follow-up due item** at the rule set's interval (proposed 60 minutes, MQ-5):
  effectiveness and any adverse effect. An unrecorded follow-up is a nurse-queue item.
- Psychotropic PRN use is separately visible on the nurse and behaviour-support queues, because it can
  be a chemical restraint under 14-197 ch. 5 when used to control behaviour outside an approved plan.
  `RestrictiveInterventionRules` (Karuna design §9.6) decides that classification, not the eMAR.

---

## 9. Refusals, holds and unavailability

- Refusals record the reason and re-offer attempts where required. The rule set defines a notification
  threshold (proposed: the nurse is notified on a second consecutive refusal of the same medication,
  or immediately for medications the order marks as critical).
- Holds record the measurement or instruction.
- Not available creates a pharmacy follow-up item and notifies the supervisor; it is not an omission
  if documented within the window, but it is visible on oversight dashboards.

---

## 10. Controlled substances

- **Count sessions** per site at shift change: for each controlled product on hand, the outgoing and
  incoming staff count together; both authenticate; each count line records expected (derived) and
  counted quantities.
- **Schedule II**: a daily count on days the medication is used and a weekly inventory (ch. 113),
  scheduled as due items by `ControlledCountRules` from the site rule set.
- **Discrepancy** locks the session into a resolution workflow owned by `Nursing`: recount, find the
  cause, record the resolution; an unresolved discrepancy after the rule-set interval creates an
  event-report draft and notifies the coordinator.
- **Disposal and wastage** require a witness and a reason and reduce derived on-hand.
- **The count log** is append-only, with a gapless per-site-per-product sequence number so a missing
  entry is detectable. Whether an electronic log satisfies ch. 113's "bound numbered book" language is
  MQ-4; until answered, Karuna prints the log in a numbered form on demand and does not claim the
  electronic log replaces the book.

---

## 11. Inventory

Receipts from the pharmacy (quantity, fill date, expiry, pharmacy, package NDC where available) and
disposals are evidence rows. On-hand for countable forms is derived: receipts − administrations −
disposals ± reasoned adjustments. Refill-due items forecast from on-hand and the schedule.
Discontinued and expired stock creates a disposal-due item (ch. 113: within 30 calendar days).
Non-countable forms (liquids, creams) track receipts and disposals without derived counts.

---

## 12. Other records

### 12.1 Self-administration

A `SelfAdministrationAssessment` per person records the level of assistance determined with the
person, their representative and the practitioner, the date and the review date. Orders the person
self-administers show "self-administered — observe" or no schedule, according to the assessed level.

### 12.2 Leave of absence

Medications sent with the person are counted out and back with two authenticated participants where
possible, or one staff member and the receiving adult's name. Doses during the absence derive as Away
(§4.4). A report of doses given by the family is recorded as "given by another party" if the agency
collects it.

### 12.3 Treatments (TAR)

Non-medication treatments — skin checks, repositioning, CPAP, wound care — are orders of kind
Treatment on the same schedule engine and administration model, without a product or dose.

---

## 13. Downtime and back-entry

- **Downtime MAR**: a PDF per site for a chosen 24-hour window — person, photo, allergy banner, active
  orders, scheduled doses with blank initial and time boxes, PRN orders with limits and the last PRN
  given, hold parameters, and a **signature legend** mapping initials to full names and credentials.
  Generated on demand; every generation is audited (`karuna.emar.downtime-mar.printed`). Agencies are
  expected to print at the start of each shift or each day by policy.
- **Back-entry**: after an outage, each paper entry becomes an administration record with provenance
  **Downtime paper**, the true administration instant, the entering user, and an optional (strongly
  recommended) scan of the paper sheet as an artifact. Late-entry rules apply; the label shows
  everywhere.
- **Reconciliation**: back-entry against a dose that already has an electronic record raises the
  double-administration question for a nurse to resolve, rather than being merged.

---

## 14. Nurse oversight

Queues, each bounded and site-scoped: orders pending verification; phone orders awaiting written
confirmation; renewals and psychotropic reissues due; undocumented and omission-suspected doses;
PRN follow-ups missing; refusal patterns; holds; exception flags awaiting review; count discrepancies;
delegations expiring; disposal due. A weekly MAR review attestation per site records that a nurse
reviewed the week (MQ-6 asks whether this is required or merely good practice).

---

## 15. Rule owners (`Karuna.Contracts.V1`)

| Owner | Rule |
|---|---|
| `MedicationOrderLifecycle` | Order transition table (§3.2), change-set atomicity, who may make each move. |
| `OrderEntryValidation` | Planning refusals and acknowledgments at order entry (§3.7). |
| `OrderRenewalRules` | Validity, psychotropic reissue, phone-order confirmation due dates. |
| `MedicationScheduleRules` | Scheduled-dose derivation, identity and DST resolution (§4). |
| `AdministrationWindowRules` | Dose state and timing classification (§5). |
| `MedicationAdministrationEligibility` | Eligibility bases and reasons (§7). |
| `PrnLimitRules` | Rolling-24-hour counts and amounts, minimum interval, follow-up due (§8). |
| `MedicationErrorClassifier` | Exception flags and their routing (§6.4). |
| `ControlledCountRules` | Count cadence, required participants, discrepancy handling, sequence integrity (§10). |
| `MedicationInventoryRules` | Derived on-hand, refill forecast, disposal due (§11). |
| `AllergyIngredientMatch` | The ingredient-only comparison and its mandatory explanatory text (§3.8). |

---

## 16. Persistence

All in the `karuna` schema, `OrganizationId` non-null, composite foreign keys.

| Entity | Mutability |
|---|---|
| `MedicationProduct` (organization's formulary view of RxNorm concepts) | Reference data, versioned |
| `MedicationOrder`, `OrderScheduleSegment`, `PrnParameters`, `HoldParameter` | Immutable after Active; Draft rows mutable with revision |
| `OrderChangeSet`, `OrderVerification`, `OrderStatusEvent` | Evidence |
| `PhoneOrderConfirmation` | Evidence |
| `AdministrationRecord` | Evidence (insert-only; DB denies update/delete) |
| `AdministrationCorrection`, `AdministrationParticipant` (witness) | Evidence |
| `MedicationExceptionFlag`, `ExceptionFlagResolution` | Evidence |
| `PrnFollowUp` | Evidence |
| `MedicationDelegation` | Evidence with expiry; revocation is a new row |
| `SelfAdministrationAssessment` | Versioned evidence |
| `PresenceInterval` | Effective-dated (owned by the person record) |
| `MedicationHandoff` (LOA out/in) | Evidence |
| `InventoryReceipt`, `InventoryDisposal`, `InventoryAdjustment` | Evidence |
| `ControlledCountSession`, `ControlledCountLine`, `ControlledDiscrepancyResolution` | Evidence, sequence-numbered |
| `MedicationRuleSetVersion` | Append-only, enforcement-dated, per site |
| `MarReviewAttestation` | Evidence |

---

## 17. API surface (sketch)

Under `/karuna/v1`, every route declaring tenant owner, capability, reach rule and audit action in
metadata (`KARUNA_DESIGN.md` §14):

- `GET  /sites/{siteId}/mar?from=&to=` — derived doses and records for people in reach, device-scoped.
- `GET  /recipients/{id}/medication-orders` · `POST .../orders` (Draft) · `POST .../orders/{id}/submit`
  · `POST .../orders/{id}/verify` · `POST .../orders/{id}/return` · `POST .../order-change-sets`
  · `POST .../orders/{id}/hold|resume|discontinue`
- `PUT  /administrations/{administrationId}` — idempotent create keyed by the client-generated id;
  returns the stored record and its flags.
- `POST /administrations/{id}/corrections`
- `POST /prn-follow-ups/{id}` · `POST /sites/{siteId}/controlled-counts` · `POST
  /controlled-counts/{id}/resolve`
- `POST /sites/{siteId}/inventory/receipts|disposals|adjustments`
- `POST /sites/{siteId}/downtime-mar` — returns the PDF, non-cacheable, audited.
- `GET  /nursing/queues/{queue}` — bounded, organization- and reach-scoped.

---

## 18. Audit actions

`karuna.medication-order.created|submitted|verified|returned|held|resumed|discontinued|expired`,
`karuna.medication-order-change-set.applied`, `karuna.administration.recorded`,
`karuna.administration.corrected`, `karuna.medication-exception.resolved`,
`karuna.prn-follow-up.recorded`, `karuna.controlled-count.recorded`,
`karuna.controlled-discrepancy.resolved`, `karuna.inventory.received|disposed|adjusted`,
`karuna.medication-delegation.granted|revoked`, `karuna.emar.downtime-mar.printed`,
`karuna.emar.back-entry.recorded`, `karuna.mar-review.attested`.

Metadata carries ids, outcome codes and flag codes only — never drug names, doses, reasons or
narratives.

---

## 19. Tests

Beyond the Karuna-wide requirements (SQL Server, cross-tenant, fail-first):

1. An active order cannot be edited through any route; a change set replaces it atomically.
2. The transcriber cannot verify their own order; a verifier without the qualification at that instant
   is refused.
3. A phone order's confirmation due date counts Maine business days.
4. Fixed-time doses on the spring-forward date: 02:30 resolves to 03:00 and is labelled.
5. Fixed-time doses on the fall-back date: 01:30 resolves once, to the first occurrence.
6. A q6h order stays six real hours apart across both transitions.
7. Discontinuing an order at 10:00 removes later derived doses and leaves earlier records intact.
8. A second Given for the same dose identity is **accepted** and flagged as possible double
   administration, and a nurse notification is queued.
9. A PRN over its 24-hour maximum is **accepted**, flagged and produces an event-report draft.
10. A Given by a staff member whose CRMA expired the previous day is **accepted**, flagged, and
    produces an event-report draft; a delegation-based eligibility for a rescue medication is accepted
    unflagged.
11. A Given against a Discontinued or unverified order is accepted and flagged.
12. The same client-generated administration id sent twice produces one record; sent twice with
    different content, the second is refused.
13. An administration in another organization's order is refused and the refusal reveals nothing about
    the other order.
14. A correction never changes the original row; the MAR shows both.
15. A dose past its window and grace with no record yields an omission flag exactly once.
16. Doses inside a presence interval derive as Away.
17. A controlled count with one participant is refused; a discrepancy blocks closing the session.
18. The count log's sequence has no gaps under concurrent sessions (SQL Server concurrency test).
19. The downtime MAR contains exactly the derived doses for its window and a signature legend, and its
    generation is audited.
20. A back-entry against a dose with an electronic record raises the reconciliation question rather
    than merging.
21. The allergy reminder never renders a "no conflicts" statement.
22. Audit metadata for every eMAR action contains no drug name, dose or free text.

Tests 8–11 and 13 are the ones that protect the principle in §1; they must be shown to fail against a
build that refuses the record before they are kept.

---

## 20. Internal landing order (within step K4)

1. Rule-set versions, products, orders, lifecycle, verification — no administration yet.
2. Schedule derivation with DST and absence; the read-only MAR view.
3. Administration records, idempotency, classification and flags; corrections.
4. Eligibility and delegations (depends on K2 staff qualifications).
5. PRN limits and follow-ups.
6. Routing to event-report drafts (depends on K3).
7. Downtime MAR and back-entry.
8. Controlled counts and inventory.
9. Nurse queues and the weekly review.

Steps 1–7 are the minimum for a synthetic demonstration of a medication pass. Steps 8–9 are required
before any residential agency could use it.

---

## 21. Open questions

- **MQ-1** The administration window and documentation grace period per site type.
- **MQ-2** Who may verify orders at each site type; is CRMA-level verification ever acceptable?
- **MQ-3** Who may receive a verbal or phone order at each site type.
- **MQ-4** Whether an append-only electronic count log satisfies ch. 113's bound, numbered book for
  Schedule II, or a printed log remains required.
- **MQ-5** PRN follow-up interval; whether it varies by medication class.
- **MQ-6** Whether a periodic nurse MAR review is required by rule or contract.
- **MQ-7** RxNorm licensing terms for the subset Karuna ships, and its update cadence.
- **MQ-8** Whether an electronic signature legend and authenticated entry satisfy "initialled, with a
  full signature recorded elsewhere".
- **MQ-9** Which medication-error categories the agency treats as affecting health or safety by
  default, for the Chapter 12 determination prompt.
- **MQ-10** Whether pharmacy partners serving Maine group homes can provide electronic orders (HL7,
  NCPDP or structured fax), to size the later pharmacy interface.
