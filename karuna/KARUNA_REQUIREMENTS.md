# Karuna — requirements for replacing Therap

*Status: research and requirements only. Nothing described here is implemented. Written 2026-09-28
against `master` @ `b2d249a` (Sati release 1.3.30). Companion to `KARUNA_DESIGN.md`,
`KARUNA_EMAR_DESIGN.md` and `CODEX_HANDOFF.md` in this folder. Read `PLATFORM_DOMAIN.md` first; it
decided Karuna's tenancy and cross-tenant model on 2026-09-07 and this document builds on it.*

This is an engineering note, not legal advice. Every regulatory statement below is labelled with how
it was established, following the convention in `SATI_SYSTEM_MAP.md`:

- **SOURCE-READ**: read from the primary source or an official Maine/CMS page during this review.
- **SECONDARY**: from a search summary or a vendor/advocacy page, not the primary text.
- **UNVERIFIED**: domain knowledge the repository and this review did not confirm. Confirm before
  building.

Rules change. Repeat a primary-source review before production and record the rule versions and
dates, as `REGULATORY_CONCERNS.md` already requires for Sati.

---

## 1. What "replace Therap" means

An agency that uses Therap for direct service uses it as the single place where:

1. staff find out who they support and what that person needs today;
2. every shift's work is documented contemporaneously, by the person who did it;
3. medications are ordered, scheduled, administered, refused, counted and reconciled;
4. anything that goes wrong is reported internally, reported to the State within its deadline, and
   followed up;
5. behaviour plans, restrictive interventions and rights restrictions are approved, trained on and
   evidenced;
6. staff are scheduled, clock in and out, and are qualified for what they were scheduled to do;
7. in-home visits are electronically verified and the verification reaches the State aggregator;
8. authorized units are tracked, documented services become claims, and remittance is reconciled;
9. supervisors, nurses, QA staff and administrators see what is late, missing or at risk; and
10. surveyors, auditors, guardians and the person themselves can be given the record.

If any one of those still has to happen in Therap, the agency cannot leave Therap. That is the test
this document applies. Section 3 is the inventory; section 4 is the regulatory ground it stands on.

### Who the users are

This is the single largest difference from Sati, and most of the design follows from it.

| User | Works where | Device | Characteristics that matter |
|---|---|---|---|
| Direct support professional (DSP) | Group home, the person's home, the community, a day program | Shared house tablet or PC; own phone | High turnover, variable literacy and technical comfort, 24/7 shifts, often floats between sites, documents while supporting someone |
| Shared living (home) provider | Their own home | Own phone or PC | Often a contractor, not an employee; lives with the person |
| House / program manager | On site and office | Laptop, phone | Supervises staff, reviews documentation, first reviewer of events |
| Nurse (RN/LPN) | Several sites | Laptop, phone | Owns medication oversight, order verification, delegation/training of unlicensed staff |
| Behaviour specialist / qualified professional | Visiting | Laptop | Authors behaviour plans, monitors data monthly |
| QA / event coordinator | Office | Desktop | Reportable-event filing, 30-day follow-ups, trend review |
| Scheduler / HR / training coordinator | Office | Desktop | Shifts, credentials, training evidence |
| Billing staff | Office | Desktop | Authorizations, claims, remittance |
| Administrator | Office | Desktop | Organization configuration, users, audit |

Sati's users are case managers at desks with one caseload each. Karuna's are many people touching the
same person around the clock, most of them on a shared device, many of them new. A design that is
correct for Sati — one owner per person, a 7:00 AM to 7:00 PM service day, a data-dense desktop
client — is wrong for Karuna in each of those three respects.

---

## 2. Scope

**In scope for the first replacement:** Maine provider organizations delivering MaineCare
Sections 21 and 29 (adults with intellectual disability or autism), with the design leaving room for
Sections 18 and 20 because the 2026-09-21 documentation bulletin and the Chapter 12 reportable-event
rule cover them together.

**Not in scope for the first replacement:** Section 19 home-care agencies, children's services under
OCFS, licensed nursing facilities, ICF/IID. Each has different rules; the design keeps rule sets
per program so they can be added rather than retrofitted (see `KARUNA_DESIGN.md` §6).

