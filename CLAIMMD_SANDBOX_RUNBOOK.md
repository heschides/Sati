# Claim.MD sandbox preparation and operations

*October 3, 2026. Source only; this checklist does not activate transport.*

No vendor account was contacted, credential installed, cloud procedure applied,
Function/API published, or reset paused by this implementation. The transport
remains default-off and Demo-only. Verify deployed capabilities before using the
new requests below. This slice needs no new EF migration.

## Prepare a bounded testing window

1. Obtain a **dedicated test account** and protected confirmation of its account
   number/test designation. Claim.MD uses the same API host for test and production;
   the credential selects the account. A test account cannot become production.
2. Use approved synthetic patient **and provider** fixtures. The July 2026
   [test-account disclaimer](https://docs.claim.md/docs/test-account-disclaimer-read-this-before-using-a-test-account)
   prohibits real patient/provider, NPI, TIN, payment and enrollment information.
   Preserve supplied sample values; obtain suitable fixtures if needed. Do not
   follow enrollment links or submit test enrollments: external links may be live.
3. Confirm the duplicate field is **only remote_claimid**, carried in 837P REF*D9.
   Confirm account number, ISA/GS values, 837P/5010 behavior, manual transmission
   approval, corrected-claim behavior and ERA format. Retained PCN, D9 and service
   line references must remain stable and distinct.
4. Inspect the current Demo schema read-only. Migration
   20260926183942_AddClearinghouseDispatchFoundation was recorded applied before
   release 1.3.30. Old Phase 5 notes saying it is unapplied are historical.
   Do not reapply it from this checklist. Any genuine schema gap follows the
   normal controlled migration procedure.
5. Through separately approved activation, install the guarded reset procedures
   from Initialize-DemoFullReset.ps1 and publish the reviewed Function/API.
   Verify contract parity. The Function calls the owner-executed
   dbo.SatiAssertCanonicalResetAllowed before restore; a missing procedure fails
   closed. Publishing only the Function is insufficient. The reset identity
   remains denied direct access to demo_baseline.
6. **Pause automatic resets before onboarding**, as Josh requested. Apply and
   verify the Function setting **AzureWebJobs.RefreshCaseload.Disabled=true**.
   Review already queued/in-flight reset requests without replaying them.
   Retain the guard against unexpected manual requests. The watchdog suppresses
   only a missing nightly outcome while this timer is disabled; failures, poison
   entries and enabled billing checks remain findings. Record the window's owner,
   purpose and expected end date. See the official
   [Azure Functions pause/resume procedure](https://learn.microsoft.com/en-us/azure/azure-functions/disable-function).
7. Keep Sati:EnableClaimMdSandboxTransport=false until the remaining checks pass.
   Put the AccountKey only in approved API-host secret storage through the
   separately approved configuration process. The database reference is an
   environment-variable name beginning CLAIMMD_SANDBOX_KEY_, never the key.
   Verify encrypted evidence storage and recovery independently of the vendor key.

## Audited account and feed setup

An agency Admin calls POST /api/v1/admin/clearinghouse/claimmd-test-accounts on the
deployed Demo API. Agency/actor derive from validated authentication; the transport
may remain off. Account, separate Status/ERA checkpoints and audit commit together.
Other agencies' account numbers, namespaces and secret references cannot be reused.
The route neither resolves a key nor contacts the vendor.

Review **both** feeds before choosing their cursors. ReviewKind=1 (VerifiedEmpty)
requires cursor 0; ReviewKind=2 (ExistingReconciled) requires a reviewed positive
high-water ID. Status ResponseID and ERAID are independent. Protect the actual
evidence outside logs and send separate opaque review IDs. Never choose a cursor
merely to make polling start or bypass records needing review.

Illustrative request; replace all routing identifiers and choose a fresh permanent
account GUID and environment/account namespace:

    {
      "accountId": "7dad5041-5c96-4c6d-b7b9-58e00909ec25",
      "expectedRevision": 0,
      "externalAccountNumber": "TESTACCOUNT",
      "claimNamespace": "DEMOA001",
      "secretReference": "CLAIMMD_SANDBOX_KEY_DEMOA001",
      "dedicatedTestAccountConfirmed": true,
      "testAccountEvidenceReference": "REVIEW-TEST-ACCOUNT-001",
      "remoteClaimIdOnlyDuplicateFieldConfirmed": true,
      "duplicateFieldEvidenceReference": "REVIEW-DUPLICATE-D9-001",
      "statusFeed": { "cursor": "0", "reviewKind": 1,
        "evidenceReference": "REVIEW-STATUS-EMPTY-001" },
      "eraFeed": { "cursor": "0", "reviewKind": 1,
        "evidenceReference": "REVIEW-ERA-EMPTY-001" }
    }

Exact replay succeeds only while original account revision, checkpoints and audit
fingerprint still match. The response omits the secret reference. The onboarding
audit itself protects reset history, including if an account is disabled or removed.

## Enable and exercise transport

Enable the real sandbox flag only in the separately approved testing window, with
the synthetic transport flag disabled. Prove account routing, encrypted evidence
readback, multiple-host SQL coordination, rate limiting, status/ERA cursor atomicity,
rejections/corrected claims and bank reconciliation with approved fixtures.
Include Claim.MD's REJECT/DENY scenarios. Use status/ERA polling: Claim.MD does not
provide an ERA webhook. Its API response workflow is XML/JSON; do not invent 999
acknowledgments or claim API 277 support from its SFTP option.

Upload and polling workers hold the shared SatiDemo.FullReset lease through vendor
calls and evidence commits. Uploads hold a per-dispatch exclusive lease as well,
preventing a conflicting manual finding while an upload is running. The reset
takes the exclusive reset lease and refuses external linkage, attempts, receipts,
reviewed checkpoints or onboarding history in either live data or baseline.
This guard complements the paused timer.

## HTTP exchange deadline — local source, October 8, 2026

`ClaimMdSandboxConnector.HttpExchangeTimeout` owns one 45-second budget from the start of the
coordinated HTTP callback. The connector's client configuration removes the separate client
timer; one `TimeProvider` deadline linked to caller cancellation covers request/headers, stream
acquisition and all response-body reads. Request, response and stream disposal unwind on failure.
SQL admission, pacing and lock cleanup keep their existing budgets. The byte cap, fixed host,
redirect/key validation, coordination and default-off activation gates are retained.

This cooperative I/O budget does not supply a hard bound for parsing/decoding, cleanup, an
account turn or a worker pass. A timed-out upload after committed `Sending` is recorded as
`OutcomeUnknown` by the existing worker. It is never automatically resent, and a lost reply
does not prove remote nonreceipt. Follow the reconciliation procedure below.

[DEC-0223](docs/decisions/current/2026-10-08-DEC-0223.md) owns the choice and rejected alternatives;
[W8](BACKGROUND_WORKERS_HANDOFF.md) and [working evidence](docs/readiness/work-evidence.md)
record local fake-time/HTTP and separate SQL proof. These tests do not activate transport or
establish live vendor behavior, deployed configuration, total budgets or agency fairness.

## Current export and replay compliance — local source, October 8, 2026

`BillingExportGate` in `Sati.Contracts.V1` retains the API/local callers' authoritative
remaining service-date compliance errors on every evaluation, including a complete matching
stored supervisory exception. Those callers already apply the exact selected obligation IDs
and immutable Admin recovery evidence. The gate separately checks frozen agency/person/date,
current approval, supported configuration and stored exception provenance. It cannot grant an
exception or use today's requirements to replace that service-date decision.

API original generation and exact replay use this gate through `LoadExportablePeriodAsync`;
the transitional local `EdiService.GenerateAndSaveAsync` uses the same rule. A separately required
obligation revoked after a retained generation blocks both a fresh key and exact replay even
when a PCP exception remains valid. The API returns `billing_export_blocked`/409 and the local
service refuses the export; the failure preserves frozen claim JSON, retained content/name and
generation/audit/submission-event counts. The note's form-work deadline remains a separate,
non-waivable check. Legitimate exact exceptions and Admin recovery remain releasable when no
authoritative blocker remains. [DEC-0226](docs/decisions/current/2026-10-08-DEC-0226.md) and
[working evidence](docs/readiness/work-evidence.md) own the decision, actual synthetic checks
and their limits.

This repair does not close assessment R1 or full R2. Original/correction lifecycle needs
physical receipt/uncertainty facts in addition to generation keys; a Generated event alone does
not prove a physical send. Complete current compliance is not yet rechecked for the exact
retained original/correction subset at queue and immediately before committed Sending.
Necessary voids must preserve payer-held claim facts and need an explicit purpose policy rather
than inheriting a new-billability check. These follow-ups remain in
[billing architecture](docs/architecture/billing.md) and [the agenda](AGENDA.md). No provider,
payer, SQL concurrency, deployed or regulatory acceptance follows from this local repair.

## Retained request replay — local source October 9, 2026

**SATI-BIL-001 / R1-05 request-kind slice: implemented and verified in local source.**
`Sati.Contracts.V1.EdiReplayRules` owns equality of period, test mode and Original/Correction
request kind. API original initial/ordinary/duplicate-write recovery, API correction replay
and both local `EdiService` original replay paths apply that rule. Keys remain agency/actor
scoped. API routing-profile checks and current original export source/compliance checks remain
required; correction replay retains its separately scoped purpose. Queue replay's exact-account
identity is unchanged. Frequency 1 can be either original or linked Resubmit and cannot choose
the request kind.

Wrong-kind requests return API `idempotency_key_reused` or a local retry-key exception before
file output. Exact authorized original/correction replay returns the retained file and creates
no generation, correction-link, submission event or audit. The
[decision](docs/decisions/current/2026-10-09-DEC-0231.md) records ownership and alternatives;
[working evidence](docs/readiness/work-evidence.md#2026-10-09--retained-edi-request-kind)
records actual fail-first/check results and evidence limits.

The API reproducer runs a fresh submitted original through mock response ingestion and a linked
frequency-1 Resubmit. The desktop reproducer retains a valid correction graph/content using the
shared formatter. Both ordinary and post-rollback wrong-kind paths failed against their previous
behavior: API returned OK, local service wrote the correction file. Recovery fixture interception
hides one lookup and, in API, supplies the exact retry-key storage conflict; it exercises recovery
but supplies no SQL race proof. Legitimate exact replay, current compliance and session/tenant
restrictions remain separately tested. Existing synthetic files use unique fixture keys and
narrow cleanup.

Physical-history projection, the queued reservation/uncertain-send guard, common SQL admission,
encryption staging and exact retained-subset queue/send compliance are still the remaining
implementation. No migration, vendor call, deployed activation, payer certification or sealed
readiness change follows from this slice.

## Sequential delivery-history guard — local source October 9, 2026

**SATI-BIL-001 / R1: shared rule/projection and sequential main-path admission implemented;
all-writer coordination and full R2 remain open.** `OriginalClaimReleaseRules` in Contracts
decides original generation, queue and immediately-before-Sending permission. The Persistence
`OriginalClaimReleaseHistory` validates immutable files and projects delivery facts; its common
loader reads accounts/dispatch/attempt/correction/receipt metadata. API/local adapters scope the
distinct source models to the trusted agency. No new local transport/correction/intake writer
or network DTO exposing EF entities is introduced.

The adopted matrix below is applied across modes/accounts/namespaces at API/local fresh original
generation, API new queue and worker admission. Generated-only remains eligible; exact result/
intent replay preserves existing checks. A queued reservation, Sending/unknown or generic upload
rejection prevents another original. Received files, including all-R accepted uploads and rejected
acknowledgements, require eligible explicit correction lineage. A late exact receipt defeats a
previous audited nonreceipt. A refused queued worker candidate is CancelledBeforeSend with safe
`original_claim_release_held` and no attempt/upload. API refusals use that conflict code; local
generation throws a safe retained-history exception without writing another file.

Validation includes row/owner/note/person agency, retained mode/ISA/GS controls, exact CLM01,
single canonical REF6R, unambiguous historical Claim.MD envelope/D9/account and correction links,
frequency/F8/amount/predecessor. The parser exposes F8 and refuses duplicate F8 values. Historical
accounts are read regardless of today's enabled flag. Exact receipt matches retain group/claim
and connector-source provenance. Nonreceipt requires the existing exact reconciliation event and
audit manifest, including dispatch/account/source/claim digest/revision/prior state, and cannot
erase later receipt or transport uncertainty. Unscoped delivery events hold the known period;
unknown scope holds the agency. Malformed unused Generated-only history is reported but proves
no delivery; an invalid candidate is refused. No history row is silently skipped as clean.

The projection caps each query at 10,000 rows, total retained content at 16 MiB characters and
receipt matches before payload projection. A reached cap returns incomplete/held. These are
local decision bounds, not full worker wall-time/fairness guarantees. Account secret references,
encrypted response bodies and unrestricted remittance explanations are outside projection reads.
Legacy local files with null row ControlNumber derive the validated retained envelope. Their
unlinked Generated events and synthetic flags never establish transmission.

[DEC-0232](docs/decisions/current/2026-10-09-DEC-0232.md) records the choice; [working evidence](docs/readiness/work-evidence.md#2026-10-09--original-delivery-history)
owns actual fail-first/results. All-writer common SQL admission, worker authoritative source
refresh, mock admission, encryption staging, poller execution scope and failed-transaction cleanup
remain the next chunk. Exact correction permission/source-purpose and full current retained-subset
queue/send compliance follow. Sequential SQLite evidence is not SQL race proof, vendor/payer
certification, deployment or full R1/R2 closure.

## Coordinated release and current subset compliance — local source October 9, 2026

**Status:** implemented source; actual local/synthetic verification and limits are recorded in
[working evidence](docs/readiness/work-evidence.md#2026-10-09--coordinated-claim-release-and-compliance).
[DEC-0233](docs/decisions/current/2026-10-09-DEC-0233.md) supersedes the pending transaction/full
current-release status in the earlier sequential checkpoint and proposal below. Historical test
results remain historical; no source result changes sealed 1.3.38 readiness or deployment facts.

`ClaimReleaseWriteScope` in Persistence acquires `Sati:ClaimRelease:{agencyId}` as the first
transaction-owned exclusive SQL lock, with a 10-second admission wait. BillingPeriod and
ServiceTime owners acquire it before their own narrower locks. Generation/correction, new queue,
worker pre-Sending/result, reconciliation, mock transmission and manual/status/ERA receipt writes
share that boundary. Relevant current-compliance writers (notes/amendments, form/release
attestations, period promotion, policy/recovery, person/provider links and annual reconciliation)
also enter admission. Current tracked source is reloaded after admission; advisory discovery is
never a decision. Serializable reads protect the queried rows/ranges. This deliberately broad
agency lock has no measured throughput/fair-wait guarantee; W8 remains separately open.

Direct local/receipt operations own single-attempt execution. Receipt and worker entry points
refuse a retrying outer scope before I/O. Endpoint writes retain the existing zero-retry filter;
admission contention returns safe `claim_release_busy`, including GET lazy reconciliation without
placing ordinary reads in the write execution scope. SqlClient command cancellation is normalized
to caller cancellation only when the caller token is cancelled; no raw SQL inner payload is
attached, and other provider failures propagate. Duplicate-write recovery rolls back and
disposes the failed scope, clears tracking and revalidates in a fresh admitted transaction.

Receipt processing first checks replay/no-op, authority/account/cursor and exact matching. It
reserves a receipt identity, releases SQL, wraps with that identity's encryption binding, then
re-enters admission and repeats all checks before receipt/effect/audit/cursor commit. Duplicate
manual replay and stale feeds need no wrapping key. The worker resolves/preflights keys outside
SQL, reloads the retained file/account/intent, commits Sending and releases SQL before upload.
Actual upload evidence is wrapped outside SQL and committed under new admission. A receipt
committed before Sending holds the successor with zero calls. Once Sending wins, later receipts
cannot unsend it: retain both facts, never conceal or automatically replay the upload. A failed
result commit leaves durable Sending for reconciliation.

The API's retained release validator uses trusted stored agency scope and the validated immutable
claim mapping, without constructing a human actor. New queue and immediate pre-Sending checks
reuse current service-date `BillingExportGate`, exceptions/recovery, source status, form/release,
provider/contact, form-work, snapshot and service-time checks. An original checks all its retained
claims. Resubmit/Replace check only their selected persisted correction subset; an unrelated
claim's new blocker cannot block that correction. Current physical standing/action/payer number
and predecessor are checked again. Original/correction exact intent replay retains its durable
result; it does not create another send.

Void keeps its existing withdrawal purpose: loss of positive billability cannot prevent
withdrawing the standing bill. Exact retained identity, agency/period, eligible standing action,
F8 payer claim number/predecessor and amendment financial review remain mandatory. No correction
creates new vendor/payer permission. Correction history now uses the same validated projection:
Generated-only alternatives do not supersede the received claim; generated corrections remain
pending; damaged physical history and uncertainty hold. GET options are advisory, and commands
repeat the decision under admission.

The private SQL acceptance observes actual transaction lock ownership for all nine history writer
categories, and pauses real receipt/worker entry points in both orders. The intended negative
proof detects premature history reads, rather than assuming ordinary SQL range locks allowed an
extra upload. Additional key preparation tests observe SQL lock absence and mutate authority/feed
cursors while wrapping. Rollback checks inject failure before commit; they are distinct from an
ambiguous network/SQL commit loss. Existing lost-response/host-recreation tests cover committed
replay. Local adapters share projection/rules and admitted fresh original/replay behavior; the
transitional desktop does not gain a second dispatch/receipt scheduler.

Acceptance mapping: R1-01/02/03/04/09 use actual generation, queue, synthetic worker and audited
nonreceipt workflows; R1-05 retains exact result/intent/request-kind replay; R1-06 checks legitimate
frequency 1/7/8 and current correction standing; R1-07 checks retained identity/damaged evidence;
R1-08 checks late group and claim receipts plus both SQL receipt/Sending orders; R1-10 uses
pre-commit rollback and durable-Sending restart, with existing joined lost-response replay.
Current exact-subset/purpose checks cover R2 at queue and pre-Sending. The ledger owns exact runs
and distinguishes initial setup failures, mutation proofs, broad suites and unrun permutations.

No live clearinghouse/payer send, cloud/shared database access, migration, worker activation,
deployment, release, legal compliance review or readiness reassessment occurred. Multi-host load,
aggregate budgets, exhaustive failure/order permutations, full-service recovery and external
certification remain with their existing agenda/evidence owners.

## Proposed original-release guard — October 9, 2026

The independently implemented replay-kind repair is described in
[the retained request owner](#retained-request-replay--local-source-october-9-2026).
The [sequential delivery-history owner](#sequential-delivery-history-guard--local-source-october-9-2026)
supersedes proposed status for the shared owners and main admission points. The following source
inventory records the released baseline before that implementation; common SQL coordination and
remaining acceptance stay proposed until their actual proof is recorded.

**SATI-BIL-001 / assessment R1: adopted design baseline; sequential implementation linked above;
SQL proof pending.**
Source inspected at `de23bd175445b1caf5caee52ccc8870d021b88da` (released 1.3.38).
This section owns the proposed fact contract, admission rule and fail-first acceptance plan.
It changes no executable rule, route, schema, transport setting or readiness score. The
[working evidence](docs/readiness/work-evidence.md#2026-10-09--billing-original-release-design)
records the original inspection and documentation checks. Full R2 and payer-held void purpose remain separate.

### What the current source proves

| Path | Existing behavior and design seam |
|---|---|
| Original export | `ApiEndpoints.cs`'s `/billing/periods/{periodId}/edi` uses `BillingPeriodWriteScope`, `LoadExportablePeriodAsync` and generation-key replay. `Data/Billing/IdeService.cs`'s `EdiService.GenerateAndSaveAsync` uses a plain Serializable transaction and the shared `BillingExportGate`. Neither classifies prior physical sends before accepting a fresh key. The local legacy row can have null ControlNumber and its Generated event has no generation link. |
| Queue | `ClearinghouseDispatchEndpoints` uses `BillingPeriodWriteScope`, retained account/profile matching, exact-generation dispatch replay and a period-wide Queued/Sending/OutcomeUnknown check. An earlier accepted generation does not block a different generation of the same original. |
| Before upload | `ClearinghouseDispatchWorker.UploadUnderDispatchLeaseAsync` reloads the dispatch, rechecks account/generation and amendments, then commits Sending inside `ServiceTimeWriteScope` before calling the connector. It does not currently recheck prior physical history of the business claim. |
| Upload evidence | `ClaimMdSandboxConnector.UploadAsync` can record Accepted even when every returned claim status is R: the file was received and its claims rejected. Generic `ClearinghouseAttemptOutcome.Rejected` at the connector seam has no definitive-nonreceipt contract. Neither is a 999 verdict. |
| Manual reconciliation | `ClaimMdReconciliationEndpoints` retains the original attempt and an append-only resolution. ConfirmedNotReceived requires SupportCase evidence; ConfirmedReceived records exact file/PCN/D9 evidence, including A/R statuses, not payer acceptance. |
| Receipts | `ClaimResponseIngestion.ImportAsync` can match a retained generation without an accepted dispatch. It commits exact receipt matches/effects without changing that dispatch's state. A late matched receipt can therefore coexist with ConfirmedNotReceived. Automated Claim.MD status/ERA currently require an accepted dispatch; they are not an existing late-state-7 recovery adapter. |
| Mock | The `/mock-clearinghouse` endpoint records synthetic Transmitted evidence, then imports responses from `MockClearinghouse.Respond`. A mock outcome proves simulated handling only; it must exercise the same proposed rule within its synthetic history. |
| Correction history | `LoadClaimHistoryAsync` in `BillingCorrectionEndpoints` treats each parseable generation as a submission and skips malformed content/CLM references. It is a correction-display projection, not a safe original-release projection. `ClaimCorrectionRules` remains the sole correction-action owner. |

### One rule owner and trusted projection

Propose **`Sati.Contracts.V1.OriginalClaimReleaseRules`** as the sole pure rule owner. Its input
contains an operation (`GenerateOriginal`, `QueueOriginal`, `BeginSendingOriginal` or
`ReplayRetainedGeneration`), exact candidate identities, complete scoped history, mapping defects,
transport facts, resolution facts and matched receipt facts. Its bounded result distinguishes
allowing an original, returning an existing result/intent, holding for review and requiring explicit
correction lineage. No EF entity, raw EDI, credential, vendor reply or clinical narrative is a network
contract. The rule grants neither caller authority nor current compliance nor a correction action.

Propose **`OriginalClaimReleaseHistory` in `Sati.Persistence`** as the shared retained-content
projection/validation owner, with API and transitional local EF query adapters supplying the same
typed facts from their existing models. Keep database reads and authorization in those adapters;
do not duplicate the decision in either generator, queue, worker or ViewModel. A background worker
uses trusted stored agency/account scope, not an impersonated human authorization wrapper.

Projection requirements:

1. Obtain agency from freshly validated actor or stored system work. Join the generation's period
   to its owner and verify generation, period owner, claim line, note and person agency markers.
   A caller's period, account, generation or claim reference is only a lookup candidate.
2. Parse **retained immutable content**, not today's whole period render. Validate interchange mode,
   controls and period against the row. Parse all of CLM01, then recompute the exact
   `ClaimSubmissionIdentity.ClaimReference(control, period, note)`; reading only its final Note ID
   is insufficient. Require the retained REF6R service-line identity to agree with that note and
   the supported single-line subset. A legacy null row ControlNumber requires an unambiguous retained envelope,
   not a guessed new control. Reject duplicate/missing/foreign references and inconsistent subsets.
3. Map each reference to the unique persisted ClaimLine for its Note ID and period. The proposed
   business key is **(trusted AgencyId, persisted NoteId)**, retaining ClaimLineId and period as
   required provenance. The unique ClaimLine.NoteId relationship supports this existing single-line
   scope. New generation/request/control/account/namespace/mode values do not create another
   business claim. Scan retained history for that business key, not only a selected account or mode;
   conflicting period/line ownership is a hold. This does not detect two different Note IDs describing
   the same service; general underlying-service deduplication remains SATI-IDEM-001.
4. For Claim.MD, validate retained D9 against the historical account namespace/agency/period/note
   binding. Keep D9, changing PCN/CLM01, local business key and external file identity distinct.
   Local legacy Office Ally history has no invented Claim.MD D9/account requirement. An account
   change must not hide an earlier submission of the same business claim. Historical account
   lookup must not require today's enabled flag. A generation has no AccountId/profile-version
   column: use retained envelope/D9 plus unambiguous account identity and dispatch/attempt evidence
   where available. Missing historical provenance is a hold, not an invented profile binding.
5. A correction requires `IsCorrection`, the exact `ClaimCorrectionSubmission` link, agency/period/
   ClaimLine/Note identity, persisted action and matching retained frequency/standing-claim facts.
   **Frequency 1 also means Resubmit**; CLM05-3 alone cannot identify an original. An unlinked or
   inconsistent correction is held, never treated as a clean original. Delegate permitted actions
   to `ClaimCorrectionRules`; do not create a second payer-adjudication rule here. The current
   parsed submission DTO does not expose REF F8; verifying the retained standing-claim number
   requires an explicit shared parser-projection extension, not a claim that it is already loaded.
6. Join dispatches, attempts and resolutions to exact generation/account/agency identities; validate
   recorded hashes/file names when supplied. Join receipt matches by receipt agency, generation,
   period and exact claim reference; a group-level 999 match covers the mapped claims in that exact
   generation. Keep connector account/feed identity and manual evidence source distinct. A period
   event or unscoped remote identifier alone cannot certify an exact claim receipt. Use named
   event semantics, not `Stage >= Transmitted`: TransportFailed also satisfies that comparison.
7. Preserve defects as facts. Damaged/unmapped/ambiguous **physical or uncertain** retained history
   holds its safely identified period/business scope; if that scope cannot be established, the
   affected agency projection is incomplete and cannot authorize release. Do not silently skip it.
   An unused damaged Generated-only file proves no send; report it separately, and refuse if it is
   the candidate. Query/parser limits or missing evidence return incomplete/held, never empty history.

### Proposed fact and admission matrix

The following matrix uses **Sati product policy adopted in
[DEC-0230](docs/decisions/current/2026-10-09-DEC-0230.md)**, not payer findings. Main-path sequential
admission now enforces it; all-writer SQL proof and the remaining acceptance are pending.
Evaluate all mapped history, not the newest generation or dispatch state alone. Conflicting exact
receipt/uncertainty evidence takes precedence over a known-unsent finding. A file containing several
original claims releases only if every candidate claim is eligible; never silently omit a blocked
claim or rewrite retained bytes.

| Recorded fact for the same business claim | Fresh original generation / new queue intent | Candidate's immediately-before-Sending decision |
|---|---|---|
| Generated-only, no durable intent, transmission or receipt | Allow, subject to existing source/compliance checks. Multiple harmless renders do not prove a send. | Require its own valid Queued intent and rerun the complete history decision. |
| CancelledBeforeSend with no contradictory attempt/transmission/receipt | Allow a separately reviewed new generation. The cancelled dispatch is not revived. | A cancelled row cannot send; an eligible successor can. |
| Another Queued original | Hold a fresh original/new intent; preserve one existing reservation. Same-generation/account queue replay returns its existing intent. | Allow only the candidate's own reservation if no competing or stronger fact exists. |
| Sending or OutcomeUnknown, including Sending without an attempt | Hold; missing receipts, timeout and cancellation are not nonreceipt. | Hold a successor; never automatically repeat the uncertain dispatch. |
| Accepted upload or audited ConfirmedReceived | Refuse another raw original; require the separately permitted correction workflow if applicable. All-R upload results still prove file receipt. | Hold another retained original, even one generated before acceptance. |
| Exact matched 999/277CA/835 or connector claim receipt | Refuse another raw original, including a rejected acknowledgement. Receipt proves that generation reached a receiver, not payment or acceptance. | Hold a successor; a late exact receipt defeats an earlier nonreceipt finding. |
| Valid ConfirmedNotReceived, no later/conflicting receipt or unresolved competing intent | Allow a separate generation; retain the old uncertainty and resolution. Recheck the entire claim history at each later admission. | Allow its own eligible successor only while that conclusion remains current. |
| Generic upload Rejected / TransportFailed with no definitive nonreceipt evidence | Hold for review under recommendation P2. Do not infer nonreceipt or a 999 rejection. | Hold; no implicit retry or fresh-original permission. |
| Missing mapping, damaged physical evidence, inconsistent scope or contradictory facts | Hold with a bounded review code; preserve evidence. | Hold before Sending and make zero connector calls. |

**Exact generation replay** returns the authorized retained result/bytes and creates no generation,
intent or physical upload. Original export currently runs the export/replay compliance gate;
correction replay returns earlier and does not run that original gate. Propose explicit request/
generation-kind validation alongside the existing applicable request/profile checks: original
replay does not currently check IsCorrection. Do not claim correction replay already receives the
original compliance check or impose it on a necessary void. An already terminal same-generation
queue replay reports that state; it does not reactivate it. Replay with changed meaning remains a
conflict. This separates receipt knowledge from safe retrieval of an earlier result.

The API generation replay fingerprint currently compares retained routing/profile bytes, period
and mode, not a persisted exact AccountId. Preserve that supported scope and add request kind in
the proposed slice. Exact-account generation replay would require a separately scoped durable
binding; Office Ally routing bytes alone cannot establish it. Queue replay already has the exact
dispatch AccountId and must retain that stricter check.

**Explicit correction lineage** goes to the existing correction owner and its own uncertainty/
receipt review. An authorized frequency-1 Resubmit must not be blocked solely because it resembles
an original on the wire. This design grants no new correction permission and does not establish
that the current correction-history projection safely classifies every physical-send state.
Any required repair to that projection must be separately scoped and tested; do not reuse it
unchanged to enforce original release. Full exact-subset R2 and necessary payer-held void purpose
are separate work, rather than an accidental new-billability condition on a void.

### Policy choices needed before implementation

**Resolved October 9:** Josh explicitly chose “Adopt all three recommended rules.”
[DEC-0230](docs/decisions/current/2026-10-09-DEC-0230.md) records the authority, choices and rejected
alternatives. This heading remains a stable link to the original policy-review dependency.

| ID | Adopted rule and reason | Implementation / operating boundary |
|---|---|---|
| P1 — cross-mode history | In one database, include physical/uncertain facts for the same agency/business claim across IsTest and synthetic/mock history, accounts and namespaces. Preserve mode/source as provenance; do not make IsTest a guard bypass. Independent synthetic cases use independent lifecycle fixtures. | Adopted. A different certified sandbox lane would require a separately decided durable lane identity and evidence boundary. Never combine separate Demo and working-PHI databases or access the latter to implement this. |
| P2 — generic rejection | Treat generic upload Rejected/TransportFailed as insufficient proof of nonreceipt. Require exact supported receipt/correction evidence or an audited definitive nonreceipt finding. | Adopted. Current manual reconciliation admits Sending/OutcomeUnknown, not generic Rejected: do not silently extend that route or manufacture a SupportCase. Any recovery-route extension is a separate explicit slice. A 999 rejection remains a received acknowledgement handled by `ClaimCorrectionRules`. |
| P3 — queued reservation | Hold a fresh original generation as well as a competing queue while another original is Queued; preserve exact replay. This prevents a second downloadable original from escaping the reserved lifecycle. | Adopted. Generated-only history without an intent remains allowed. Test positive cases with legitimate separate histories instead of bypassing the reservation. |

No external requirements research or vendor calls were performed for this design. Existing
[payer requirements](PAYER_BILLING_REQUIREMENTS.md), [certification](PAYER_BILLING_CERTIFICATION.md)
and [regulatory posture](REGULATORY_CONCERNS.md) remain the authority for their separate reviews.
The accepted product policy does not certify external billing behavior or implement the guard.

### Admission order and concurrent facts

Fresh generation: validate actor/scope and request identity; distinguish exact replay; load exact
candidate/source and complete lifecycle facts inside the coordinated transaction; apply existing
compliance plus the shared original rule; only then persist immutable generation, audit and Generated
event atomically. Reauthorize/re-evaluate duplicate-write recovery in a new protected transaction.

Queue: validate stored source/account and caller authority; distinguish existing exact intent; load
the retained subset and complete history; decide permission before inserting intent/audit. Before
Sending: retain reset and per-dispatch leases, reload Queued/account/profile/source, run amendment
and required key/evidence preflight, then refresh lifecycle facts and decide immediately before
committing Sending/revision. A lifecycle refusal remains truthfully unsent with a safe hold reason;
state/UI semantics need a bounded implementation decision, not a fabricated transport failure.
Current exact-subset compliance at queue/send remains full R2 and must be tracked independently.

The existing application-lock names **do not coordinate with each other**:
`Sati:BillingPeriod:{agency}:{owner}:{year}:{month}` protects generation/queue, while
`Sati:ServiceTime:{agency}:{owner}` protects the worker's Sending decision. Receipt ingestion uses
plain Serializable transactions. Serializable is not by itself evidence that those actual query
ranges prevent every conflicting admission, especially when no receipt yet exists.

Propose a transaction-owned common **`Sati:ClaimRelease:{agencyId}`** SQL application lock for
generation/queue/Sending, correction creation/generation that consumes the same history, mock
transmission, reconciliation, upload-result commits and all receipt/status/ERA effect writers.
Acquire it before lifecycle reads or business table writes, then existing
schedule/period locks in one reviewed order. Preserve outer reset/dispatch session leases, acquire
all needed inner resources in a deterministic order and use no nested transaction. Existing scope
helpers need an acquire-in-owned-transaction seam; their current BeginAsync calls cannot simply
be nested. Pre-transaction scoped discovery is advisory and must be revalidated after admission.

The agency scope deliberately covers multi-period receipts and unmapped-history holds without
guessing a narrower partition. Its intended boundary is short local transactions; achieving the
no-network boundary requires the encryption staging described below. Contention,
wait/transaction limits and the complete lock-order graph require SQL proof and W8 review before
adoption. Do not introduce a cycle through reset/dispatch/feed leases or hold it across upload/polling.
Release it after committed Sending: a receipt already committed must defeat a successor; a receipt
that arrives after that decision cannot undo an external request. Preserve truthful uncertainty and
review that later conflict. This is a bounded database decision guarantee, never remote exactly-once.

### Transaction-boundary review — October 9, 2026

Read-only review at documentation checkpoint `08fc36e40a837d8e6e217e737fe78513623337c0`
revalidated the actual call paths. No common lock, helper refactor or execution-strategy repair has
been implemented/tested. The inventory below covers normal application writers; raw SQL,
restore/cutover and operator changes retain their separate containment/reconciliation procedures.

| Owner | Current transaction/read boundary | Required integration seam before full R1 adoption |
|---|---|---|
| API original export and duplicate-write recovery | `ApiEndpoints.cs`: each branch starts `BillingPeriodWriteScope`; actor/profile/previous/source reads follow. | Own one Serializable transaction, acquire ClaimRelease before the period lock and protected reads; preserve a fresh transaction and revalidation after rollback. |
| Local `EdiService.GenerateAndSaveAsync` and recovery | Plain Serializable; session/actor and retained-key reads follow. | Resolve only advisory stored identity before admission; revalidate session/agency/source and kind under common admission. Do not activate local correction, receipt or transport functionality. |
| Queue | `ClearinghouseDispatchEndpoints`: scoped period discovery precedes BillingPeriod scope; actor/generation/account/amendment/history reads follow. | Treat discovery as advisory. Common lock before period lock; reload full trusted scope, retain exact-generation/account replay and atomic intent/audit. |
| Worker known-unsent cancellation | `UploadUnderDispatchLeaseAsync`: account/profile mismatch can save CancelledBeforeSend before ServiceTime scope. | Include this easily missed write in the decision boundary; it cannot race a receipt and become false nonreceipt evidence. |
| Worker Sending | Reset/dispatch session leases; account/generation discovery; ServiceTime transaction; key/receipt preflight; Sending commit. | Move network preflight outside the SQL decision transaction. Common lock before ServiceTime, then reload dispatch/revision/account/generation/source/history and decide before Sending. |
| Worker outcome append | Same outer leases/single-attempt operation; response protection and attempt/event construction precede implicit SaveChanges transaction. | Prepare protected response outside common admission. In a new common-lock transaction, verify the exact still-Sending identity/revision and atomically commit attempt, state, event and required evidence. |
| Correction create/generation | `BillingCorrectionEndpoints`: BillingPeriod scope then actor, history, line/amendment and correction/link writes. | Common lock before BillingPeriod and all authoritative history reads; preserve `ClaimCorrectionRules` and exact links, including Resubmit. This review grants no additional correction action. |
| Mock transmission | `/mock-clearinghouse`: period/latest-generation/submission checks, implicit Transmitted/audit save, then three separately owned response imports. | Add one explicit common-lock transaction for transmission admission/event/audit. Commit/release before importing responses; do not wrap intake's owned transactions inside it. Keep partial-response failure truthful. |
| Manual/mock receipt intake | `ClaimResponseIngestion.ImportAsync`: parse/hashes outside, then Serializable duplicate/match/encryption/effect/audit commit. | Stage protection outside common admission while retaining no-key duplicate replay; reload exact matches and identities and commit receipt/effects atomically under the common boundary. |
| Connector status / ERA | `ClaimMdStatusProcessor.ProcessAsync` / `ImportConnectorEraAsync`: plain Serializable then account/cursor/accepted-dispatch/match/protection/effects/checkpoint. | Protected local commit phase only; revalidate agency/account/cursor/full page and duplicate identities under common admission. Preserve accepted-dispatch feed eligibility; no state-7 late-receipt route extension. |
| Manual reconciliation | Scoped dispatch discovery; dispatch session lease; BillingPeriod transaction; current actor/manifest/attempt/receipt checks and resolution/audit/event commit. | Common lock before period lock and refreshed manifest/history checks, preserving busy/foreign-ID refusal and exact evidence attestation. |

Recovery seams also require review: queue currently rereads after a DbUpdateException without
ending its failed transaction, and manual receipt recovery rolls back but leaves that transaction
object current before rereading. Full common-boundary integration must dispose the failed scope
and reacquire a fresh protected decision before using a recovered result. These are inspected
paths, not newly executed failure claims or part of the first replay-kind repair.

The local `BillingService` does **not** implement response import, correction creation/generation,
dispatch or deposit recording; `IBillingService` defaults refuse them and `CloudBillingService`
forwards supported operations to the API. Existing local model/history rows still need a read
adapter. There is no local receipt/correction writer to add or enable through this review.

Lease graph: Demo HTTP middleware or a worker owns the outer shared reset lease. Upload and
reconciliation then own the per-dispatch session lease. Polling owns the **global** Poller session
lease, uses the global ApiRequest lease for pacing/HTTP and releases ApiRequest before status/ERA
commit. There is no account/feed-specific named lease in this path. Onboarding owns a separate
global transaction lock. The proposed inner order is owned transaction → ClaimRelease → needed
ServiceTime/period locks → authoritative data reads/writes. All participating paths must use that
order, with stable ordering for multiple inner resources, or the common lock can introduce a cycle.
Preserve current claim-insertion locks; do not acquire ClaimRelease late after taking them. Account
onboarding/other non-history writers and their actual row-lock interactions need separate graph
acceptance, not an assertion that unrelated lock names coordinate.

**Execution ownership:** HTTP mutation endpoints run inside `SingleAttemptWriteFilter`, while the
dispatch operation uses `DispatchSingleAttempt` around its upload path. Preserve their zero-retry
behavior. The poller directly calls the status/ERA transaction owners; no equivalent execution
scope is visible despite `Program` enabling SQL retries. This is a source-predicted setup hazard,
not an executed failure. Full R1 integration must give these local commits an explicit supported
single-attempt owner (without retrying the external fetch) and test it. Do not count an EF setup
refusal as a concurrency-regression failure or replace the deadline/transaction owners wholesale.
A zero-retry wrapper must also refuse an already retrying ambient execution strategy before work,
following the separately proven incident-owner pattern; nesting a wrapper alone may not replace
that ambient owner. Prove this at the actual processor/admission seam before claiming closure.

**Key Vault staging:** `EnvelopeProtector.ProtectAsync` calls `IKeyWrapper.WrapAsync`; the configured
API wrapper calls Azure `WrapKeyAsync`. Current receipt protection and worker preflight occur
inside SQL transactions. Simply adding ClaimRelease there would hold the agency lock through
Key Vault network waits. Release every SQL decision lock before protection/preflight, including
the existing schedule/period locks, while retaining required outer reset/dispatch exclusion.

Use a short admitted no-op/replay inspection, release without writes if new protection is needed,
reserve immutable in-memory IDs/bindings, protect outside SQL, then reacquire admission and fully
revalidate before commit. A reviewed advisory fast path may optimize that sequence, but cannot
authorize a write or stale replay. Keep existing duplicate-receipt replay and stale-cursor no-op
behavior working without a successful key-service call. Discard prepared envelopes when their
binding/source/authority changes, and never persist plaintext or add an automatic retry.

Receipt AAD binds agency, reserved receipt GUID, raw hash and parser version; attempt AAD binds
agency, reserved attempt GUID and response hash. Fix those values before protection and verify
them again with account/dispatch/generation hashes, expected revision/cursor, exact matches,
duplicate identities and current authority after admission. Preflight establishes only observed
readiness: credentials/key service can change after Sending, so preserve OutcomeUnknown and
reconciliation on later evidence failure. Network/crypto budgets and session-lease loss remain
W8/idempotency/recovery concerns; this proposed staging establishes no live capacity bound.
Preserve caller cancellation for preparation/admission and the existing post-upload evidence
obligation using CancellationToken.None. The connector's 45-second HTTP exchange budget does
not bound Key Vault; changing those budgets or token semantics needs a separately verified slice.

**First implementation slice:** the inspected replay gap can be repaired before the complete
physical-history projection/lock integration. Original export and local `EdiService` compare
period/mode but can return a retained `IsCorrection` file under an original request key. The common
API replay helper also omits kind. Correction export already explicitly requires correction kind;
the helper must accept the expected kind, rather than hard-coding originals and breaking that path.

Select a shared **`Sati.Contracts.V1.EdiReplayRules`** request-identity owner for period, mode and
original/correction kind. Preserve API routing-profile matching, scoped key lookup and original
compliance revalidation. Apply it to every initial and duplicate-write recovery replay branch in
API and local services. Do not infer a persisted exact account fingerprint where none exists.
This named request-identity owner is separate from the future original-release lifecycle owner.

Proposed acceptance for that bounded slice: build an isolated legitimate original → rejection →
linked frequency-1 correction through real API workflows; reuse its correction key at original
export and require conflict with no file/record/audit mutation. Use a valid retained correction
fixture in the local original-export path; require refusal before file output. Confirm both tests
fail against the unfixed paths, then verify legitimate original and correction exact replay, changed
period/mode/profile, tenant/permission refusal, current original compliance refusal and duplicate-write
recovery coverage. No test or source guard is implemented in this review. Full P1–P3 enforcement,
retained physical projection, F8 extension, common SQL admission, key staging and full R2 remain
separately bounded follow-ons.

### Proposed fail-first acceptance (not executed)

| ID | Fixture, boundary and required observation |
|---|---|
| R1-01 | Accept G1 using the synthetic upload worker; request a fresh key/control/account or mode for the same original. Refuse generation with no new file/event/audit; contrast an independent eligible claim. |
| R1-02 | Retain G2 before G1 acceptance. After G1 accepted upload, refuse G2's new queue. For the separate pre-send case, legitimately queue G2 while G1 is Generated-only, then import an exact G1 receipt representing outside-queue transmission (or use R1-08). Hold G2 before Sending, zero successor uploads and retained G2 bytes unchanged; do not seed two simultaneous unresolved dispatches by editing states. |
| R1-03 | Sending without attempt, OutcomeUnknown and timeout/lost-reply histories refuse fresh original/queue/successor send. Count physical connector calls, not just final state. |
| R1-04 | Generated-only and genuine CancelledBeforeSend permit an eligible new generation/queue; ConfirmedNotReceived uses the real Admin reconciliation request, exact manifest/revision and synthetic SupportCase evidence. No state-edit shortcut. |
| R1-05 | Replay the exact generation/request and existing queue identity after acceptance; same bytes/result, unchanged generation/intent/attempt/audit counts. Changed period/mode/routing profile or original-versus-correction request kind conflicts; queue replay also conflicts on changed exact account. Current compliance revocation still refuses original export replay; correction replay retains its separately scoped rules. |
| R1-06 | Exercise legitimate frequency-1 Resubmit, frequency-7 replacement and frequency-8 void with exact persisted correction links and applicable receipts. Invalid/unlinked/mismatched correction is held. Keep existing action policy and void-purpose limits. |
| R1-07 | Malformed physical retained content, wrong control/period/note/D9, duplicate claims, missing links and foreign owner/account/receipt matches hold safely. Generated-only history must not become physical send evidence. Cover group-level and per-claim receipts. |
| R1-08 | Genuine OutcomeUnknown G1 → audited ConfirmedNotReceived → generate/queue successor G2 → import an exact late G1 receipt through real manual/mock ingestion → worker. G1 may remain state 7; the receipt still holds G2 before Sending, with **no second upload**. Repeat with rejected 999 and exact claim receipt; do not substitute an ERA accepted-dispatch shortcut. |
| R1-09 | Toggle mode/account/namespace and use all-R accepted upload, generic rejection and explicit 999 rejection fixtures. Verify accepted P1/P2 semantics without an IsTest exception. Give unrelated positive tests separate business histories instead of weakening the guard. |
| R1-10 | Fail generation/queue/hold/result/audit/receipt commit and interrupt/restart at the durable Sending boundary. No partial success or lost receipt effect; post-Sending uncertainty stays quarantined. Exact local/API projection parity is required. |

For each future security/concurrency/replay regression, retain the intended failure against the
unfixed safeguard before keeping the test, then fixed results, exact source and command. Pure rule
and SQLite path tests prove logic/atomic effects only. Real SQL proof must use the existing guarded,
uniquely named private LocalDB runner and synthetic fixtures; no shared working database.

Known fixture repair targets: `BillingCorrectionApiTests.RunMockAsync` repeatedly generates originals
for shared period 1202 after earlier lifecycle history, and
`TenantAuthorizationTests.RetryingEdiGenerationReplaysTheExactFileAndAuditsOnce` uses that shared
period. Give these positive cases isolated legitimate histories, following the existing rejected-claim
and joined-pipeline fixture patterns. Keep their authorization/replay assertions and real endpoints.
Direct status/ERA processor calls also need a supported single-attempt execution scope when the
private SQL factory enables retries; an execution-strategy setup refusal is not an R1 regression.

SQL barrier plan: `SyntheticPipelineDatabase(sqlServer: true)`, two independent
hosts/contexts/connections and explicit completion signals with
bounded waits, not sleeps. Exercise existing real generation/queue, worker and receipt entry points.
Pause A after it holds the common admission lock and before its authoritative history read/decision;
start B on the different ServiceTime/BillingPeriod/plain-receipt path, assert its decision has not
passed, commit A and prove B refreshes facts. Reverse each order, including two fresh-key queues,
accepted-result commit versus fresh generation, and receipt import versus worker Sending. Inject
failure/cancellation while waiting and while holding; prove rollback/disposal and unrelated agency B
progress. Test multi-period receipt locks and dispatch/reconciliation/reset ordering separately.

For the late-receipt race, pause the worker **before acquiring admission**, commit G1's exact late
receipt, then resume and require no G2 Sending/no second call. Reverse order: commit G2 Sending
first, then the receipt; assert honest uncertainty/conflict handling, not a retroactive no-send claim.
Use command/transaction interceptors or injected internal barriers at actual owners, and independently
observe lock acquisition/commit and connector-call counts. Red runs must reach the unsafe path and
fail the safety assertion; a timeout/deadlock alone is not fail-first proof. If the actual unfixed SQL
query ranges happen to prevent a candidate race, document that result and redesign the proof rather
than claiming a failure. No barrier, SQL test or application test was run in this design slice.

## Resolve an uncertain upload without resending

Sending after interruption and OutcomeUnknown are quarantine states. Do not
requeue them, edit rows directly, or generate another period file before review.
A filename in uploadlist alone is insufficient. Claim.MD claimdata regenerates
the current 837P and does not prove retention of original uploaded bytes.

1. An agency Admin loads GET
   /api/v1/admin/clearinghouse/dispatches/{dispatchId}/reconciliation.
   Compare revision, account number/ID, generation, filename, SHA-256 and every
   PCN/D9 pair with authoritative vendor account/file/claim evidence. Protect
   vendor evidence outside logs. Ambiguity means leave quarantine and seek support.
2. Submit POST to the same route with expectedRevision, expectedAccountId,
   evidenceAccountNumber, expectedEdiGenerationId, expectedContentSha256,
   evidenceFileName, decision, evidenceKind, opaque evidenceReference, retained
   proof's evidenceSha256, UTC evidenceObservedAtUtc and exact attestation copied
   from the manifest. Evidence must postdate the relevant dispatch/upload attempt.
   A busy upload or changed source returns 409; refresh and review again.
   Foreign dispatch IDs return 404 before upload-lease lookup, including while busy.
3. ConfirmedReceived requires AccountFileRecord or SupportCase evidence, numeric
   externalFileId, accepted/rejected counts, and every exact claimReference,
   remoteClaimId and status (A or R) in claims. This records clearinghouse receipt,
   never payer acceptance.
4. ConfirmedNotReceived requires SupportCase evidence confirming non-receipt
   after allowing for delayed processing. File ID and counts must be null, claims
   empty. It is a distinct terminal finding, not a vendor rejection.

The API validates internal consistency and the Admin's attestation; it does not
ask the vendor to certify the finding or original-byte identity. Original attempts
and receipts remain unchanged. Resolution, audit and submission-history event
commit together. Repeated/stale findings cannot overwrite it. Neither decision
automatically sends anything; a future send needs a separately reviewed new
generation through the existing workflow.

## End testing and restore ordinary Demo resets

1. Stop queueing uploads, disable sandbox transport through the approved process,
   and let in-flight work finish. Reconcile uncertain dispatches and retain final
   independent feed checkpoints.
2. Preserve the **complete** linked sandbox database/encrypted evidence, recovery
   keys, account/claim correlations and audit history under the approved retention
   procedure. Prove readback/recovery. A count report or claimdata download is
   insufficient.
3. Use a separately approved return-to-canonical Demo procedure that retains that
   sandbox history independently and removes live vendor linkage from the
   resettable environment. A cutover to verified clean canonical Demo is one
   possible approach; the archive/cutover adapter is **not implemented here**.
   Retire the sandbox credentials/account association and reserve its namespace.
   Do not erase audit rows, bypass the guard or capture linked data as a baseline.
4. Verify clean Demo identity/baseline, transport off, guarded procedures and no
   pending reset requests. Then re-enable
   **AzureWebJobs.RefreshCaseload.Disabled=false** and run one separately approved
   verification reset. Confirm its audit and resumed watchdog expectation.

Flipping the timer back on against the linked database yields guarded failures.
Preservation and return to clean Demo are prerequisites for pause/test/resume.

## Before live MaineCare billing

This does not certify payer readiness. The formatter lacks the Claim.MD-documented
Maine Medicaid service-facility ID in block 32/loop 2310C (xxxx-xxx). Payer ID,
effective provider identifiers, taxonomy, authorizations, service code and actual
enrollment need review against current MaineCare requirements before real claims.
These gaps are tracked separately in AGENDA.md.

Primary references: [quickstart](https://docs.claim.md/docs/test-account-quickstart-guide),
[API](https://api.claim.md/),
[companion guide](https://www.claim.md/ClaimMD_Companion_Guide.pdf),
[account settings](https://docs.claim.md/docs/account-settings),
[Maine Medicaid](https://docs.claim.md/docs/me-medicaid).
