# Sati — Active agenda

**Consolidated October 8, 2026.** Stable IDs below are current priorities, not new implementation
authorization. Read the topic owner and [readiness registry](docs/readiness/README.md) before work.
Historical completions/releases remain in the [byte-preserved agenda](docs/archive/2026-10-08/AGENDA.md).

## Release 1.3.38 — readiness evidence and service safeguards

**Status:** Source, Demo API, both installers and distribution verified under the October 8
DATT authorization. Final source, embedded build/UI, automated acceptance and documentation
gates passed, including the actual cached-model gate and complete isolated API suite. This
closing record supplies release evidence; the handoff reports its resulting commit after push
and exact remote confirmation. This release covers
the completed bounded worker, billing-export and request/incident/health repair sequence, the
Settings readiness display and canonical documentation/evidence gates. Broader billing lifecycle,
worker fairness/budgets, tenancy, recovery and independent launch review remain open.

| Gate | Current evidence / remaining work |
|---|---|
| Source audit | Audited repair checkpoint `61c6d0453b34d559886c5f4b28278552b46172fb` and verified AI protocol checkpoint `bb2cef87ed64dd30e31bbe0cbbf41def9946bd87` are included in default `master`. Coordinated source release commit `a50a4df611439f2354f02426e8b5d404297b8ab4` pushed; exact remote source and clean tracked checkout confirmed before packaging. |
| Schema | No persistence migration/model, API Data or reset-baseline delta against fetched released default. No cloud migration, capture, reset or firewall action required or performed. |
| Readiness | Sealed reviewed 1.3.38 snapshot uses audited application source `bb2cef8`; independent review confirms the later test-only fixture diff changes no assessed control. All 42 criteria reviewed, unchanged rubric/history, overall 22.0%, agency separation 25.0%, repeat-request handling 26.8%, operations 14.3%; 41 launch checks remain open. Final embedded parser/WPF 58 passed and all four renders inspected. Later hosting evidence is operational evidence for a future assessment. |
| Automated gates | Final full Release build passed with 18 existing warnings and zero errors. Main-project acceptance: desktop 3,017, API 1,197, signatures 119, portal 8 and Carika 4 passed (4,345 total). The desktop's one initial model skip was separately exercised: the unchanged actual-model gate passed all three original scenarios (one test, zero failed/skipped, 4m14s). Final complete API acceptance passed 1,197, failed 0, skipped 0 in 6m36s, including all ten boundary cases; private SQL cleanup passed. Earlier failed/aborted runs remain retained. All privacy assertions and time limits remain unchanged; fixture isolation changes no application control and establishes no earlier timeout cause. Final embedded parser/WPF 58, documentation negative proofs 22, readiness helper 43, preflight helper 9, portal UI 9 and synthetic installer helpers 16 passed. [Model evidence](docs/readiness/work-evidence.md#2026-10-08--datt-local-model-prompt-gate-repair) and [final API acceptance](docs/readiness/work-evidence.md#2026-10-08--final-release-suite-response-start-timeout) own results and limits. |
| Demo API | Publication verified after the one accepted upload: completed real deployment, exact 70 known deployed file hashes and all three public health/version/contract checks. [The canonical environment inventory](DATABASE_ENVIRONMENTS.md#demo-api-1338-publication--october-9-2026-012401-utc) owns current version, ZIP hash, deployment ID and observed health. Initial temporary-ID polling and local verifier failures are retained in working evidence; no upload replay or rollback. |
| Installers | Demo `SatiDemoSetup-1.3.38.exe`: 104,542,208 bytes, SHA-256 `A83823443BC4E5390169F5D7C9E1F1234C8A551EE0214881DBA3B3443C65C052`; all five responsive launches, normal closes, exact 1.3.38.0 and cleanup passed. Local `SatiLocalSetup-1.3.38.exe`: 206,872,617 bytes, SHA-256 `5D01641720FFC97EB5864CDB98C5D505C034052DBCD8AFB4F7012000B031F958`; exact 1.3.38.0, embedded Microsoft signature, integrated security and cleanup passed. Initial Local refusal during simultaneous Demo acceptance was resolved by sequential acceptance of the unchanged artifact. [Artifact/acceptance evidence](docs/readiness/work-evidence.md#2026-10-08--1338-installer-acceptance-and-distribution). |
| Distribution | Both accepted EXEs and their checksum files published with identical final hashes to the exact `Sati Desktop` and `SatiLogica Demo Files` destinations under `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents`. Four files verified, no overwrite or alternate location. Cloud-sync receipt and independent external-device acceptance remain unverified. [Exact paths and evidence](docs/readiness/work-evidence.md#2026-10-08--1338-installer-acceptance-and-distribution). |
| Branches/worktree | Agenda branch source fast-forwarded into local master. Older background-worker/Claude branches and detached 1.3.31 worktree retained because active/retention intent is uncertain. No deletion, force, stash or reset. Preexisting assessment helper/TRX scratch retained and excluded. |
| Final evidence | Closing operational evidence and Kudu polling/timestamp guidance updated; final documentation structure and all 22 negative proofs passed, preflight/readiness history passed, sealed readiness/rubric unchanged. [Working evidence](docs/readiness/work-evidence.md#2026-10-08--1338-installer-acceptance-and-distribution) records exact results and limits. The release handoff reports the resulting evidence commit and confirms default remote equality; no self-referential second ledger commit is needed. |

## Release 1.3.37 — source version reference

This heading preserves the release-preflight lookup. Exact release outcomes, commands, hashes and
remaining historical checkpoints belong to the [captured release record](docs/archive/2026-10-08/AGENDA.md),
not this active backlog. The [environment inventory](DATABASE_ENVIRONMENTS.md) records the later
October 8 firewall-removal evidence; historical unchecked release items were not silently closed.

## Current priorities

| Stable ID | Status | Work and acceptance owner |
|---|---|---|
| SATI-TEN-001 | planned | Structural tenant owners/composite constraints and shared/database-per-tenant/hybrid decision; [architecture](docs/architecture/identity.md), [structural review](SATI_STRUCTURAL_REVIEW_2026-09-28.md). Preserve current caseload/capability checks. |
| SATI-WRK-001 | partial; note failure, HTTP deadline, agency paging and daily cache rotation implemented | W8 workload isolation, agency/account fairness, API admission, global/tenant budgets and synthetic multi-host/load proof; [worker handoff](BACKGROUND_WORKERS_HANDOFF.md), [working evidence](docs/readiness/work-evidence.md). |
| SATI-OPS-001 | activation/evidence pending | Watchdog publication, named-owner notification/absence evidence and enabled-worker expectations; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-REC-001 | evidence pending | Complete-service recovery, approved RPO/RTO, protected SQL/blob/key inventories, restore/cutover/later-write/external-send reconciliation; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-IDEM-001 | investigation/evidence pending | End-to-end command identity: durable scoped request keys and fingerprints, replay/race conflicts, outbox/inbox intent, lease/fencing ownership, ambiguous external sends and provider reconciliation; [protocol baseline](docs/readiness/protocol-baseline.md), [readiness method](docs/readiness/readiness-method.md). Idempotency is assessed separately from source-only assertions or literal exactly-once claims. |
| SATI-BIL-001 | residual export-error repair implemented; R1/full queue/send R2 open | Assessment R1/R2 original/correction lifecycle and current compliance release gates; [billing architecture](docs/architecture/billing.md), [sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md). Five intended regressions and focused source/synthetic acceptance are recorded in working evidence; no complete send-gate closure. |
| SATI-SEC-001 | current request/incident/health repair sequence implemented and locally verified; broader sink/admission/security work open | Authenticated expensive-operation/per-actor/IP/validated-agency budgets, distributed login guard, deployed least privilege/redaction and independent review; [logging owner](LOGGING_DESIGN.md), [security review](SECURITY_REVIEW_2026-09-10.md), [API audit](API_SECURITY_AUDIT.md). |
| SATI-GOV-001 | source implemented/runtime gated | Governance activation, approved policy periods and complete storage/recovery adapters; [governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md). Runtime retention stays PolicyOnly. |
| SATI-CLI-001 | evidence pending | External-device/accessibility/mixed-version/clean-install acceptance and supported client/server/schema compatibility; [Demo acceptance](DEMO_ACCEPTANCE.md), [readiness](docs/readiness/README.md). |
| SATI-DB-001 | planned | Incremental model/schema owner consolidation, controlled migration/rollback and bounded summary queries; [architecture](ARCHITECTURE.md), [environment procedures](DATABASE_ENVIRONMENTS.md). |
| SATI-DOC-001 | implemented; upkeep required | Canonical documentation owners, stable IDs, dated evidence and no duplicate current inventory; [governance](docs/documentation-governance.md). Validator and mutation checks are required. |

## Current repair sequence — completion handoff

The bounded worker failure/deadline/discovery/cache repairs, billing residual export-error repair,
request exception boundary, incident execution scope and two-check health redaction are locally
implemented and verified. [Working evidence](docs/readiness/work-evidence.md) owns actual results,
failed baselines and limits. This completes the current repair sequence, not the broader active
agenda. Billing R1/full R2, worker budgets/fairness and general security/operational evidence remain
future work.

The incident and health changes and Settings readiness display are included in audited checkpoint
`61c6d04`, committed and pushed to the agenda branch after the completion handoff. Josh's valid October 8 `Invoke DATT!`
invocation now authorizes the bounded release actions in
[the release playbook](RELEASE_PLAYBOOK.md), subject to its gates. These repairs added no schema
migration. Cloud database changes, security settings and Production actions retain their separate
authorization requirements; no release outcome is established by this invocation.

## Next eligible work

**Next eligible item:** SATI-BIL-001

**Eligibility:** future bounded R1 fact/policy design when agenda work resumes after the current
DATT release. This pointer does not select billing work during the release. The residual
export-error repair is verified, but it does not implement an
original-release lifecycle rule. Read-only source exploration identifies suitable physical-history
and late-receipt test seams; no proposed fact matrix is an implemented gate. Cross-mode history
and generic upload-rejection semantics require an explicit decision before full guard implementation.
Follow [the standing workflow](AGENTS.md#standing-work-and-documentation-upkeep).

**Bounded slice:** design one shared Contracts owner for original-release permission using actual
recorded transport/uncertainty/receipt facts. Define precise tenant/period/generation/business-claim
mapping from retained immutable content and explicit correction links. Generation alone is not
receipt or physical transmission; damaged, unmapped or ambiguous retained physical history must
be held for review in the proposed rule. Preserve harmless generation, exact replay, known-unsent
recovery and explicit correction lineage, including frequency-1 Resubmit. Describe admission at
fresh generation, new queue intent and immediately before durable Sending without making a
test-mode bypass. Do not implement the guard until policy and race design are settled.

**Dependencies and owners:** read [the billing architecture](docs/architecture/billing.md),
[the sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md),
[payer requirements](PAYER_BILLING_REQUIREMENTS.md),
[payer certification](PAYER_BILLING_CERTIFICATION.md),
[regulatory posture](REGULATORY_CONCERNS.md),
[the protocol baseline](docs/readiness/protocol-baseline.md),
[the contingency owner](docs/readiness/multitenancy-contingencies.md) and
[working evidence](docs/readiness/work-evidence.md). Revalidate original export, local `EdiService`,
queue/worker, mock transmission, manual reconciliation, receipt ingestion, `ClaimCorrectionRules`
and `LoadClaimHistoryAsync`. The latter currently counts generated-only files as submissions and
skips malformed content, so it cannot become an original-release guard unchanged. A new key,
account or control number does not create a new business claim. No schema/cloud/vendor or
working-data access is needed for this design.

**Required policy/design outputs:** classify Generated-only, CancelledBeforeSend, Queued, Sending,
OutcomeUnknown, accepted/manual received and exact matched receipts separately. Specify when
ConfirmedNotReceived permits a separate generation and how a later matched receipt takes
precedence. Decide cross-mode history and whether generic upload rejection supplies definitive
nonreceipt evidence; do not equate it with a 999 rejection or invent a payer decision. Preserve
the existing correction owner. Design deterministic synthetic SQL barriers across the distinct
ServiceTime/BillingPeriod named-lock paths and plain Serializable receipt transactions; do not
assume their different lock resources coordinate or claim SQLite proves SQL locking.

**Boundaries and completion evidence:** this next pointer selects design and a concrete proposed
fail-first test plan only. No billing production/test implementation or SQL execution is part of
the current DATT release through this pointer. The valid invocation supplies only the separate
[playbook release authority](RELEASE_PLAYBOOK.md); this design pointer does not broaden it to
billing activation, schema/cloud database changes, security settings, Production actions,
working-data access or real billing provider calls. Preserve sealed readiness/history and
unrelated work.

The proposed acceptance must cover fresh-key generation and a previously retained original
after actual accepted upload; Unknown/Sending refusal; generated-only and known-unsent positives;
exact replay; correction lineage; malformed-history hold; and a genuine nonreceipt → later exact
receipt → queued successor sequence with no second upload. Remediate old positive fixtures with
legitimate separate lifecycle histories, not an `IsTest` exception. Identify an exact shared owner,
query adapters, enforcement order, deterministic SQL barrier and evidence limits before source work.

Full R2 current eligibility for the exact retained original/correction subset, payer-held void
purpose policy, broader sink/admission work, worker fairness, structural tenancy, recovery and
independent review remain separate future work. The current repair sequence supplies local
source/synthetic evidence only; no complete billing send-gate or logging-redaction claim follows.

## Preserved open-work inventory

[Legacy open-item registry](docs/backlog/legacy-open-items.md) contains **454** original
unchecked tasks with stable LEG IDs, original text, heading context and original line numbers.
They require revalidation: some old checkboxes conflict with later implemented/superseded records.
No item was dropped or declared fixed merely to shorten this agenda. Promote, merge, supersede or
close each legacy ID only with an explicit disposition, canonical active ID and dated evidence.

Deferred domain/product work remains in its existing canonical topic owner and the legacy registry.
Do not expand into future Karuna/OADS/mobile work just because a historical roadmap mentions it.

## Change discipline

Preserve IDs when wording/status changes. Record owner, acceptance criteria, evidence and reason
for deferral; update readiness separately when activation requires operator/cloud/vendor evidence.
Completed records move to a dated archive with a disposition link, rather than disappearing.
Never use a checked box or aggregate test count as proof of a live service or legal clearance.