**Services the model must represent** — MaineCare Section 21.05 covered services (**SOURCE-READ**,
10-144 C.M.R. ch. 101, ch. II, §21.05):

| Service | Unit as stated in 21.05 | Why it matters to the model |
|---|---|---|
| Home Support — Agency Per Diem | Per diem | 24/7 staff presence; "at least one staff person … awake at all times" when members are home (**SECONDARY**). Billing is by day, staffing is by hour — two different clocks. |
| Home Support — Quarter Hour | Quarter hour | In-home, time-based, EVV-relevant. |
| Home Support — Family-Centered Support | Not stated | Standard unit rate per Appendix I (**SECONDARY**). |
| Home Support — Remote Support (Interactive, Monitor Only) | Not stated | Remote/telehealth delivery does not require an EVV visit (**SECONDARY**). |
| Shared Living (Foster Care, Adult) | Per diem | Principal care provider lives in the home; at most two people per home; respite is inside the rate. |
| Community Support (Individual, Group, Center-Based) | Varies by ratio 1:1, 1:2, 1:3 | One staff minute legitimately serves several people. |
| Work Support (Individual, Group of 2–6) | Varies | Same ratio question. |
| Employment Specialist Services | Max 10 hours/month | A monthly cap is a utilization rule, not an authorization rule. |
| Career Planning, Crisis Assessment, Crisis Intervention, Consultation, Counselling-type and therapy maintenance services, Non-Traditional Communication, Non-Medical Transportation | Mostly not stated | Rates and units come from Appendix I; do not hard-code them. |
| Assistive Technology, Communication Aids, Home Accessibility Adaptations, Specialized Medical Equipment | Not stated | Purchases, not time. Out of first-release billing scope; still need authorization tracking. |

The unit column is incomplete because 21.05 does not state most units; Appendix I and the rate
schedule do. **Karuna must read units and rates from versioned configuration, never from code.**

---

## 3. Capability inventory

"Therap equivalent" names the Therap module an agency would recognize. Therap describes itself as
"70+ modules" and its public pages confirm T-Log, ISP Data, MAR, SComm, General Event Reports,
Scheduling/EVV, EVV Aggregator, billing, incident management, person-centred planning, case
management, audits/QA and performance dashboards (**SECONDARY**, therapservices.net and its app
listing). Module names in the table that are not in that list are **UNVERIFIED** as current Therap
product names.

Priority: **R** = required for any agency to leave Therap. **R-res** = required for residential
agencies (per diem homes, shared living). **L** = later; an agency can operate without it on day one
with a documented workaround.

### 3.1 Foundation

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| F1 | Organization tenant, programs, sites (homes, day programs), enrolment of a person in a program/site with dates | Agency/Program setup | R | Tenant is the provider organization (`PLATFORM_DOMAIN.md`). |
| F2 | Staff accounts, independent permissions, site membership | User/role admin | R | Sati's per-user `[Flags]` capability model, with Karuna's own capability set. |
| F3 | Care-team access: who may see which person, derived from site membership, shift assignment and enrolment, with audited emergency access | Caseload/program access | R | Replaces Sati's one-owner `Person.UserId` model. |
| F4 | Shared-device mode: enrolled house tablets, fast staff switching, idle lock, device scoped to its site | — | R | The common real-world device in a group home. |
| F5 | Audit of reads and writes, record versions, amendments | Audit trail | R | Platform. |
| F6 | Contemporaneous-entry evidence: occurred-at vs recorded-at on every record, late-entry reason | — | R | Required by the 2026-09-21 bulletin's timing language. |
| F7 | Documents and uploads (scanned orders, consents, external reports), hash-verified | Document storage | R | Platform `DocumentArtifact` pattern. |
| F8 | Notifications without PHI in the payload | SComm notifications | R | Sati chat already uses contentless notices. |
| F9 | Downtime procedure: printable MAR, face sheets and schedules; back-entry with downtime provenance | — | R | An eMAR without a downtime path is unsafe. |
| F10 | Data import from Therap, current state only, history as retained artifacts | — | R | No agency switches without it. |

