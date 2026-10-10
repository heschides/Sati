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
| SATI-WRK-001 | partial; missing-key recovery released; idle/wake source implemented locally; broader W8 open | W8 workload isolation, agency/account fairness, API admission, global/tenant budgets and synthetic multi-host/load proof; [scheduling implementation](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-idle-wake-implementation), [working evidence](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-dispatch-idle-wake-implementation). |
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

## Next eligible work

**Next eligible item:** SATI-WRK-001

**Eligibility:** missing-key account recovery is included in completed release 1.3.39. The later
approved idle/wake slice is implemented locally under [DEC-0237](docs/decisions/current/2026-10-09-DEC-0237.md).
[W8](BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-idle-wake-implementation) and the
[working ledger](docs/readiness/work-evidence.md#2026-10-09--sati-wrk-001-dispatch-idle-wake-implementation)
own source acceptance and its limits; the environment inventory owns deployment facts. Dispatch
activation, aggregate capacity and sustained fairness remain open.

**Bounded slice:** inventory and design fair dispatch selection under sustained ready backlog and
lease contention. Define eligible agency/account lanes, persisted versus process-local selection
state, preserved reset/account/dispatch admission order, cold-start behavior and a conditional
turn bound for healthy work. Inventory existing indexes/query shape and synthetic test seams;
recommend a concrete policy with alternatives and dependencies for review. This is a reversible
local source/design slice, not authorization to implement an unreviewed fairness policy.

**Dependencies and owners:** current typed scheduling outcomes and activity/pacing boundary,
D1–D4, [W8](BACKGROUND_WORKERS_HANDOFF.md), [operations](OPERATIONS.md), current billing compliance,
readiness and uncertainty gates. Fair selection must not infer account failure from contention,
replay uncertain sends or create unbounded tenant maps. No measured latency/resource guarantee
can be claimed from a turn-count proposal. Broader budgets, request admission, full operation
bounds, configuration consistency and owner load/alert evidence retain their separate work.

**Boundaries and completion evidence:** a source-grounded lane/lock inventory, recommended bounded
selection policy, dependencies and fail-first/private SQL acceptance plan; review its adoption
before runtime implementation. Run documentation structure and negative checks and source drift
checks. No schema, database access, real data, hosting, vendor call, cloud/security setting, release
or dispatch activation is part of this design slice. Follow [standing upkeep](AGENTS.md#standing-work-and-documentation-upkeep);
sealed release scores remain unchanged.

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
