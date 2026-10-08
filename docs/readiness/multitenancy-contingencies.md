# Multitenancy and repeat-operation contingencies

This catalog is the review and acceptance plan for agency separation, shared-capacity fairness, safe retries and their operating dependencies. [readiness.json](readiness.json) owns the criterion IDs, evidence requirements, stages and immutable release history. [readiness-method.md](readiness-method.md) defines scoring; [protocol-baseline.md](protocol-baseline.md) defines the proposed request/retry contract. This document does not credit planned controls as implemented.

No finite inventory can predict every future risk. The initial rubric covers the known architecture, 42 criteria and the 48 scenarios below. New features, routes, stores, providers, tenancy models, releases and incidents trigger another boundary review and, where scope changes, a new rubric. “All checks passed” means the declared scenarios and conditions passed, not a promise that no new failure can occur.

## Present boundaries and explicit gaps

The October 8 assessment reviewed release 1.3.37 at `a1af92129f60728a0bbcf0dd27c42190644b1e3b`. Current stored actor checks, explicit tenant predicates, selected composite relationships, revisions, targeted SQL application locks, durable dispatch and receipt/cursor effects are meaningful existing controls. Their targeted tests do not establish every release path, deployed principal or scale condition.

Concrete existing gaps include the global oldest-dispatch/preflight poison path, serial account polling, completion bookkeeping and provisioning invalidation, note-sweep fairness/pass budgets, total operation/account/pass deadlines and live deadline proof, expensive-work admission, a complete structural tenancy strategy and tenant lifecycle evidence. The October 8 local slices isolate narrowly recoverable note-agency faults, supply the connector HTTP I/O deadline including response bodies, and bound each note-worker agency-ID discovery to 100 IDs within a captured finite key range; [working evidence](work-evidence.md) records their fail-first proof, actual checks and remaining limits. Discovery does not freeze membership: within-range inserts after the last discovery query may be missed even above the cursor, along with behind-cursor/lower-ID changes and growth after the final check. Bounded observed higher-ID growth (or any agency after an empty capture) leaves the day due. These component controls do not close scenarios 12/13/16 or establish a healthy-agency wait bound. Billing R1/R2 remain separate: a fresh generation key after acceptance and incomplete current documentation/compliance recheck before send. Ordinary API note audits and arbitrary full-exception redaction also remain unresolved. See [the assessment](../../reports/SATI_ARCHITECTURE_ENGINEERING_ASSESSMENT_2026-10-08.md), [the worker handoff](../../BACKGROUND_WORKERS_HANDOFF.md) W8 and the ledger's notes for the bounded current status.