### 3.2 The person

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| P1 | Demographics, identifiers (MaineCare ID), photo, guardian/representatives, contacts | Individual Data Form (IDF) | R | Links to the platform person registry. |
| P2 | Allergies, diagnoses, diet/texture, adaptive equipment, communication profile, code status/advance directive | IDF / Health | R | Allergies feed order entry. |
| P3 | Printable face sheet / emergency fact sheet for hospital transfer | Face sheet | R | Also a downtime artifact. |
| P4 | Person-specific protocols (seizure, choking/dysphagia, positioning, elopement) with staff training evidence | Health / ISP | R | Training on a protocol is a staff-qualification fact. |
| P5 | Rights modifications under the HCBS settings rule, with the required elements and review dates | ISP / Rights | R | Originates in the PCP; the provider implements and evidences. |
| P6 | Admission, transfer between sites, discharge, death | Admission/Discharge | R | Death is also a reportable event. |
| P7 | Personal funds held by the provider: ledger, receipts, cash counts, reconciliation | Personal Finance (**UNVERIFIED** name) | R-res | Sati's representative-payee ledger is the precedent. |
| P8 | Guardian/family read access | Family access | L | Needs its own identity and consent design. |

### 3.3 Plans and goals

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| G1 | Receive the person-centred plan (PCP) as an immutable snapshot, with version and source | ISP | R | Karuna implements the PCP; it does not author it. Standalone agencies enter or upload it. |
| G2 | Provider support plan: goals/objectives linked to PCP outcomes, support strategies, data-collection method | ISP Program | R | The 2026-09-21 bulletin requires notes to tie to PCP goals. |
| G3 | Goal data collected during shifts | ISP Data | R | |
| G4 | Periodic progress summaries (monthly/quarterly), human-authored, generated from data | ISP reports | R | |
| G5 | Service authorizations as received (service code, units, dates, rate, provider), with utilization | Service Authorization | R | The platform authorization (`PLATFORM_DOMAIN.md`) once it exists. |

### 3.4 Daily documentation

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| D1 | Shift notes: narrative plus structured fields, per person per shift | T-Log | R | |
| D2 | Service delivery records: the billable fact, carrying the bulletin's required elements | Attendance / Billing data | R | See §4.2. |
| D3 | Health observations: vitals, weight, bowel, seizure, sleep, fluids, menses, pain, skin, blood glucose | Health Tracking (**UNVERIFIED** name) | R | Seizure with rescue medication links to the MAR. |
| D4 | Medical appointments, outcomes, follow-ups, new orders arising | Appointments | R | New orders go through order verification. |
| D5 | Behaviour data (ABC, frequency/duration/intensity) against the active plan's target behaviours | Behavior Tracking | R | |
| D6 | Shift handoff / communication log | SComm / Shift notes | R | Platform chat may serve; see sharing rules. |
| D7 | Community-integration evidence (HCBS settings) | — | L | Useful for settings compliance; not billing-critical. |

### 3.5 Medication (eMAR)

Detailed in `KARUNA_EMAR_DESIGN.md`. Required in full for any agency whose staff administer
medication.

| # | Capability | Therap equivalent | Priority |
|---|---|---|---|
| M1 | Prescriber orders: entry, transcription, second-person verification, phone-order confirmation tracking, renewal/expiry | MAR — orders | R |
| M2 | Derived administration schedule (scheduled doses by site and time) | MAR | R |
| M3 | Administration record: given, refused, held, not available, leave of absence, self-administered; with actor, time, dose, route, site | MAR | R |
| M4 | PRN: indication, max-per-24h, minimum interval, required follow-up of effectiveness | MAR — PRN | R |
| M5 | Administrator eligibility: credential or person-specific delegation valid at that instant | — | R |
| M6 | Medication errors detected and routed to an event report | GER link | R |
| M7 | Controlled-substance counts at shift change, two-person, discrepancy workflow; Schedule II daily count and weekly inventory | Controlled count (**UNVERIFIED** name) | R-res |
| M8 | Inventory: receipt from pharmacy, on-hand, refills due, discontinued-medication disposal within 30 days | — | R-res |
| M9 | Treatments (TAR) on the same schedule engine | TAR | R |
| M10 | Self-administration assessment per person | — | R |
| M11 | Downtime MAR and back-entry | — | R |
| M12 | Nurse review queues (PRN use, refusals, holds, late passes) | — | R |
| M13 | Pharmacy integration (fax/HL7/NCPDP), barcode verification | — | L |

