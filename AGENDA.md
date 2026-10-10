# Sati — Active agenda

**Consolidated October 8, 2026.** Stable IDs below are current priorities, not new implementation
authorization. Read the topic owner and [readiness registry](docs/readiness/README.md) before work.
Historical completions/releases remain in the [byte-preserved agenda](docs/archive/2026-10-08/AGENDA.md).

## Release 1.3.39 — billing release and account recovery safeguards

**Status:** October 9 DATT release gates complete: source, Demo migration/API/reset, both installer
acceptance gates and exact distribution verified; operator firewall removal verified. Final
documentation/preflight gates passed. This closing record supplies the evidence for the final
ordinary commit/push; the handoff reports its identifier and exact remote confirmation.
Bounded Demo migration, baseline replacement and one verification reset were
separately approved; workstation access was operator-managed.
Completed billing/worker source and controlled migration preparation fast-forwarded into local
`master` at `6a5c6497aab7c4f761d6aef5194087475c00424d`. Older branches/worktree remain retained
because their retention or activity is uncertain. Unrelated `Sati.csproj` formatting and
assessment scratch files are preserved. Dispatch activation and broader W8 work remain separate.

| Gate | Evidence / remaining work |
|---|---|
| Source and readiness | Source release `6fbfeb65730697d4e0244a551fca736f5a3e82a1` pushed and remote equality verified. Version 1.3.39 coordinated; all 42 criteria reviewed and current embedded WPF report verified; prior sealed reports/rubric unchanged. |
| Build and tests | Complete Release build passed; final desktop 3,028, API 1,289, signatures 119, portal 8 and Carika 4 passed. Actual cached-model test passed all three scenarios (3m08s); 4,449 accepted cases, zero unresolved failures/skips. Retained failed run and bounded fixture repair are in the working ledger. |
| Demo schema and reset | Approved rollback rehearsal, apply and idempotent rerun passed at 129 migrations. `DEMO_FULL_RESET_BASELINE_CAPTURED`, anchor 2026-10-09; single request `d851db02-81c6-4559-ab9c-e15ea880bddd` recorded `demo.reset.completed` and correlated Function compliance marker. [Environment inventory](DATABASE_ENVIRONMENTS.md#1339-controlled-demo-rollout--october-9-2026) owns exact deployed facts; operator firewall removal verified. |
| Demo API | ZIP SHA-256 `ED56F687601B26D986893AF26900929DCD3E3260B4872ECB46CD8E8C7D3247C6`; completed deployment `943c4d4f09154180b32f33fe20a57338`. All 70 exact deployed file lengths/hashes and HTTP 200 live/Healthy/version 1.3.39/contract `D30D44631876` verified. |
| Installers | Demo `SatiDemoSetup-1.3.39.exe`: 104,583,168 bytes, SHA-256 `B7E18B675B53C0B024E09162183DEE0E4B3A9EE9A9ECEF1FE6051D4AA99FFCEB`; all five responsive launches, normal closes, exact 1.3.39.0 and cleanup passed. Local `SatiLocalSetup-1.3.39.exe`: 206,932,521 bytes, SHA-256 `07F3850B46315537405C584770BFE8B6D7930DB14E8BFFF396AAE6A2E3BADF10`; exact 1.3.39.0, embedded Microsoft signature, integrated security and cleanup passed. [Acceptance evidence and limits](docs/readiness/work-evidence.md#2026-10-09--1339-installer-acceptance-and-distribution). |
| Distribution | Both accepted EXEs and checksums published and final hashes/sizes verified at `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` (Local) and `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files` (Demo). Four allowlisted files, no overwrite. [Inventory](DATABASE_ENVIRONMENTS.md#1339-installer-publication--october-9-2026) records exact destinations; cloud-sync receipt and independent external-device acceptance remain unverified. |
| Local schema uptake | Known LONGCHENPA installed Local Sati is 1.3.38.0 at preflight; next-launch migration belongs to desktop and is not run by this release audit. Other machine inventory requested; unknown versions will remain explicit. |
| Final evidence | Operational checkpoint `5f815b07b610be35ca3827fb06a5adfcd7d17e73` preserves the interrupted rollout. Both acceptance JSON records and create-new distribution evidence complete the operational checklist. Final documentation structure, 22 negative proofs and preflight/report/history gates passed (`artifacts/datt-1.3.39/documentation-negative-closing.json`). The handoff reports the one closing evidence commit's resulting identifier and exact default remote equality without a self-referential ledger commit. |

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
| SATI-WRK-001 | partial; missing-key recovery released; idle/wake and dispatch fairness source verified locally; broader W8 open | [Numbered work breakdown](#1--sati-wrk-001--safe-background-processing-across-agencies); other-worker fairness, API admission, global/tenant budgets and combined multi-host/load proof. [Dispatch implementation](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-fairness-implementation--task-132) and [working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-132-dispatch-fairness-implementation) own current behavior and acceptance. |
| SATI-OPS-001 | activation/evidence pending | Watchdog publication, named-owner notification/absence evidence and enabled-worker expectations; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-REC-001 | evidence pending | Complete-service recovery, approved RPO/RTO, protected SQL/blob/key inventories, restore/cutover/later-write/external-send reconciliation; [operations](OPERATIONS.md), [readiness](docs/readiness/README.md). |
| SATI-IDEM-001 | investigation/evidence pending | End-to-end command identity: durable scoped request keys and fingerprints, replay/race conflicts, outbox/inbox intent, lease/fencing ownership, ambiguous external sends and provider reconciliation; [protocol baseline](docs/readiness/protocol-baseline.md), [readiness method](docs/readiness/readiness-method.md). Idempotency is assessed separately from source-only assertions or literal exactly-once claims. |
| SATI-BIL-001 | R1/R2 source implemented and locally verified; external evidence pending | Duplicate-claim lifecycle, common admission and current retained-subset/action/purpose gates; [billing architecture](docs/architecture/billing.md), [coordinated release owner](CLAIMMD_SANDBOX_RUNBOOK.md#coordinated-release-and-current-subset-compliance--local-source-october-9-2026), [working evidence](docs/readiness/work-evidence.md#2026-10-09--coordinated-claim-release-and-compliance). External certification and deployed operating evidence remain separate. |
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
agenda. At that October 8 checkpoint, billing R1/full R2 and the broader work remained future
work. The later [billing source completion](CLAIMMD_SANDBOX_RUNBOOK.md#coordinated-release-and-current-subset-compliance--local-source-october-9-2026)
supersedes only that pending source status; worker budgets/fairness and general operating/security
evidence remain open.

The incident and health changes and Settings readiness display are included in audited checkpoint
`61c6d04`, committed and pushed to the agenda branch after the completion handoff. Josh's valid October 8 `Invoke DATT!`
invocation now authorizes the bounded release actions in
[the release playbook](RELEASE_PLAYBOOK.md), subject to its gates. These repairs added no schema
migration. Cloud database changes, security settings and Production actions retain their separate
authorization requirements; no release outcome is established by this invocation.

## 1 — SATI-WRK-001 — Safe background processing across agencies

Outline numbers identify work packages and their smaller tasks. Keep these numbers fixed as
status changes; retain `SATI-WRK-001` as the governing backlog ID. Numbers are references, not
automatic execution order or additional authorization. Topic owners retain detailed rules and
the working ledger retains actual test results. This is a breakdown of existing W8 scope.

| Number | Work package or task | Recorded status / completion owner |
|---|---|---|
| **1** | **SATI-WRK-001 — Safe background processing across agencies** | Overall item open; [W8](BACKGROUND_WORKERS_HANDOFF.md) owns scope and acceptance. |
| **1.1** | Failure isolation: keep a missing account key from blocking healthy work | Missing-key account recovery released in 1.3.39; [recovery owner](BACKGROUND_WORKERS_HANDOFF.md#october-9--known-unsent-dispatch-isolation-implementation). Broader worker failure cases remain under 1.3–1.6. |
| **1.2** | Idle/wake scheduling: control when dispatch runs and stops checking the database | Complete for bounded source implementation and acceptance; committed source `d05312a`, unreleased. [Working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-dispatch-idle-wake-implementation) records actual coverage and limits, separately from deployment and activation. |
| **1.3** | Fair selection: give eligible agencies and accounts predictable turns | Open; contains tasks 1.3.1–1.3.3. |
| **1.3.1** | [Dispatch fairness design](#131--dispatch-fairness-design) | Complete; policy/additive source-schema proposal explicitly approved in [DEC-0240](docs/decisions/current/2026-10-09-DEC-0240.md). |
| **1.3.2** | [Dispatch fairness implementation and tests](#132--dispatch-fairness-implementation-and-tests) | Complete for bounded source verification/main integration; unreleased. Full API 1,330 and shared persistence boundary 1 passed, zero failed/skipped; [evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-132-dispatch-fairness-implementation) owns failures/limits. |
| **1.3.3** | Fairness in polling, note maintenance and signature work | Remaining broader W8 scope; preserve each worker's existing safety owner and define bounded tasks before implementation. |
| **1.3.3.1** | [Polling fairness design](#1331--polling-fairness-design) | Complete for source/design integration; DEC-0241 selects bounded local policy under continued-work authority. |
| **1.3.3.2** | [Bounded ERA feed quantum](#1332--bounded-era-feed-quantum) | Complete for source verification/main integration; 35 local regressions passed; unreleased. |
| **1.3.3.3** | [Durable polling selection](#1333--durable-polling-selection) | Complete for bounded source verification/main integration; 119 tests passed, unreleased. |
| **1.3.3.4** | [Note-maintenance fairness review](#1334--note-maintenance-fairness-review) | Complete for bounded source review; 35 existing tests passed; crash/membership/capacity gaps retained. |
| **1.4** | Resource limits: bound shared connections, concurrency and operation times | Open; worker intervals and adjacent consumers reviewed, strict API lock results repaired. Backend/values require host/closure/enrollment facts and operating objectives. |
| **1.4.1** | [Resource-limit inventory](#141--resource-limit-inventory) | Complete for main-reviewed source/documentation integration; current-source corrections in W8; no adopted limiter. |
| **1.4.2** | [Shared SQL/session admission design](#142--shared-sqlsession-admission-design) | Complete for main-reviewed proposal integration; DEC-0242 remains proposed, backend/value adoption open. |
| **1.3.3.5** | [Signature fairness design](#1335--signature-fairness-design) | Complete for main source/design integration; DEC-0243 selects bounded local implementation. |
| **1.3.3.6** | [Signature reset and hosted boundary](#1336--signature-reset-and-hosted-boundary) | Complete for bounded source/main acceptance; 89 tests passed, unreleased. |
| **1.3.3.7** | [Durable signature selection](#1337--durable-signature-selection) | Complete for bounded source verification/main integration; 111 signature cases passed, unreleased. |
| **1.4.3** | [Worker SQL connection-cost review](#143--worker-sql-connection-cost-review) | Complete for bounded source accounting; conditional costs and unknowns retained, no limiter adoption. |
| **1.4.4** | [Session admission result hardening](#144--session-admission-result-hardening) | Complete for bounded source/main acceptance; 162 focused cases passed, unreleased. |
| **1.4.5** | [Note-worker connection-lifetime evidence](#145--note-worker-connection-lifetime-evidence) | Complete for bounded test/main acceptance; 139 focused cases and final six passed, no resource policy. |
| **1.4.6** | [Signature-worker connection-lifetime evidence](#146--signature-worker-connection-lifetime-evidence) | Complete for bounded test/main acceptance; 228 focused cases passed, no runtime policy. |
| **1.4.7** | [Dispatch-worker connection-lifetime evidence](#147--dispatch-worker-connection-lifetime-evidence) | Complete for bounded test/main acceptance; 110 focused cases passed, no runtime policy. |
| **1.4.8** | [Polling-worker connection-lifetime evidence](#148--polling-worker-connection-lifetime-evidence) | Complete; six new and 153 focused cases passed, no runtime policy. |
| **1.4.9** | [Shared SQL consumer enrollment inventory](#149--shared-sql-consumer-enrollment-inventory) | Complete; bounded source-family census, no live enrollment/capacity claim. |
| **1.4.10** | [Onboarding admission-result hardening](#1410--onboarding-admission-result-hardening) | Complete; 126 focused cases passed, unreleased; no budget policy. |
| **1.4.11** | [Mutation admission-result hardening](#1411--mutation-admission-result-hardening) | Complete; 171 focused cases passed, unreleased. |
| **1.5** | API workload limits: protect interactive work from expensive requests | Open; coordinate shared admission work with SATI-SEC-001 rather than create a second owner. |
| **1.5.1** | [API workload admission inventory](#151--api-workload-admission-inventory) | Complete; bounded source-family review, no numeric/runtime policy. |
| **1.5.2** | [Shared admission adoption prerequisites](#152--shared-admission-adoption-prerequisites) | Complete for direction/assignment; operating adoption remains separate. |
| **1.5.3** | [Shared accounting candidate and deterministic limits](#153--shared-accounting-candidate-and-deterministic-limits) | Complete for unregistered candidate/main acceptance; 182 focused cases passed, no live limiter/capacity adoption. |
| **1.5.4** | [Durable controller recovery and retirement proposal](#154--durable-controller-recovery-and-retirement-proposal) | Complete for reviewed proposal; protocol tests/backend/fencing remain separate. |
| **1.5.5** | [Terminal retirement reference tests](#155--terminal-retirement-reference-tests) | Ready after 1.5.4 proposal acceptance; no durable backend or activation. |
| **1.6** | Multi-host and load verification | Open; prove preserved safety, aggregate limits and healthy-agency progress under a defined workload. Tests accompany implementation; this package also owns combined acceptance. |
| **1.7** | Monitoring and operational evidence needed for activation | Open; coordinate with SATI-OPS-001 and the readiness registry. Source checks alone do not establish operating acceptance. |

Each actionable task has four fields: **Eligibility** (why it is ready), **Scope** (its exact
deliverable), **Dependencies** (prerequisites and safeguards), and **Completion criteria** (how
completion is established, including its authority boundaries). The existing bold field labels
below remain compatible with documentation checks; their plain-language equivalents are shown
alongside them. Do not confuse these four fields with four separate tasks.

## Parallel work assignments

Follow [the coordination rules](AGENTS.md#concurrent-thread-coordination). Start with one main
integration thread and one independent investigation/review thread. Only an explicit assignment
starts work; this table does not create threads or automatically claim tasks. Keep the single main
next-item pointer below. These task numbers all belong to the existing SATI-WRK-001 backlog row.

| Task | State | Assignment / integration owner | Dependencies and safe parallel work | Edit ownership |
|---|---|---|---|---|
| **1.2 — Idle/wake scheduling** | Complete for bounded source implementation, verification and integration; unreleased | Existing main thread / main thread | Source `d05312a`; broad 1,309-case acceptance precedes the final guard, with 21 focused/private SQL cases after it. Detailed limits belong to the existing evidence record. | Existing scheduling source, topic section and evidence ownership remain unchanged. |
| **1.3.1 — Dispatch fairness design** | Complete; policy and additive source-schema adopted by explicit approval | Main integration thread / main thread | Retained design/evidence at `acf638b`; DEC-0240 records approval and scoped supersession. | Fairness design section, adoption decision and integration. |
| **1.3.2 — Dispatch fairness implementation** | Complete for bounded source verification/main integration; unreleased | Main integration thread / main thread | DEC-0240; final API 1,330 and persistence boundary 1 passed. Source migration only; operating/combined load limits retained. Independent 1.4.1 remains source-only. | Selector/worker/scheduling owner, shared cursor model/source migration, relevant tests and canonical behavior/evidence. |
| **1.3.3.1 — Polling fairness design** | Complete; main design integrated under continued-work approval | Main integration thread / main thread | Completed dispatch slice and current polling safety/source owners; source-only proposal may accompany 1.4.1 without taking its matrix. | Its W8 polling proposal, proposed decision/registry, global agenda ordering and dated evidence; no runtime/schema/test edits. |
| **1.3.3.2 — Bounded ERA feed quantum** | Complete; 35 local tests passed, unreleased | Main integration thread / main thread | DEC-0241; preserve all-page discovery and receipt effects. May accompany source-only 1.4.1. | Poller ERA quantum, relevant fail-first tests, canonical topic/agenda/evidence. |
| **1.3.3.3 — Durable polling selection** | Complete for source verification/main integration; 119 passed, unreleased | Main integration thread / main thread | DEC-0241, ERA quantum acceptance and current reset/receipt owners. | API selector/pass, shared scheduling metadata/source migration, relevant tests and canonical docs/evidence; synthetic fixtures only. |
| **1.3.3.4 — Note-maintenance fairness review** | Complete; 35 existing tests passed | Main integration thread / main thread | Existing note batch/discovery/cache/failure policy; completed polling slice. | Its W8 review, assignment/status and dated evidence; focused existing synthetic tests. No runtime policy change without a defined successor. |
| **1.4.1 — Resource-limit inventory** | Complete for main-reviewed documentation integration | Investigation chat `01a12363-741d-79b0-8b4d-dc2f7e7c5a1b`, `C:/Users/Joshu/.codex/worktrees/c3a1/Sati`, base `40e091b` / main thread | Captured source digest verified; current dispatch/polling drift reconciled against main `72cec51`. | Only its W8 inventory/assignment/evidence extracted; inherited changes and dedicated checkout preserved. No runtime source edits. |
| **1.4.2 — Shared SQL/session admission design** | Complete for main-reviewed proposal integration; adoption remains open | Same investigation chat/worktree, separately assigned by Josh / main thread | Current source corrections retained; DEC-0242 proposed; backend/enrollment/cost/value/fencing prerequisites unresolved. | Only its W8 proposal/assignment/evidence extracted; main review/decision registration. No implemented limiter or capacity claim. |
| **1.3.3.5 — Signature fairness design** | Complete; DEC-0243 selected local direction | Main integration thread / main thread | Integrated resource inventory/proposal; signature reset, projection/package/mail and revocation/recovery owners. | Its W8 source/design, decision, assignment/global ordering and dated evidence; no runtime edits in design. |
| **1.3.3.6 — Signature reset and hosted boundary** | Complete for source verification/main integration; 89 passed, unreleased | Main integration thread / main thread | DEC-0243; existing reset coordination and signature processing/identity/uncertainty owners. | Hosted service seam/timing/reset/gates, fail-first synthetic tests, canonical behavior/evidence; no schema or selection policy change. |
| **1.3.3.7 — Durable signature selection** | Complete; bounded source/main acceptance, unreleased | Main integration thread / main thread | DEC-0243; 111 signature and complete 1,404-case API inventory coverage; exact interrupted/completed run evidence and limits retained. | API selector/host scan, shared scheduling metadata/source migration, projection-query reuse and scoped mail claim, synthetic tests and canonical docs. |
| **1.4.3 — Worker SQL connection-cost review** | Complete; bounded source/documentation review | Main integration thread / main integration thread | Reviewed current source at `1802717`; conditional normal counts are not reservation costs or capacity. | Task-owned W8 accounting subsection, assignment/definition and dated working evidence only. |
| **1.4.4 — Session admission result hardening** | Complete; source verification/main integration, unreleased | Main integration thread / main integration thread | 47 fail-first failures; final 162 focused/private SQL cases passed; resource budget/fencing remain open. | API session coordinators and narrow shared result owner, actual-call-path synthetic tests, canonical topic/agenda/evidence. No schema/admission-budget change. |
| **1.4.5 — Note-worker connection-lifetime evidence** | Complete; test-only source/main acceptance | Main integration thread / main integration thread | 139 focused cases, final six and adverse extra-held control passed; actual provider-failure/pooling costs remain unproved. | Test-only shared connection event tracker and note lifetime acceptance, W8/agenda/dated evidence. No runtime/schema/budget change. |
| **1.4.6 — Signature-worker connection-lifetime evidence** | Complete; test-only source/main acceptance | Main integration thread / main integration thread | Six dependency/adverse cases and final 228 focused cases passed; physical/failure-safe costs remain open. | Test-only fixture/provider/barrier seams and lifetime cases, W8/agenda/evidence. No app/schema/admission policy change. |
| **1.4.7 — Dispatch-worker connection-lifetime evidence** | Complete; test-only source/main acceptance | Main integration thread / main integration thread | Five new boundary cases and final 110 focused cases passed; per-interval accounting clarified, physical/failure-safe costs open. | Test-only fixture/key/transport/save barriers and lifetime cases, W8/agenda/evidence. No app/schema/admission policy change. |
| **1.4.8 — Polling-worker connection-lifetime evidence** | Complete; test-only main acceptance | Main integration thread / main integration thread | Current polling/receipt/ERA quantum/reset owners; source `dd4df9e` and accepted shared probe. | Test-only fixture/wrapping/HTTP/save barriers and lifetime cases, W8/agenda/evidence. No app/schema/admission policy change. |
| **1.4.9 — Shared SQL consumer enrollment inventory** | Complete; main source/documentation acceptance | Main integration thread / main integration thread | Base `a447d0f`; bounded adjacent consumer census complete; host/closure/values remain open. | Its W8 subsection, agenda assignment/definition and dated evidence only. No runtime/schema/test changes or external/database access. |
| **1.4.10 — Onboarding admission-result hardening** | Complete; bounded main source acceptance | Main integration thread / main integration thread | Eight fail-first failures; 126 focused cases passed with private SQL collision/replay proof. | Onboarding coordinator, narrow actual-call synthetic ADO tests, W8/agenda/evidence. No schema, budget, release or data changes. |
| **1.4.11 — Mutation admission-result hardening** | Complete; bounded main source acceptance | Main integration thread / main integration thread | Nine fail-first failures; final 171 focused cases passed, including reset and exception/health boundaries. | Middleware raw-connection constructor seam/result guard, narrow actual-call synthetic tests, W8/agenda/evidence. Preserve target/exemptions/lock order and response. |
| **1.5.1 — API workload admission inventory** | Complete; bounded main source/documentation acceptance | Main integration thread / main integration thread | Base `d0e8fed`; representative route/pipeline/limits and proposed deterministic cases reviewed; no measured capacity. | Its W8 subsection, main agenda/assignment and dated evidence only; no application/schema/test changes or external/data access. |
| **1.5.2 — Shared admission adoption prerequisites** | Complete; direction and successor assignment | Main integration thread / main integration thread | Josh's October 10 direction; DEC-0244 and dated W8/evidence. | Direction/assignment accepted; backend/numbers/enrollment remain unadopted for operation. |
| **1.5.3 — Shared accounting candidate and deterministic limits** | Complete; unregistered candidate/main acceptance | Main integration thread / main integration thread | Base `18ba76a`; 182 focused cases/guard mutations and dated W8/evidence. | Model-only guarantees; no DI/route/workflow/schema or operating capacity adoption. |
| **1.5.4 — Durable controller recovery and retirement proposal** | Complete; main-reviewed proposal | Main integration thread / main integration thread | Base `73b3aaf`; W8/DEC-0245 and dated evidence/checks. | Proposal only; backend, fencing, transport values and actual-call enrollment remain unadopted/unverified. |
| **1.5.5 — Terminal retirement reference tests** | Ready; assigned main reference tests | Main integration thread / main integration thread | 1.5.4 proposal closure; DEC-0245, 1.5.3 candidate and current W8. | Unregistered reference model/fake store and deterministic tests, W8/architecture/agenda/evidence. No DI/route/workflow/schema, backend/DB/cloud/real-data access. |

At startup, the assigned thread records its actual thread identifier if available, worktree path,
starting commit and included uncommitted inputs in its own dated evidence, and sets only its own
assignment row active. Review source identity again before handoff. The main thread reconciles
the row and evidence when integrating; independent worktree status is not a shared live lock.
Do not assign the same task twice. For each later parallel assignment, record the same explicit
fields and a bounded task definition before work begins. Use blocked with a reason when a
dependency fails, awaiting review when a deliverable is ready, and complete only after integration
review and applicable checks. Broader work packages retain their separate acceptance requirements.

### 1.4.1 — Resource-limit inventory

**Eligibility:** existing W8 explicitly requires inventory of shared dependency use. This bounded
source review can precede adoption of the fairness policy. It becomes active only when Josh
assigns the second thread and it verifies a dedicated worktree contains the agreed current inputs.

**Scope:** inventory dispatch, polling, note-abandonment, signature processing and signature-mail
paths at the captured source revision. For each, identify trusted agency/account scope, selection
and query/page/batch bounds, locks and admission order, SQL connections held across dependency
work, concurrency and queue limits, deadlines/cancellation, retries/restart behavior and feature
gates. Classify deployment-wide, tenant/account, record-specific and process-local controls.
Link current source owners and test seams; identify gaps and dependencies for later 1.4 budget
design and 1.6 synthetic acceptance. Reference additional shared workers/API services as
dependencies rather than expanding into the separate 1.5 API admission implementation.

**Dependencies:** [W8](BACKGROUND_WORKERS_HANDOFF.md#w8--tenant-workload-isolation-and-worker-fairness-significant-now-required-evidence-before-a-multi-agency-pilot),
D1–D4, the scheduling/recovery decisions and [operations](OPERATIONS.md). Current observed hosting
facts remain owned by [the environment inventory](DATABASE_ENVIRONMENTS.md), with dates and
unknowns retained. Preserve reset exclusion, lock ordering, uncertainty and current billing
compliance. Resolve overlap with 1.3.1 by handing off facts, not adopting its fairness policy.

**Completion criteria:** one source-grounded matrix in a task-owned W8 subsection, citing relevant
symbols/paths and distinguishing existing enforced bounds from missing controls or proposals;
a prioritized gap/dependency list and proposed deterministic acceptance cases for later work.
Record actual source identity, drift checks and documentation structure/negative/whitespace
results in dated working evidence. Submit awaiting review with specific next budget-design work;
the main thread reviews and integrates before completion. No application/schema/test-code changes,
database access, benchmarks, real data, vendor calls, hosting/security changes, release, commit,
push or dispatch activation are authorized by this assignment. Findings establish no measured
capacity, latency, operating guarantee or sealed readiness-score change.

### 1.4.2 — Shared SQL/session admission design

**Eligibility:** Josh separately assigned the second investigation chat after its 1.4.1 handoff.
Its proposed documentation contract is awaiting main review; 1.4.1 is now integrated with current
dispatch/polling corrections. That assignment does not transfer the main queue to the second chat.

**Bounded slice:** review/integrate the proposed connection accounting, cross-host/agency bounds,
bounded waiters, interactive/recovery reserve, order and failure/restart contract for the five
inventoried paths. Compare coordination alternatives using symbolic limits. Preserve proposed
status until a reviewed choice is adopted; separate safe local source follow-ups from missing
hosting/exclusivity, numeric limits and operational evidence.

**Dependencies and owners:** W8 inventory and proposed admission contract, D1–D4, existing reset,
scheduling/recovery, external uncertainty, signature revocation/evidence and API 1.5/combined 1.6
owners. Main integration reconciles current source and owns adopted policy; actual hosting and
quota facts remain with their canonical operational owners.

**Boundaries and completion evidence:** concrete main review, source identity/drift, selected or
deferred decisions/alternatives, deterministic acceptance plan, documentation/negative/whitespace
checks and dated evidence. Documentation review only; no application/schema/test-code changes,
database, real data, benchmark, external call, runtime limiter, numeric capacity promise,
deployment or sealed evidence change. Extract only assigned sections; preserve the other checkout.

## Next eligible work

**Next eligible item:** SATI-WRK-001

**Next numbered task:** **1.5.5 — Terminal retirement reference tests**.
Josh selected overlapping instances and daily casework/recovery priority on October 10 and
authorized proposed technical limits/tests. Direction 1.5.2 and unregistered candidate tests 1.5.3
are complete. 1.5.4's reviewed proposal assigns 1.5.5 retirement reference tests.
Real backend/enrollment/closure/pool facts remain integration
and activation prerequisites; they do not prevent candidate source tests.
The completed bounded scheduling source work is **1.2**; its actual verification and limits belong
to its working evidence record. Deployment and activation remain separate.

### 1.3.1 — Dispatch fairness design

**Eligibility:** missing-key account recovery is included in completed release 1.3.39. The later
approved idle/wake slice is implemented locally under [DEC-0237](docs/decisions/current/2026-10-09-DEC-0237.md).
[W8](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-idle-wake-implementation) and the
[working ledger](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-dispatch-idle-wake-implementation)
own source acceptance and its limits; the environment inventory owns deployment facts. Dispatch
activation, aggregate capacity and sustained fairness remain open.

The source inventory and concrete [fairness design](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-fairness-design--task-131)
are now complete as a reviewable deliverable. [DEC-0239](docs/decisions/current/2026-10-09-DEC-0239.md)
is retained, with source/drift and documentation checks in [dated evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-131-dispatch-fairness-design).
Josh subsequently approved the policy and additive source-schema proposal in DEC-0240; 1.3.1 is complete.

**Bounded slice:** **Scope —** inventory and design fair dispatch selection under sustained ready backlog and
lease contention. Define eligible agency/account lanes, persisted versus process-local selection
state, preserved reset/account/dispatch admission order, cold-start behavior and a conditional
turn bound for healthy work. Inventory existing indexes/query shape and synthetic test seams;
recommend a concrete policy with alternatives and dependencies for review. This is a reversible
local source/design slice, not authorization to implement an unreviewed fairness policy.
**Current review deliverable:** agency-first/account rotation with oldest-in-lane ordering;
persisted scoped pivots and a short shared selector; a distinct known lane-skip outcome;
additive metadata/index source design; conditional offer bounds and fail-first/private SQL plan.
Review these concrete choices before implementing them; existing database operations remain separate.

**Dependencies and owners:** **Dependencies —** current typed scheduling outcomes and activity/pacing boundary,
D1–D4, [W8](BACKGROUND_WORKERS_HANDOFF.md), [operations](OPERATIONS.md), current billing compliance,
readiness and uncertainty gates. Fair selection must not infer account failure from contention,
replay uncertain sends or create unbounded tenant maps. No measured latency/resource guarantee
can be claimed from a turn-count proposal. Broader budgets, request admission, full operation
bounds, configuration consistency and owner load/alert evidence retain their separate work.

**Boundaries and completion evidence:** **Completion criteria —** a source-grounded lane/lock inventory, recommended bounded
selection policy, dependencies and fail-first/private SQL acceptance plan; review its adoption
before runtime implementation. Run documentation structure and negative checks and source drift
checks. No schema, database access, real data, hosting, vendor call, cloud/security setting, release
or dispatch activation is part of this design slice. Follow [standing upkeep](AGENTS.md#standing-work-and-documentation-upkeep);
sealed release scores remain unchanged.

### 1.3.2 — Dispatch fairness implementation and tests

**Eligibility:** Josh explicitly approved DEC-0239's reviewed policy/additive source-schema;
DEC-0240 adopts it. Starting source is `acf638b8b840fdabcde443ee005b35a14221e6a6` with unrelated
project formatting and assessment scratch excluded.

**Bounded slice:** implement the adopted short SQL selector, persisted agency/account pivots,
oldest-in-lane ordering, typed known LaneSkipped outcome, shared model/source migration/index
and fail-first/private synthetic acceptance. Keep one existing hosted scheduling owner.

**Dependencies and owners:** W8's adopted contract, DEC-0240, existing recovery/scheduling,
reset/account/dispatch/claim-release lock order, current billing compliance and uncertainty.
The secondary 1.4.1 is source-only; preserve its checkout and reconcile its findings later.

**Boundaries and completion evidence:** retain unfixed failures, prove bounded selection and
classification with actual runtime/fake time, and prove shared selector/disposal/restart/rollback/
same-work safety with private SQL. Validate both EF models and generated migration, run appropriate
regressions and documentation structure/negative/whitespace checks. Record actual failures/counts/
skips/cleanup and coverage limits before completion. Source migration generation and newly owned
synthetic schema tests are authorized; no existing/shared/working/cloud database, real data,
vendor call, deployment, migration apply, baseline reset or activation is included.

**Completion:** main source acceptance is complete under DEC-0240; final API 1,330 and persistence
boundary 1 passed with zero failed/skipped. [W8](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-fairness-implementation--task-132)
and the dated ledger own actual fail-first/mutation/SQL evidence, source schema and remaining
operational limits. Broader 1.3/1.4/1.6 acceptance remains open.

### 1.3.3.1 — Polling fairness design

**Eligibility:** after bounded dispatch 1.3.2 acceptance, polling remains a distinct W8 fairness
gap. Revalidate its current source/feature gates and topic owners; do not treat dispatch's turn
bound as polling acceptance. The main thread owns this next design; ready does not start work.

**Bounded slice:** source inventory and reviewable fair polling proposal covering agency/account/
feed selection, retained cursor/receipt semantics, healthy-lane opportunity under backlog or a
busy lane, shared-failure behavior, restart and bounded discovery/state. Define deterministic
fail-first/private SQL acceptance and alternatives without adopting weights or runtime policy.

**Dependencies and owners:** W8, `ClaimMdSandboxPoller`, vendor request admission/deadline,
feed checkpoints/response ingestion, reset/account/receipt/compliance safety and DEC-0240's
dispatch-only scope. Link reviewed 1.4.1 resource facts when available; it stays independently
assigned. Inventory relevant polling facts here without taking over its resource-limit matrix.

**Boundaries and completion evidence:** main-owned W8 polling proposal, unused proposed decision
record/registry, agenda and dated evidence only. Revalidate source identity/drift, distinguish
enforced versus proposed controls, specify bounded tests and operational dependencies, and run
documentation structure/negative/whitespace checks. No application/schema/test implementation,
database access, benchmarks, real data, external calls, hosting, activation or release. Submit
the concrete proposal for adoption before implementation. The main thread owns integration.

**Completion:** source-grounded design is integrated in
[W8](BACKGROUND_WORKERS_HANDOFF.md#october-9--polling-fairness-design--task-1331).
Josh's subsequent continued-work instruction authorizes routine local choices; DEC-0241 records
the main thread's selected policy and its source-schema/operating limits. This supersedes only
this task's pending adoption restriction, without representing a separate line-by-line review.

### 1.3.3.2 — Bounded ERA feed quantum

**Eligibility:** DEC-0241 selected under continued-work authority; this independent source change
can be verified before durable scheduling implementation.

**Bounded slice:** retain complete bounded listing validation, then download/import only the
oldest ERA per feed visit. No new source schema, cursor policy, cadence, quota or feature flag.

**Dependencies and owners:** poller, connector, receipt ingestion and expected-cursor atomicity;
protect nonascending listing order, missing/damaged cursors, reset and caller cancellation.

**Boundaries and completion evidence:** fail-first two-artifact regression plus existing paging,
receipt, key/wrapping/deadline and reset regressions. Update canonical W8/agenda/working evidence,
run documentation structure/negative/whitespace checks, commit and push verified source under
standing authority. Synthetic fixtures only; no existing/shared database, vendor, cloud or release.

### 1.3.3.3 — Durable polling selection

**Eligibility:** after 1.3.3.2 acceptance; DEC-0241 local policy and additive source-schema choice.

**Bounded slice:** persisted agency/account/feed selector, bounded 100-distinct-feed pass,
short candidate context lifetime, strict gate/scope checks, three shared scheduling entities,
source migration/selection indexes and deterministic fail-first/private synthetic acceptance.

**Dependencies and owners:** W8 design, DEC-0241, current reset/poller/vendor locks, independent
receipt cursors and authoritative status/ERA ingestion; resource 1.4.1 stays independently assigned.

**Boundaries and completion evidence:** rotation/count/mutation tests and private SQL ownership,
restart/rollback/scope/provider tests; both shared EF models and source migration parity. Run
appropriate regressions and documentation checks; record actual outcomes/limits before integration.
Only newly owned synthetic database tests/source migration generation; no existing database apply,
real data, vendor call, hosting/security settings, activation or release. Commit/push verified chunks.
### 1.3.3.4 — Note-maintenance fairness review

**Eligibility:** bounded polling source acceptance is complete. Existing note maintenance already
limits each due agency to one 100-note batch per sweep; review that behavior before adding policy.

**Bounded slice:** source-grounded review of agency opportunity, discovery membership, completion
cache and restart behavior; run the existing focused note-worker acceptance with owned synthetic
SQL fixtures. Distinguish stable-pass opportunities from elapsed-time, crash/restart and capacity
gaps. Define the next bounded fairness task rather than treating passing tests as broader closure.

**Dependencies and owners:** DEC-0222–DEC-0225, W8 note maintenance, shared reset/sweep leases,
authoritative abandonment rules and scheduling/recovery owners. Resource investigation remains
independently owned until main review/integration.

**Boundaries and completion evidence:** canonical W8 finding, actual targeted results and limits,
agenda/evidence reconciliation and documentation/negative/whitespace checks. Source review and
existing synthetic tests only; preserve business policy, runtime/schema/test code, deployment,
real data, external calls and sealed release evidence. Commit/push verified documentation chunk.

### 1.3.3.5 — Signature fairness design

**Eligibility:** polling and note bounded review are complete; resource inventory and admission
proposal are integrated. Signature fairness is independent of unadopted numeric admission limits.

**Bounded slice:** source-grounded design for agency/phase/item selection across compliance
projection, package preparation and mail. Inventory eligibility, stalled/damaged heads, process
cursor/restart, query bounds, reset participation and gate/cancellation checks. Propose durable
opportunity/quantum and deterministic acceptance without shortening evidence/revocation locks,
changing business eligibility or automatically retrying uncertain submissions.

**Dependencies and owners:** signature feature/identity, persistence entities/model/migration,
projection semantics, immutable package/outbox and mail GUID/lease/revocation recovery, Demo reset,
W8 inventory, DEC-0242 proposed resource contract and D1–D4. Review reset safety separately before
any runtime fairness implementation; metadata offers are not completion/delivery evidence.

**Boundaries and completion evidence:** canonical source/decision/evidence/agenda update, concrete
preserved-safety and fail-first/private-synthetic plan, explicit conditional progress/remaining
limits, documentation structure/negative/whitespace checks. Documentation/source review only;
no runtime/schema/test code, database, real data, vendor/cloud call, benchmark, deployment or sealed
release evidence change. Main owns later adoption and implementation; commit/push reviewed chunk.

### 1.3.3.6 — Signature reset and hosted boundary

**Eligibility:** DEC-0243 selected under continued-work authority; source design establishes
reset participation as a prerequisite for durable signature scheduling.

**Bounded slice:** preserve existing selection/three phases/quantum; extract an internal pass,
inject hosted timer TimeProvider, join shared Demo reset before discovery/effects and recheck
matching signature/API identity, gates and cancellation before later work.

**Dependencies and owners:** signature feature/options, hosted service, zero-retry processor
scopes and existing reset coordination. Preserve package/clinical/mail evidence, cancellation,
revocation and uncertainty. A held reset lease is exclusion, not external-reset retention proof.

**Boundaries and completion evidence:** fail-first reset-exclusion test against original scan,
disabled/mismatch/timing/cancellation/fault tests, private SQL exclusion/lifetime/disposal and
appropriate signature regressions; canonical docs/evidence/agenda and documentation checks.
Owned synthetic fixtures/source changes only; no schema, existing database, real data, vendor,
cloud/security, activation, migration, deployment or release. Commit/push verified bounded chunk.

### 1.3.3.7 — Durable signature selection

**Eligibility:** after 1.3.3.6 acceptance; DEC-0243 defines local policy/additive source schema.

**Bounded slice:** two scheduling entities with per-phase seed and per-agency/item positions,
short verified zero-retry selector, ten distinct item offers per each of three phases, shared
projection candidate query, conditionally scoped mail claim and preserved processor semantics.
Generate additive source migration/indexes and deterministic fail-first/private acceptance.

**Dependencies and owners:** selected W8/DEC-0243 design, accepted reset/timing boundary, current
projection/clinical scope, package/blob/key/orphan and mail lease/GUID/revocation/attempt owners.
Resource DEC-0242 remains proposed; this implementation claims scheduling opportunities only.

**Boundaries and completion evidence:** fail-first agency/damaged-head/restart behavior, scope/
count/gate/cancellation effects, SQL ownership/commit uncertainty/order/restart, shared model/index
parity and guarded source-migration Down; meaningful API/signature/portal regressions. Record all
failures/limits and run documentation/negative/whitespace checks. Source schema and owned synthetic
tests only; no existing apply/baseline/host rollout, real data, external call, activation or release.

### 1.4.3 — Worker SQL connection-cost review

**Eligibility:** after durable signature selection acceptance; admission proposal DEC-0242 requires
source-grounded simultaneous-open costs before runtime/backend adoption. Continued Agenda authority
permits this bounded local review; a ready row does not start another thread.

**Bounded slice:** trace exact-Demo dispatch, polling, note sweep, signature projection/package/mail
connection owners and admission/selection/processing/cleanup spans. Count explicitly open lease
sessions separately from idle contexts, provider/transaction opens and SQL command counts. Report
source-derived normal-path maxima with assumptions, unresolved failure/retry/closure costs and
prioritized missing enrollment/hosting facts. Identify deterministic synthetic counter/barrier seams
for later acceptance. Inventory adjacent HTTP/reset/control-plane/outside consumers as dependencies,
without claiming a complete API/deployment consumer census or numerical capacity.

**Dependencies and owners:** current W8/DEC-0242, accepted dispatch/poll/signature source, existing
reset, billing, note transaction/retry and mail revocation owners. Main owns policy adoption and
integration; canonical operations/environment documents retain actual hosting facts and unknowns.

**Boundaries and completion evidence:** reviewable source-cited accounting matrix, explicit costs/
assumptions/unknowns, prioritized next bounded step, source identity and dated evidence; documentation
structure/negative/whitespace checks. Documentation/source review only; no application/schema/test
change, database, real data, benchmark, provider call, infrastructure, runtime admission, capacity
promise, deployment or sealed release change. Commit/push the verified documentation chunk.

### 1.4.4 — Session admission result hardening

**Eligibility:** completed 1.4.3 identifies coercible/malformed scalar and cancellation-race gaps
in existing session lock admission. Continued Agenda authority permits this bounded local guard;
it does not adopt the proposed resource admission backend or capacity values.

**Bounded slice:** require SQL Int32 0/1 for acquired and -1 for known contention in existing
Demo worker reset/dispatch/account, Claim.MD request/poll and note sweep session coordinators.
Cancellation observed after scalar completion wins before callback admission. Preserve each
caller's existing contention outcome, lock names/modes/order, waits, pacing, release and gates.
Use actual coordinator callback paths with synthetic ADO results and owned cleanup observations.

**Dependencies and owners:** W8 accounting, existing session/reset, note and billing/polling owners;
current selector strict contracts are precedent, not a new runtime budget. Main integrates.

**Boundaries and completion evidence:** meaningful fail-first malformed/cancel-race cases, valid
0/1/contention and failure cleanup coverage, focused existing private-instance SQL regressions,
documentation structure/negative/whitespace checks and dated evidence. Synthetic data only; no
existing database apply, schema, vendor, real-data, infrastructure, deployment or sealed evidence
change. No mid-callback fencing or failure-safe reservation claim. Commit/push verified source.

### 1.4.5 — Note-worker connection-lifetime evidence

**Eligibility:** 1.4.3 needs deterministic evidence before registering connection costs. Begin
after 1.4.4 closure; main owns this bounded test-only slice under continued Agenda authority.

**Bounded slice:** track open attempts, successful opens and confirmed close/disposal across
all contexts of real note-worker coordination/discovery/sweep on a private synthetic SQL fixture.
Observe normal discovery/transaction intervals, precommit failure/cancellation unwind, repeated
same-day completion and a second caller's contention. Assert note/revision/audit effects alongside
connection evidence; distinguish in-flight/unknown cleanup from confirmed closure. No benchmarks.

**Dependencies and owners:** W8 accounting and existing note transaction/retry/reset/completion
rules. Only the private named test instance is used; no shared LocalDB or application database.

**Boundaries and completion evidence:** deterministic barriers, normal/error/cancellation and
independent-call cases; meaningful adverse-control proof that tracker detects held extra SQL
connections; relevant regressions, documentation structure/negative/whitespace and dated evidence.
This test evidence cannot prove physical pool/session cost, failed provider closure, multi-host
capacity, complete enrollment or a safe reservation K. No app/schema/admission policy, real data,
vendor/cloud call, deployment or sealed evidence change. Commit/push verified bounded tests/docs.

### 1.4.6 — Signature-worker connection-lifetime evidence

**Eligibility:** completed 1.4.5 supplies a reviewed logical connection probe; current signature
source is implemented and accepted locally. Start this test-only main assignment after closure.

**Bounded slice:** run actual hosted signature selection/projection/package/mail with real reset
coordination in owned synthetic SQL and synthetic blob/key/email providers. Observe selector
disposal before fresh business work, projection/package transaction spans, package dependency
barriers, mail POST protection and existing-GUID GET recovery. Verify effects/rollback and
preserved uncertainty on dependency faults/cancellation alongside logical connection events.

**Dependencies and owners:** W8 accounting, DEC-0243, projection/package/mail revocation and
operation-GUID recovery owners. Keep all transactions/order/gates/evidence rules intact.

**Boundaries and completion evidence:** deterministic normal/fault/cancel barriers, no unintended
new mail during recovery, confirmed owned cleanup, meaningful adverse connection control and
focused regressions; documentation structure/negative/whitespace plus dated evidence. Tests only,
no runtime/schema, existing DB, real data, vendor/cloud call, benchmark, resource policy or sealed
evidence change. Logical synthetic intervals do not establish physical pooling/fencing, failure-safe
K, global limits or deployed capacity. Commit/push verified tests/docs; main owns integration.

### 1.4.7 — Dispatch-worker connection-lifetime evidence

**Eligibility:** completed 1.4.6 establishes reusable source-only dependency-barrier acceptance;
dispatch's enumerated four-connection intervals still need private synthetic SQL evidence.
Start the explicit main test-only assignment after predecessor closure.

**Bounded slice:** run real dispatch selection/preflight/compliance/evidence with real reset,
dispatch/account and vendor coordination, synthetic keys/wrapping and intercepted HTTP. Observe
key/wrapping preflight, Sending decision transaction, upload, response wrapping and evidence
commit. Preserve/verify account release before vendor admission, accepted/uncertain/missing-key
record effects and known cleanup. Include deterministic cancellation/fault and adverse connection
control; no benchmark or actual vendor request.

**Dependencies and owners:** W8 accounting, accepted dispatch fairness, claim release/compliance,
known-unsent account recovery, exchange deadline and external uncertainty owners. Protective
record admission and evidence transactions remain intact; use private test SQL only.

**Boundaries and completion evidence:** actual callback/transaction/HTTP barriers, persisted
dispatch/attempt/readiness evidence, zero held on confirmed cleanup and meaningful adverse guard;
focused regressions, documentation structure/negative/whitespace and dated evidence. Tests only,
no runtime/schema/business policy, existing DB, real data, vendor/cloud/security, benchmark,
activation or sealed evidence change. Logical intervals do not establish physical/failure-safe
K, host/global capacity or deployed acceptance. Commit/push verified tests/docs.

### 1.4.8 — Polling-worker connection-lifetime evidence

**Eligibility:** completed 1.4.7 leaves polling's normal intervals and receipt scope/cleanup proof
as the remaining worker connection-cost acceptance slice. Explicit main test-only assignment,
start after closure; no resource backend/value adoption follows.

**Bounded slice:** run actual poller/selector, SQL reset/global-poller/vendor coordination,
status receipt processor and scoped ERA ingestion with synthetic retained claim evidence and
intercepted connector HTTP. Observe request/pacing, receipt wrapping outside decision SQL,
receipt/cursor/audit transactions and independent-caller contention. Verify no cursor/effect
advance on dependency failure/cancellation, healthy feed/later progress and no replay duplicates.
Use deterministic barriers/adverse extra-held control and distinguish confirmed logical cleanup.

**Dependencies and owners:** W8 accounting, DEC-0241, current independent receipt cursors,
bounded ERA quantum, claim release and external evidence/reset owners. Only owned synthetic SQL;
source scopes/transactions/order/gates/retry/quotas remain unchanged.

**Boundaries and completion evidence:** actual callback/transaction/HTTP and wrapping observations,
persisted receipt/outcome/cursor evidence, independent-call/adverse control, focused regressions,
documentation structure/negative/whitespace and dated evidence. Tests only; no runtime/schema,
existing DB, real data, actual vendor/cloud/security call, benchmark, activation or sealed evidence
change. Logical successful intervals do not establish physical/failure-safe K, complete enrollment,
global capacity or deployed operating acceptance. Commit/push verified tests/docs.

### 1.4.9 — Shared SQL consumer enrollment inventory

**Eligibility:** worker connection intervals have bounded synthetic evidence. DEC-0242 still
requires enrollment of adjacent SQL consumers before a global budget or backend can be adopted.
Explicit main source-review assignment under continued Agenda authority; start after 1.4.8 closure.

**Bounded slice:** map API pipeline/route/service contexts, raw ADO owners, startup/health,
signature portal, reset/watchdog/compliance/seed and migration/operator/desktop paths. Distinguish
same-database candidates from separate local consumers, source gates from observed deployment,
and context creation from connection checkout. Identify acquisition order, scope, dependency
spans, bypasses and existing test seams. Prioritize deterministic follow-up acceptance and
missing host/closure/enrollment/objective facts without inventing capacity or runtime policy.

**Dependencies and owners:** current API identity/authorization, reset/recovery, operations,
deployment inventory and scheduling owners; DEC-0242 remains proposed. Preserve record admission,
tenant authorization and existing real-data/infrastructure boundaries.

**Boundaries and completion evidence:** source-grounded matrix, current source identity, explicit
unknowns and next bounded step; documentation structure, negative and whitespace checks. Edit
only its W8 subsection, main agenda/status and dated evidence. No application/schema/test code,
database/data access, external calls, benchmark, deployment or sealed evidence changes. Commit/push
reviewed documentation under existing repository approval.

### 1.4.10 — Onboarding admission-result hardening

**Eligibility:** completed 1.4.9 found `Convert.ToInt32` accepts unconfirmed/coercible onboarding
lock results. The shared strict admission guard is accepted and worker-tested; use a bounded main
repair assignment, preserving existing onboarding behavior for confirmed results.

**Bounded slice:** exact integer 0/1 admit, -1 stays busy, all other scalar shapes fail safely.
Recheck caller cancellation after scalar before continuation. Keep serializable transaction-owned
lock on caller context; no added session lock/release or resource policy. Extend synthetic ADO
actual-call seam and prove regression fails before repair, then verify focused onboarding/guard
regressions with synthetic fixtures and owned private SQL only.

**Dependencies and owners:** current onboarding route, collision/audit/tenant/reset boundaries,
SqlSessionAdmission, proposed DEC-0242. Mutation middleware gap is separate; no claimed repair
there. Preserve unrelated checkout work and transaction disposal ownership.

**Boundaries and completion evidence:** malformed/valid/contention/cancellation/command-failure
actual-call cases, pre-fix failures, transaction ownership/cleanup and existing two-host collision
acceptance; documentation structure, negative and whitespace checks. No schema, existing database,
real data, actual vendor/cloud/security, deployment, benchmark or sealed evidence changes. Commit
and push verified source/tests/docs under existing authority; main owns integration.

### 1.4.11 — Mutation admission-result hardening

**Eligibility:** 1.4.9 found a separate raw mutation lock conversion gap; finish 1.4.10 before
starting this explicit main repair. Reuse accepted strict scalar guard without changing reset
exclusion, business permission, resource budgets or source target mapping.

**Bounded slice:** add constructor-injected internal synthetic connection seam with the public
middleware constructor retaining the exact configured raw SqlClient path. Test actual middleware
with unfixed scalar conversion, then reject all unconfirmed results and post-scalar cancellation.
Preserve GET/HEAD/reset/non-Demo/non-SQL exemptions, zero-wait shared session reset lock,
known-contention 503 payload, confirmed lock release even when cancellation/next throws, and
owned connection disposal. No extra connection, session policy or runtime budget adoption.

**Dependencies and owners:** SqlSessionAdmission, Demo mutation/reset/exception boundary, current
configured database scope and existing fixture/ADO seams. Known -1 is contention; unexpected
negative/failure results fail safely rather than masquerading as normal busy. No bypass of actor
validation or existing authorizations; retain command/dependency ownership.

**Boundaries and completion evidence:** actual-call malformed/result/cancellation/next/command/
release failure and exemptions, fail-first evidence, focused middleware/reset/onboarding/guard
checks and documentation structure/negative/whitespace. Synthetic fixtures and owned private SQL
only; no schema, existing database, real data, provider/cloud/security, release/activation,
benchmark or sealed evidence changes. Commit/push verified chunk; main owns integration.

### 1.5.1 — API workload admission inventory

**Eligibility:** 1.4.9 identified API enrollment dependencies; complete 1.4.11 before starting
this explicit main source-review assignment. Host/budget choices are pending but do not prevent
an independent source inventory of current API controls and deterministic future acceptance.

**Bounded slice:** map authentication/pipeline order and representative expensive route families,
current rate/body/parser/page/item controls, transaction/dependency spans and missing agency/actor/
global concurrency/queue/deadline protections. Separate request bytes, stored rows, CPU/dependency
work and SQL checkout; identify test seams and prioritized acceptance without claiming exhaustive
route costs. Link existing security findings and propose next bounded adoption requirements.

**Dependencies and owners:** SATI-SEC-001, existing identity/tenant/replay/record rules, DEC-0242,
W8 consumer inventory and logging/recovery owners. Validated stored actor/agency remains authority;
reserve validation capacity before a trustworthy actor exists. Preserve current response contracts.

**Boundaries and completion evidence:** source-grounded family matrix, current identity/limits,
actual source checks and proposed deterministic acceptance, documentation structure/negative/
whitespace checks and dated evidence. Only W8 subsection, agenda and evidence edits. No runtime/
schema/test changes, existing database, real data, external calls, load/benchmark, deployment,
numeric capacity adoption or sealed evidence changes. Commit/push reviewed docs; main integrates.

### 1.5.2 — Shared admission adoption prerequisites

**Eligibility:** Josh supplied overlapping-instance and daily casework/recovery direction and
authority to propose/test technical limits on October 10. Current costs/inventories support a
source-only candidate; deployment/session closure/enrollment facts still gate operating adoption.

**Bounded slice:** reconcile the proposed DEC-0242 admission contract with the selected direction
and assign one next proof/design slice with a declared hosting/enrollment envelope. Identify owner
exclusivity or consistent controller, cost/uncertainty debt, validation/interactive/worker/recovery
reserves, finite waiters/overload contract and dependency/physical-pool envelopes. Backend/value
adoption and runtime implementation need a concrete reviewed successor, not arbitrary defaults.

**Dependencies and owners:** Josh's intended deployment and operating targets; canonical dated
environment inventory/operations, SATI-SEC-001 and proposed DEC-0242. Raw middleware, portal,
Function/children, health/startup, tooling and outside consumers must be enrolled or explicitly
excluded by an enforceable boundary. Preserve identity/record/reset/uncertainty owners.

**Boundaries and completion evidence:** record selected or still-missing decisions/facts, scope a
single deterministic proof/design successor, run documentation checks and retain dated evidence.
This prerequisite status does not authorize release, migration, cloud/security changes, real data,
benchmark or runtime activation. Current unrelated work and sealed release evidence stay preserved.

**Result:** [W8 direction](BACKGROUND_WORKERS_HANDOFF.md#october-10--overlapping-host-budget-direction--task-152),
DEC-0244 and [dated evidence](docs/readiness/work-evidence.md#2026-10-10--sati-wrk-001-152-overlapping-host-budget-direction)
close direction/assignment only. Multiple hosts and protected daily/recovery capacity selected;
main 1.5.3 assigned. Numeric operating budgets, backend and enrollment remain later prerequisites.

### 1.5.3 — Shared accounting candidate and deterministic limits

**Eligibility:** after 1.5.2 direction/decision closure; Josh explicitly authorized proposed limits
and tests. Present hosting is not capacity proof.

**Bounded slice:** implement an unregistered server accounting candidate for one atomic shared
controller model, server-owned costs, protected class/agency shares, exact target/epoch/request/
incarnation and non-expiring uncertainty debt. Synthetic profile: 32 logical credits split daily
12/recovery 8/background 8/validation 4; scoped agency cap 4 per class, zero parked waiters,
128 retained admission/replay records and 16 child connection identities per grant. Test overlap,
saturation/reserves, replay/conflict, cancellation, nested opens/confirmed closure, crash/restart
and bounded metadata. Mutate core guards to prove the adversarial tests detect weakened logic.

**Dependencies and owners:** DEC-0244/DEC-0242 and W8/identity/reset/record/uncertainty owners.
No caller-controlled class/cost, lease-expiry credit reuse or inner reacquisition. A shared
in-memory model is not a distributed backend or live enforcement.

**Boundaries and completion evidence:** deterministic tests/mutation failures, source identity,
canonical ownership/evidence and structure/negative/whitespace checks. No DI/route/workflow changes,
schema, runtime activation, existing DB, real data, external/cloud/service calls, benchmark or
sealed evidence changes. Main integrates/commits/pushes verified chunk; backend/enrollment is separate.

**Result:** [W8 model](BACKGROUND_WORKERS_HANDOFF.md#october-10--shared-accounting-candidate--task-153)
and [dated evidence](docs/readiness/work-evidence.md#2026-10-10--sati-wrk-001-153-shared-accounting-candidate):
182 focused cases passed, four weakened guard variants detected, source restored exactly and
documentation gates passed. Candidate only; no running workflow or operating budget changed.

### 1.5.4 — Durable controller recovery and retirement proposal

**Eligibility:** after 1.5.3 source/model acceptance. Josh authorized multiple-instance design and
daily/recovery priority; numeric operating adoption remains separate. Main owns this proposal.

**Bounded slice:** specify a concrete durable admission/recovery/retirement protocol and backend
selection requirements. Model lost responses, duplicate/conflicting requests, controller restart,
old-owner debt, stale releases, terminal replay retirement and target/epoch fencing. Define finite
controller-call admission/deadlines and protected recovery access; identify enrollment ordering
before identity/mutation SQL and expensive work. Independent model copies cannot serve as a
deployment budget. Do not reclaim live debt based only on clocks or disappearance of a process.

**Dependencies/owners:** DEC-0244/DEC-0242; W8 candidate/lifetime and API/adjacent consumer
inventories; operations/security/identity/reset/record/uncertainty owners. Physical pools, outside
consumers and failure-safe costs remain integration prerequisites. If a concrete backend cannot
satisfy closure/restore/fencing, retain a proposed alternative and exact proof gap, not adoption.

**Boundaries/acceptance:** source-grounded protocol/failure matrix, bounded deterministic successor,
dated evidence and documentation structure/negative/whitespace checks. Docs/proposal only: no
runtime registration, new infrastructure, existing database/schema/data access, cloud/security
change, release/deployment, benchmark or sealed evidence change. Later implementation/enrollment
gets its own assignment; this proposal requires no new product choice already supplied by Josh.

**Result:** [W8 proposal](BACKGROUND_WORKERS_HANDOFF.md#october-10--durable-controller-protocol-proposal--task-154),
proposed DEC-0245 and [dated evidence](docs/readiness/work-evidence.md#2026-10-10--sati-wrk-001-154-durable-controller-protocol-proposal)
complete the bounded protocol review. Structure, 22 negative proofs and whitespace passed;
no application/database/provider tests or backend adoption. Main 1.5.5 assigned.

### 1.5.5 — Terminal retirement reference tests

**Eligibility:** after 1.5.4 proposal/main review and documentation acceptance. DEC-0244 authorizes
proposed-limit reference tests; DEC-0245 remains proposed for operation. Main owns this slice.

**Bounded slice:** test normal terminal retirement/high-water rejection with 16 bounded owner
identities, 128 retained requests and exact slot/generation/request sequence. Use an injected
synthetic compare/commit/response-loss store seam, never claim disk durability. Include over-128
sequential completions, delayed replay after retirement, live/uncertain holes, fingerprint conflict,
wrong target/epoch, old generations, competing commits, lost response, client restart preserving
the same fake state and numeric overflow. Positive terminal retirement only; no automatic slot
reuse, epoch reset or uncertain-debt reconciliation. Preserve shared cost/profile ownership.

**Acceptance/boundaries:** deterministic tests and meaningful guard mutation failures, exact source
restoration, source identity and canonical evidence/architecture/agenda, structure/negative/
whitespace checks. Unregistered reference model only; no DI/routes/workflows/schema, actual backend
service/DB/real data/cloud calls, benchmark, release/deploy or sealed evidence change. Real storage,
bounded transport/fencing and actual-call enrollment need separately defined acceptance successors.

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