The fourth local worker slice rotates completion-cache storage on the captured local date under
the existing run gate, retaining same-day success skips and failed-agency retries. Its regression
checks retained cardinality across synthetic date/population changes, not measured memory or
current-day churn/capacity. Rotation runs only after an enabled gate-acquired check; disabled
workers do not gain an idle scheduler. [DEC-0225](../decisions/current/2026-10-08-DEC-0225.md) and
working evidence retain actual local checks and limits. This does not close scenario 16 or remove
same-day provisioning/removed-agency cache limitations. The bounded billing export/replay repair
now retains caller-authoritative residual compliance blockers even with a matching stored
exception; [DEC-0226](../decisions/current/2026-10-08-DEC-0226.md) and
[the sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md#current-export-and-replay-compliance--local-source-october-8-2026)
own its scope and evidence limits. Exact-obligation exception and Admin recovery decisions remain
with their existing shared owners. R1 physical-send/original-correction lifecycle and full
exact-retained-subset queue/pre-send R2 remain open, including coordinated source-change races
and a purpose policy for payer-held voids. The bounded request-exception boundary now contains
escaping downstream failures before raw exceptions reach framework logging/hosting diagnostics;
[the logging owner](../../LOGGING_DESIGN.md#api-request-boundary--source-october-8-2026) and
[DEC-0227](../decisions/current/2026-10-08-DEC-0227.md) record scope and observability tradeoffs.
General EF/provider, health/startup and callback redaction remain open. The incident aggregator's
explicit transaction/execution-scope gap is the next separate repair with two genuine failing
baselines. Non-retrying boundary-fixture incident positives do not establish production recording;
these local slices do not close scenario 42 or advance sealed readiness.

The dated hosting inventory has one owner: [DATABASE_ENVIRONMENTS.md](../../DATABASE_ENVIRONMENTS.md). Consult its captured evidence and refresh requirements rather than copying a configuration here. Agency IDs and clearinghouse IDs do not allocate physical resources. A dedicated worker can still share SQL, key/storage/mail services and a vendor quota. RLS, separate databases, partitioned pools and dedicated resources are design options to evaluate, not universal prescriptions.

## Failure response rules

Readiness reporting itself is a release dependency: corrupt/missing version data fails unavailable;
concurrent report writers serialize and refuse changed inputs; history and rubric corrections append
rather than erase. Monitor ledger capacity and follow the versioned-history migration in
[the method](readiness-method.md#ledger-durability-and-growth) before its bounds are reached.
An installed offline assessment remains dated evidence and cannot detect later incidents by itself.

- **Suspected cross-agency release or unauthorized change:** contain the affected path; preserve evidence and exact source/configuration; use the approved security incident process and authorized agency communication. Do not “repair” evidence by deleting it. Reopen only after the owner verifies the boundary and affected scope.
- **Known-unsent recoverable account/job failure:** preserve accurate unsent/held/backoff state, cap attempts and wait, and give healthy lanes a turn. Do not silently skip official work or count a failed pass as successful.
- **Possible external effect:** retain Sending/OutcomeUnknown or the provider's operation identity; quarantine and reconcile exact evidence. Timeout, cancellation, a process crash and a missing local receipt do not prove nonreceipt.
- **Shared dependency failure:** activate its correctly scoped protection, retain durable intent and show the honest reduced/unavailable state. A healthy-tenant progress promise is conditional on its required shared dependencies being available; no scheduler promises successful work during a total shared outage.
- **Overload:** reject or defer within a declared bounded budget, preserving interactive capacity where designed. Do not replace a queue bound with unlimited memory, threads, SQL connections or nested retries.
- **Restore or rollback:** suspend affected writers/relays until later valid records, external effects, keys/cursors and access versions are reconciled. Restoring a backup is not permission to resend forgotten work.
- **Evidence/permission/key failure:** fail closed for protected releases and official changes requiring that evidence. Keep independent content-free monitoring; a failing incident sink must not be the only detector.

Every response needs a named owner, threshold, affected scope, permitted action, safe staff message, reconciliation evidence and reopen conditions. The table specifies those invariants; implementation/runbooks must supply numeric limits and owner-approved procedures. It does not authorize a live call, deployment, cloud/security change, PHI query/copy, working-database migration, poison replay or external resend.

## Boundary and capacity scenarios

| # | Failure and criteria | Required control and response | Proof to retain |
|---|---|---|---|
| 01 | Caller changes target agency/user/resource; MT01, MT02 | Derive authority from current stored identity; compare all owner/child scopes before release/change; refuse safely. | Real registered route refuses foreign IDs, leaks no existence details and commits no success effects. |
| 02 | Permissions, security version or membership change mid-session; MT01, MT14, OP12 | Revalidate the current account and long-lived connection/replay authority; discard old client state. | Disable/reset/remove membership between selection and action; stale HTTP/socket/replay response cannot release protected data. |
| 03 | Person, owner and child rows have inconsistent tenant IDs; MT02, MT03 | Enforce the chosen relationship/write boundary and deny inconsistent records; do not trust a valid parent ID alone. | Synthetic inconsistent relationships and raw writes under the intended database principal are rejected or safely inaccessible. |
| 04 | A new join, bulk endpoint or query projection misses agency scope; MT02 | Centralize named scope owners where appropriate and maintain the complete route/release matrix. | Positive authorized result plus foreign-record refusal across nested, bulk, history and derived queue paths. |
| 05 | RLS/session tenant context survives pooled connection reuse, if adopted; MT03, OP02 | Set/reset trusted context per checkout; fail closed on missing/invalid context; prevent privileged bypass. | Alternating A/B requests and background contexts over reused connections, with intended grants and provider behavior. |
| 06 | Cache, result memoization or search returns A's data to B; MT04 | Bind trusted tenant, permissions and representation/version; revoke/invalidate correctly; bound cache/index state. | Same resource/key across tenants, changed access and poisoned/incomplete index tests; record absent search/cache features. |
| 07 | Blob/download URL, temporary file or export escapes scope; MT04, OP02, OP09 | Scope object access and retained URLs; bound expiry/copies; require current authority for sensitive release. | Foreign URL replay, expired/revoked access, cleanup and actual storage-principal probes on synthetic objects. |
| 08 | Account switch, hidden view or late UI response displays another agency; MT04, MT12 | Cancel and invalidate prior loads/session state; use current selection identity before updating shared screens. | Deterministic late-response, switch/reconnect/privacy-screen and dirty-editor tests; no unsafe local content cache. |
| 09 | Clearinghouse account/credential/namespace changes or is selected from B; MT05 | Recheck agency/account/file/profile/secret binding at execution; preserve retained bytes and truthful unsent cancellation. | Wrong account, foreign credential, rotated profile and namespace-collision refusal with no provider call/cursor effect. |
| 10 | Large A backlog or multiple A accounts monopolize turns; MT06, MT11 | Fair bounded agency/account/feed turns, total pass budget and restart continuation; preserve order only when required. | Sustained A plus small B, multiple hosts/accounts and restart; B meets a declared turn/time bound. |
| 11 | A's missing key or oldest poisoned row is selected forever; MT07, ID10 | Bounded unsent backoff/hold and recoverable lane isolation; retain failure evidence and continue healthy lanes. | Repeated preflight failure while B completes; no busy loop and no false OutcomeUnknown before Sending. |
| 12 | One agency's note sweep or package work fails; MT06, MT07 | Catch recoverable agency faults, preserve successful partial progress and leave failed agency due. | First-agency fault still permits later agencies; only committed changes produce success audits; existing package scan continuation survives. |
| 13 | Headers/body stalls, parser hangs or a huge page consumes memory; MT07, MT08, ID10 | Whole-operation/component deadlines, byte/page/query bounds and cancellation; release waits/permits safely. | Stalled body after headers, slow/malformed/oversized data and bounded memory/time; healthy B still meets the conditional bound. |
| 14 | Valid users repeatedly run expensive reports, imports or exports; MT08, MT11 | Trusted agency/actor admission, cost/byte/page/concurrency limits and bounded overload responses. | Baseline/contended B tail latency, query/connection waits, memory, rejections and bounded waiting queue under repeatable load. |
| 15 | Per-agency workers multiply a global vendor or namespace quota; MT09 | Preserve the real quota scope and aggregate limiter across hosts; allocate permits fairly. | Dated provider quota evidence, concurrent-host request counts and throttling/recovery test without accidental rate multiplication. |
| 16 | Pool exhaustion, SQL blocking or tenant bookkeeping overwhelms all agencies; MT06, MT08, MT10, MT11 | Bound connections, discovery/state/queues and lock duration; profile shared authorization cost; tune supported capacity. | SQL provider load, wait/lease measurements, inactive/new lanes, saturation and cancellation; no unbounded list or local-only limit claim. |
| 17 | Two hosts claim work, a stale owner resumes or reset starts; MT10, ID05 | Durable coordination, fencing/conditional ownership and documented lock order; retain reset/reconciliation exclusion. | Separate SQL hosts, barriers through lease expiry/lock loss/reset, stale-owner refusal and no duplicate effects. |
| 18 | Wrong agency setting/date/DST or stale projection changes official meaning; MT12 | Shared business-rule owner, supported tenant date policy and scoped settings; refresh/refuse stale state appropriately. | A/B different settings, date/DST boundaries, change during work and atomic authoritative decisions. |
| 19 | Partial provisioning, duplicate tenant creation, transfer or offboarding; MT13, OP12 | Durable lifecycle steps, immutable tenant identity, approved transfers and staged revoke/export/hold decisions. | Retry/crash at each step, no partial foreign grants/defaults, exact transfer evidence and tenant-specific recovery. |
| 20 | Support/admin authority expands or diagnostics expose unrelated agencies; MT14, OP06 | Separate support identity, narrowly scoped permissions and reviewed action evidence; agency admins remain scoped. | Privilege escalation, cross-tenant incident/export and emergency-access tests plus actual access review records. |

## Replay, transaction and external-effect scenarios

| # | Failure and criteria | Required control and response | Proof to retain |
|---|---|---|---|
| 21 | Successful create/save reply is lost and client retries; ID01, ID02, ID14 | Retain the scoped request key/fingerprint/result before reply; replay the original identity. | Lost-successful-response and restart tests; one durable business effect and stable authorized response. Cover ordinary note creation. |
| 22 | Same key is reused with changed payload or by another agency; ID01, MT01 | Versioned semantic fingerprint and trusted operation scope; conflict on changed meaning; no cross-tenant result disclosure. | Same/different tenant, actor, amounts, account, document and revision cases; invalid keys are bounded and safe. |
| 23 | Two duplicate requests race across hosts; ID02, ID03 | Atomic unique claim and compare-and-write; no process-memory-only or check-then-insert deduplication. | Independent-host simultaneous requests, commit conflict/unknown and restart; one effect and truthful in-progress/result state. |
| 24 | Concurrent approval/edit/form update leaves partial or overwritten state; ID03, ID04 | Correct aggregate transaction, expected revision and scoped schedule/period coordination. | Paused competing writes and refusal after intermediate save; no partial form/claim/audit commit and typed stale conflict. |
| 25 | Business transition commits without intent/audit, or event precedes commit; ID04, OP06 | Commit required business/evidence/outbound intent together; relay later; use accountable compensation for multi-store work. | Failure before/after each commit/publication boundary; required rollback or durable recoverable intent. |
| 26 | Lease expires and old worker resumes; ID05, MT10 | Fenced/conditional ownership, retained provider operation and safe takeover; cancellation is not remote rollback. | Pause A, expire/replace with B, resume A; stale commit and duplicate send refused. |
| 27 | Vendor received upload but reply was lost; ID06 | Committed Sending and exact file/business identity; quarantine rather than blind retry. | Accepted remote stand-in plus dropped reply/timeout/crash, then restart; no automatic send and exact reconciliation evidence. |
| 28 | Upload succeeds but local evidence commit fails; ID06, ID13 | Preserve ambiguous send state and original receipt identity; operator reconciles before any separate resend approval. | Failure after provider acceptance but before encrypted receipt/event commit; restart remains held and cannot claim nonreceipt. |
| 29 | Fresh generation key repeats an already accepted original; ID07 | Central original/correction lifecycle independent of transport keys; hold unsupported repeat. | Accepted original then fresh-key generation/queue/send, including parallel races; current R1 must fail before repair. |
| 30 | Attestation or material eligibility changes after generation; ID08 | Current applicable release gate and material-change hold inside the coordinated decision; retain frozen historical bytes. | Revoke/change/race between generate/queue/send; current R2 reproduces; no stale eligible upload and truthful held evidence. |
| 31 | Duplicate/reordered/altered receipt or provider callback; ID09, MT05 | Authenticate callbacks if introduced, tenant/account/feed inbox uniqueness and current identity; do not trust remote IDs alone. | Re-envelope, reordered and forged/foreign data; one effect or safe refusal and no wrong-account evidence. |
| 32 | Cursor advances despite failed effect or ERA page omitted; ID09 | Commit complete supported effects and independent checkpoint together; bounded complete pagination before advancement. | Rollback/concurrency/unmatched page, repeated pages and limits; no lost payment/status or advanced cursor on refusal. |
| 33 | Retry storm, nested retry amplification or provider throttle; ID10, MT09 | One retry owner, whole-attempt deadline, capped backoff/jitter and aggregate budgets; bounded shared recovery. | Count physical requests across SDK/client/jobs/hosts; simulate throttle/outage/half-open and verify cap/fairness. |
| 34 | Validation, stale conflict or forbidden action is retried as transient; ID03, ID10, ID14 | Terminal-safe error contract and deliberate refetch/review; preserve original key rather than inventing a fresh action. | Client protocol tests show no automatic overwrite, fresh-key duplicate or unsafe role-change retry. |
| 35 | Signature invitation/copy uncertain, stale, replaced or already sent; ID11, MT04 | Generation/provider-operation identity, durable lease, stale signer/source guard and bounded reconciliation. | Two workers, recipient/source change, provider response loss and exhausted attempts; no unsafe duplicate or wrong document/recipient. |
| 36 | Dedup entry expires or a late offline client reconnects; ID12 | Retain keys through supported horizon; explicit expired-key/business-state policy; never expire unresolved send knowledge. | Just-before/after lifetime, delayed redelivery and replay after cleanup; no silent new original effect. |
| 37 | Contract upgrade changes request normalization/fingerprint; ID01, ID12, OP08 | Versioned canonicalization and supported old/new interpretation; reject incompatible unsafe retries. | Cross-version golden requests/results and changed-payload conflicts; old operation remains recoverable with its original meaning. |
| 38 | Database restore or binary rollback forgets external outcomes; ID13, OP04, OP08 | Freeze affected writers/relays; reconcile later records, provider receipts, replay keys, outbox/inbox and cursors before resuming. | Approved synthetic full-service drill spanning acceptance and restored history; prevented duplicate and permitted resumed workflow. |

## Operating and release scenarios

| # | Failure and criteria | Required control and response | Proof to retain |
|---|---|---|---|
| 39 | Demo/local PHI is treated as cloud Production or endpoint is mispointed; OP01 | Separate deployment/environment identity, credentials, logs, storage, backups and administration; fail wrong-target checks. | Synthetic wrong database/API/key/instance tests and approved live environment inventory; no working-PHI inspection needed. |
| 40 | Broad principal, key/blob outage, secret rotation or network change; OP02 | Least privilege, environment-bound keys and scoped failure; protected releases fail closed without logging secrets. | Intended/actual grants, synthetic permitted/denied probes, rotation/revocation/expiry and unavailable-key recovery evidence. |
| 41 | Worker/watchdog stops or alerts never reach a person; OP05, OP07 | Independent missing-outcome/progress detection, poison/backlog thresholds, owner/escalation and tested delivery. | Safely induced missing/failed/reset/feed/starvation events and receipt/acknowledgment; detection delay on sleeping Demo is explicit. |
| 42 | Audit write fails, tampering occurs or exception text leaks secrets; OP06, OP03 | Required evidence gates release, immutable official action evidence and redacted bounded diagnostics under actual permissions. | Fail audit commit, raw privileged write attempts and synthetic narrative/secret sentinel through every enabled sink; independent review. |
| 43 | SQL, vendor, storage, key or network suffers shared outage; OP07, MT07, MT09 | Correctly scoped circuit/degraded mode, honest staff state and safe durable backlog; approved response and fair recovery. | Fault drill plus recovery drain, no claim that B can succeed through its unavailable shared dependency, owner closure evidence. |
| 44 | Backup exists but files/keys/later writes do not recover; OP04, ID13 | Agreed RPO/RTO, complete dependency restore/cutover and protected later-valid-record/external reconciliation. | Measured approved full-service drill with usable synthetic workflows; scratch table counts alone are insufficient. |
| 45 | Tenant closure, purge or export bypasses legal hold/preservation; OP09, MT13 | Approved retention/discovery/hold policy covers secondary stores and backups; no automatic delete without prerequisites. | Synthetic hold/export/offboarding/restore cases and accountable approved policy/operating record. |
| 46 | Hosting sleeps, saturates, scales out or deployment/settings drift; OP10, OP11, MT09 | Evidence-based target capacity, controlled defaults/configuration, cross-host limits and tested repair/rollback. | Representative approved sustained/burst/fault measurements, drift response, exact package/source and interrupted update acceptance. |
| 47 | Identity recovery, real provider use or agency workflow lacks approval; OP12, OP13 | Role-appropriate identity controls, offboarding, provider/account certification, operating responsibilities and accessible agency acceptance. | Independent role/recovery/vendor checks, actual permitted synthetic acceptance and agency sign-off; synthetic success is not real-use approval. |
| 48 | Readiness ledger is stale, incomplete, malformed or criteria/history rewritten; OP14 | Strict current-version/source/evidence coverage, immutable rubrics/snapshots and plain unavailable/not-ready state. | Malformed/duplicate/missing criteria, wrong version, revised rubric, no prior snapshot and immutable-history rejection tests; human-readable Settings review. |

## Acceptance design and evidence

For each implemented concern, record the exact trusted owner, invariant, failure boundary, allowed recovery and accountable reviewer. Build a matrix with authorized positive cases, foreign-tenant negative cases, single-host and independent-host concurrency, before/after-commit interruption, cancellation/restart, stale owner/permissions, key/schema/provider changes and restore/history loss.

Use synthetic A/B agencies, independent accounts, deterministic time and barriers. Real SQL provider tests are required for locks, uniqueness, transaction/connection behavior and cross-host limits; SQLite or a class named “concurrency” does not prove those guarantees. Run the repository's guarded private-instance tooling, never the shared working LocalDB/PHI store. Retain exact source, commands, result counts/skips and failing-before/fixed-after evidence for security, concurrency, replay and fairness repairs.

A synthetic connector proves Sati's handling of those simulated facts. It does not certify provider duplicates, actual receipt retention, real delivery, routing, identity grants or intended hosting capacity. Those need their separately authorized live and independent evidence. Inspect account-specific quota and certification requirements before narrowing shared locks or multiplying workers.

Define “healthy” and eligible before testing. For a finite set of N lanes, specify the maximum waiting turns, per-turn deadline, shared dependency assumptions and resulting time bound. Include multiple A accounts, quiet/new lanes, repeated host restarts and recovery drain. A test that eventually finishes without such a bound does not prove fairness.

Observe useful outcomes: oldest eligible work, queue depth, last successful agency/feed progress, scheduling delay, dependency duration, permit/lease wait, failure class, safe backoff/quarantine, resource pressure and rejection. Use bounded labels and authorized operational stores for necessary detailed IDs; do not log names, narrative, tokens, secrets, raw file contents or vendor replies. A heartbeat is not successful business progress.

## Ownership and launch boundaries

Worker selection and resource controls primarily belong to W8. End-to-end tenant integrity and lifecycle belong to the platform/persistence/security work; general create replay and transaction coverage belong to feature/API owners; billing R1/R2 belong to billing rules; complete recovery, deployment/identity, retention, vendor and human agency acceptance have separate owners and approvals.

All criteria marked blocking in the ledger remain launch checks until verified, even if a source feature is complete. An intentionally disabled integration may stay unavailable while its intended-use acceptance is reviewed; do not silently score its real-use gate as met. Changes to supported scope require explicit rubric decisions.

Keep one active backlog and link these stable criterion IDs instead of duplicating current checkboxes across dated reviews. Dated reports remain evidence, current runbooks remain operating instructions, and archived proposals remain history. None of those is silently promoted to live acceptance by consolidation or by this catalog.