### 3.6 Events, behaviour and rights

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| E1 | Event report: draft by any staff, one report per person with a shared occurrence group | GER | R | The OCFS matrix says "enter separate reports for each client" (**SOURCE-READ**); design follows that for privacy. |
| E2 | Supervisor review, categorisation, reportability determination against the program's rule set | GER review | R | |
| E3 | State filing tracked against its deadline; Evergreen reference recorded by staff | — | R | Evergreen has no documented provider interface; see §4.3. |
| E4 | APS / law-enforcement / guardian / case-manager / physician notification record with times | GER notifications | R | Mandated-reporter duty is separate from the reportable-event filing. |
| E5 | 30-day provider follow-up (root cause, quality improvement) with due date | Investigation | R | |
| E6 | Behaviour support plan: level, versions, approval evidence, consent, review cadence, staff training per version | Behavior Plan | R | 14-197 C.M.R. ch. 5. |
| E7 | Restrictive-intervention record: planned vs emergency, technique, duration, injury, debrief | Restraint | R | Emergency restraint is reportable. |
| E8 | Trend review: by person, site, category, staff, time of day | GER reports | R | |

### 3.7 Staff, schedule, time and EVV

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| S1 | Credentials and training with evidence and expiry (CPR/First Aid, College of Direct Support, CRMA, MANDT, background check, licence) | Staff Training / Agency (**UNVERIFIED**) | R | Section 21.10 requirements, §4.5. |
| S2 | Person-specific training (behaviour plan version, protocols, medication delegation) | — | R | |
| S3 | Qualification gate used by scheduling, medication and billing | — | R | |
| S4 | Shifts, assignments, open shifts, coverage (awake overnight, ratio, credential coverage for med passes) | Scheduling | R | |
| S5 | Clock in/out; time records; payroll export (not payroll processing) | Time Tracking / Payroll export | R | |
| S6 | EVV capture of the six federal elements for EVV-required services, with visit maintenance and reason codes | Scheduling/EVV | R for in-home services | §4.4. First release may reconcile against the State's Sandata EVV instead of becoming an alternate vendor. |
| S7 | Sandata aggregator interface as a certified alternate EVV vendor | EVV Aggregator | L | Minimum eight-week integration process (**SOURCE-READ**). |

### 3.8 Billing

| # | Capability | Therap equivalent | Priority | Notes |
|---|---|---|---|---|
| B1 | Rates and unit rules per service code and modifier, versioned by effective date | Billing setup | R | |
| B2 | Claim readiness: authorization, units remaining, documentation approved, EVV verified where required, staff qualified, no duplicate, no incompatible concurrent service | Billing validation | R | |
| B3 | Claim generation (837P) with frozen claim inputs | Billing | R | Reuse the platform 837P formatter — see `KARUNA_DESIGN.md` §11. |
| B4 | Clearinghouse submission, acknowledgments, 835 remittance, denials, corrections (frequency 7/8), deposit reconciliation | Billing | R | The Sati Claim.MD/outbox/835 work, extracted to the platform. |
| B5 | Utilization vs authorization, per service and per month | Service Authorization reports | R | |
| B6 | Eligibility (270/271) | — | L | Manual monthly verification record until then. |

### 3.9 Oversight

| # | Capability | Therap equivalent | Priority |
|---|---|---|---|
| Q1 | Supervisor documentation review (approve/return) with the Sati transition-table discipline | T-Log approval | R |
| Q2 | Dashboards: missed/late medication, overdue event filings and follow-ups, expiring credentials, unbilled approved services, late documentation | Dashboards | R |
| Q3 | QA chart audits with sampling and findings | Audits & QA | L |
| Q4 | Designated-record-set export for a person; audit export | Reports | R |
| Q5 | Survey/auditor read-only access, time-bounded | — | L |

---

## 4. Regulatory ground

### 4.1 Reportable events — 14-197 C.M.R. ch. 12

- **Who and when.** "Any Required Reporter shall report a Reportable Event" on becoming aware of it,
  including second-hand, "as soon as possible within one (1) business day of the Reportable Event"
  (**SOURCE-READ**, §197-12-2 via LII).
- **Event types** (**SOURCE-READ**, §197-12-1 and §197-12-2): death; suicide attempt; suicide threat
  with intent and plan; emergency-department visit; hospital admission; medication error affecting
  health or safety; emergency medical services beyond first aid; serious injury; missing individual;
  physical-plant disaster; law-enforcement involvement; transportation accident; physical
  assault/altercation; emergency restraint outside an approved plan; rights violation (34-B M.R.S.
  §5605); dangerous situation posing imminent harm. Abuse, neglect and exploitation are defined terms.
- **Required content.** "All of the required fields to the extent that such information is known or
  readily available," including every applicable category and the immediate protective response
  (**SOURCE-READ**).
- **Separate duty.** Filing a reportable event "does not relieve any individual or Provider" of the
  duty to report suspected abuse, neglect or exploitation to APS Central Intake or to law enforcement
  (**SOURCE-READ**; 22 M.R.S. §3477 is the mandated-reporter statute).
- **Follow-up.** A 30-day provider follow-up and internal review, introduced as root-cause analysis
  and quality improvement (**SECONDARY**, maine.gov OADS reportable-events page and search summary).
- **System of record.** Sections 18, 20, 21 and 29 moved reportable events to the State's Evergreen
  system in early 2024; EIS is read-only (**SECONDARY**, Evergreen FAQ and search summary).

**Children's services differ.** The OCFS matrix (dated 01/21/2022) requires EIS submission within 72
hours, a phone report within 4 hours for deaths, missing clients and physical-plant disasters, and
"separate reports for each client" (**SOURCE-READ**, text layer of the matrix PDF). Karuna's first
scope is adult services, but the difference proves the rule set has to be per program authority.

**The adult (OADS) matrix was not located in this review.** The categories above come from the rule
text. Before building the taxonomy, obtain the current OADS matrix and Evergreen's reportable-event
field list; do not reconstruct either from the OCFS matrix.

### 4.2 Documentation of billable services — Maine DHHS bulletin, 2026-09-21

Applies to Sections 18, 20, 21 and 29 (**SOURCE-READ**). Documentation must be completed "at the time
services are provided, or as close to the time of service as practical," and must show:

1. the specific service provided;
2. the assessed need and person-centred goal addressed;
3. the date of service and the actual amount of time provided;
4. what the staff member did to support the individual;
5. who provided the service;
6. where it was provided, when location is relevant.

Progress notes must "demonstrate how the service provided relates to the individual's person-centered
plan and identified goals." Partial-unit and rounding rules may be applied when billing, but
"documentation must reflect the actual amount of time services were delivered."

**Consequence for the design:** these six elements are the minimum shape of a Karuna service record,
enforced by one rule owner before submission. Actual time is stored; billed units are derived and
never written back over it. This bulletin is one week old at the time of writing and is the most
directly relevant source in this document.

### 4.3 Evergreen

Evergreen replaced EIS, MAPSIS and MECARE as the OADS client data system (**SECONDARY**). Providers
enter reportable events and 30-day follow-ups there; behaviour management plans are a module there.
Accounts are requested by email (**SECONDARY**, Evergreen FAQ). **No provider-facing API, file upload
or bulk interface is documented.**

**Consequence:** Karuna cannot claim Evergreen integration. It keeps the provider's own record, tracks
the filing deadline, and records the Evergreen reference and filing time as staff-recorded provenance
— the same honesty as Sati's "Externally signed — staff verified". If OADS later offers an interface,
it becomes an outbox connector behind the same record, exactly as Claim.MD did for Sati.

### 4.4 Electronic visit verification

- 21st Century Cures Act §12006 requires EVV for Medicaid personal care and home health services
  requiring an in-home visit, verifying six elements: type of service, individual receiving, date,
  location, individual providing, and time begun and ended (**SECONDARY**, multiple state and CMS
  summaries).
- Maine runs an **open model**: providers may use the State-offered Sandata system or their own system
  integrated with MIHMS through the Sandata aggregator; alternate-system integration takes at least
  eight weeks (**SOURCE-READ**, maine.gov EVV page).
- Since 2021-01-01, claims without a verified EVV record "pend for up to 30 days and then deny"
  (**SOURCE-READ**, MaineCare bulletin).
- For Sections 12, 18, 19, 20, 21, 29 and 96, services by a **live-in caregiver** are not subject to
  EVV, and Home Support Remote Support delivered as telehealth does not require a verified visit
  (**SECONDARY**, Section 21 waiver-amendment notice summary).
- The exact list of impacted Section 21/29 procedure codes is published as a State spreadsheet that was
  not read here. **Do not hard-code the list.**

**Consequence:** "EVV-required" is a versioned property of a service code and delivery circumstance,
owned by one rule. The first release can let agencies keep using the State's Sandata EVV and reconcile
Sandata visit IDs against Karuna service records; becoming an alternate EVV vendor is a later,
certification-gated integration.

### 4.5 Staff qualifications — Section 21.10

**SOURCE-READ** (10-144 C.M.R. ch. 101, ch. II, §21.10 via LII):

- DSP: at least 18; high-school diploma or GED; current CPR and First Aid; background checks every
  24 months.
- The DHHS Direct Support Professional curriculum (Maine College of Direct Support) within six months
  of hire; **four modules before working independently**: Introduction to Developmental Disabilities,
  Professionalism, Individual Rights and Choice, Maltreatment.
- Department-approved trainings within six months of hire and **every 36 months** thereafter,
  covering the reportable-events system, behavioural-support regulations and person-centred planning.
- Medication administration only by a CNA-M, CRMA, RN, or someone trained through a DHHS-approved
  family-centred or shared-living program.
- Crisis-intervention providers need behavioural-intervention training (for example MANDT).
- Shared living providers must meet the DSP requirements.
- "All training completion records must be maintained in personnel files."

**Consequence:** qualification is a function of staff, requirement and instant, and three different
parts of the product ask it: scheduling, medication administration and billing. It needs one owner.

### 4.6 Medication administration

The most specific rule located is 10-144 C.M.R. ch. 113 (Assisted Housing Programs, including
residential care facilities), §7 (**SOURCE-READ** via LII):

- The MAR records medication name, dosage, route and time; PRN entries record date, time, medication,
  dose, route, reason given and results or response.
- Each entry is initialled, with a full signature recorded elsewhere on the document; documentation is
  made whenever a medication is started, given, refused or discontinued.
- Psychotropic PRN orders need written instructions: symptoms, exact dose, interval and maximum in 24
  hours. Antipsychotics may not be ordered PRN-only.
- Errors, including omissions and documentation errors, are recorded in incident reports.
- Schedule II: daily counts if used, weekly inventory in a bound, numbered book, double-locked storage;
  witnessed disposal.
- Discontinued or expired medications are removed, locked separately and destroyed or returned within
  30 calendar days.
- Written, signed orders before administration; orders effective at most 12 months; psychotropics
  reissued every 3 months; telephone orders confirmed in writing within 5 working days.
- Self-administration ability assessed on admission with the person, representative, practitioner and
  facility.
- Unlicensed staff need Department-approved training and an 8-hour refresher every two years.

**Open applicability question.** Chapter 113 governs assisted housing and residential care
facilities. Whether it governs a given Section 21 agency home or shared living home depends on how
that site is licensed, which this review did not establish. Karuna must attach a medication rule set
to each **site**, not to the organization, and the default rule set must be chosen by the agency with
advice, not by Karuna.

### 4.7 Behaviour support — 14-197 C.M.R. ch. 5

**SECONDARY** (summary of §197-5-5 via LII; the full text should be read before building):

- Levels 3–5 need Planning Team approval, guardian consent, a qualified professional (psychiatrist,
  psychologist, LCSW, LCPC or BCBA), and Review Team approval for restraint over 15 minutes or any
  mechanical or chemical restraint.
- Plans specify target behaviours, baselines, interventions, staff training, the recording method,
  quarterly evaluation, discontinuation criteria and a plan to reduce intrusiveness.
- The qualified professional monitors monthly; the Planning Team reviews quarterly; the case manager
  reviews in person at least quarterly; functional and psychological assessments every three years.
- Physical restraint is limited to 15 minutes unless the Review Team approves otherwise; a physician
  evaluation within 30 days before implementation, then yearly.
- Restraint used outside the regulation is reported under Chapter 12.

### 4.8 Federal HCBS rules

- **Settings rule**, 42 CFR 441.301(c)(4): a modification of a person's rights requires a specific
  assessed need, prior positive interventions, less intrusive methods tried, a proportionate condition,
  data collection and review, informed consent, time limits and an assurance of no harm (already
  recorded in `REGULATORY_CONCERNS.md`).
- **Person-centred planning and conflict of interest**, 42 CFR 441.301(c)(1)–(2): a provider of HCBS
  must not also provide the person's case management or develop the plan, subject to a narrow
  exception (already recorded in `REGULATORY_CONCERNS.md`).
- **Ensuring Access to Medicaid Services** final rule (CMS-2442-F, effective 2024-07-09): a minimum
  definition of critical incident, provider reporting to the State, State investigation within set
  timeframes, and a State electronic incident-management system, phased in over several years
  (**SECONDARY**; the exact compliance dates in 42 CFR 441.302(a)(6) were not confirmed here).
  Karuna's event taxonomy should map to that minimum definition as well as to Chapter 12.

### 4.9 Privacy and records

Carried from `REGULATORY_CONCERNS.md` without restating: HIPAA (the provider is a covered entity and
SatiLogica a business associate — a BAA is required), 42 CFR Part 2 for substance-use records, Maine
22 M.R.S. §1711-C, 34-B confidentiality and recipient rights, minimum necessary, right of access to
the designated record set, retention and legal hold. **Retention periods for MARs, event reports and
service documentation were not established in this review** (UNVERIFIED: MaineCare record-retention
requirements are commonly cited as several years; confirm the controlling rule).

### 4.10 Device and software classification

An eMAR that records administration is clinical documentation. Software that recommends doses,
checks interactions or otherwise provides patient-specific clinical decision support can fall within
FDA device definitions depending on its function (UNVERIFIED for Karuna specifically). **Karuna does
not provide drug-interaction or dosing decision support**, and must not add it without regulatory
review. It enforces the order it was given and the agency's own policy, and says so.

---

## 5. Sources consulted

- 14-197 C.M.R. ch. 12 §1 and §2 — <https://www.law.cornell.edu/regulations/maine/14-197-C-M-R-ch-12-SS-2>
- Maine OADS reportable events — <https://www.maine.gov/dhhs/oads/providers/adults-with-intellectual-disability-and-autism/reportable-events>
- OCFS Reportable Events Matrix (01/21/2022) — <https://www1.maine.gov/dhhs/sites/maine.gov.dhhs/files/inline-files/Reportable%20Events%20Matrix_1.pdf>
- Evergreen FAQ — <https://www.maine.gov/dhhs/oads/about-us/evergreen/faq>
- MaineCare documentation requirements for billable services (2026-09-21) — <https://content.govdelivery.com/accounts/MEHHS/bulletins/42bcf1e>
- MaineCare EVV — <https://www.maine.gov/dhhs/oms/providers/electronic-visit-verification>
- MaineCare EVV implementation reminder — <https://content.govdelivery.com/accounts/MEHHS/bulletins/2b1d118>
- Section 21 waiver amendment notice — <https://www.maine.gov/dhhs/oms/providers/provider-bulletins/mainecare-notice-agency-waiver-amendment-mainecare-benefits-manual-section-21-home-and>
- Section 21.05 covered services — <https://www.law.cornell.edu/regulations/maine/C-M-R-10-144-ch-101-ch-II-144-101-II-21-subsec-144-101-II-21.05>
- Section 21.10 provider qualifications — <https://www.law.cornell.edu/regulations/maine/C-M-R-10-144-ch-101-ch-II-144-101-II-21-subsec-144-101-II-21.10>
- 10-144 C.M.R. ch. 113 §7 — <https://www.law.cornell.edu/regulations/maine/10-144-C-M-R-ch-113-SS-7>
- 14-197 C.M.R. ch. 5 §5 — <https://www.law.cornell.edu/regulations/maine/14-197-C-M-R-ch-5-SS-5>
- CMS HCBS incident management (Access rule) — <https://www.medicaid.gov/medicaid/access-care/downloads/access-final-rule-slides-september-2024.pdf>
- Therap Services — <https://www.therapservices.net/> and <https://www.therapservices.net/products/electronic-visit-verification-solutions/>
- Maine College of Direct Support — <https://www.maine.gov/dhhs/oads/providers/adults-with-intellectual-disability-and-autism/resources-training/college-of-direct-supports>
