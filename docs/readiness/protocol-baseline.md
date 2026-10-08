# Reliable protocol baseline

This is the proposed contract and investigation baseline for tenant isolation and safe replay. It records requirements and gaps; it does not claim Sati already implements every rule or authorize changing business code, deployed resources or existing databases. Use the [criterion ledger](readiness.json) and [failure contingencies](multitenancy-contingencies.md) to track actual evidence.

Primary references were checked October 8, 2026. Recheck their status and the intended provider/account contract before adopting a change.

## HTTP behavior and confidentiality

Use standard method, status and conditional-request semantics from [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html). Safe reads should not initiate a requested business mutation. PUT/DELETE method idempotence does not imply that every audit/log side effect is identical, and POST is not automatically safe to repeat. A client must not blindly replay a non-idempotent action after losing its reply. Document queued work separately from completed work; HTTP 202 represents acceptance for processing, not successful external delivery.

Sati already uses expected revisions and typed conflicts. Preserve that supported body contract unless a versioned change is justified. Strong ETags and If-Match are a suitable standard mapping for representation updates: authorize the scoped resource first and atomically compare the appropriate current version at the database write. An HTTP precondition failure uses 412; existing application-level revision conflicts can remain documented 409 responses. Do not replace a stale revision with the current value and retry an overwritten edit. Revision checks prevent stale changes; an operation identity separately protects repeated creation.

Apply [RFC 9111](https://www.rfc-editor.org/rfc/rfc9111.html) deliberately to clinical, financial and authenticated responses. Choose explicit no-store/private directives for each representation according to approved handling; private alone still permits a private cache. Vary is not a tenant authorization wall. Custom caches, generated files, search results and downloads need trusted agency/resource/version bindings, revocation and expiry rules. Test proxy/private caches and account switching; do not assume TLS prevents content from being cached.

Use safe structured problem responses based on [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html) when useful. Keep stable problem types/codes and a safe correlation reference; do not return stack traces, raw vendor responses, narratives, keys or existence details for another agency's resource. Problem Details standardizes an error representation; it does not establish authorization or replay safety.

## Durable operation identity

The IETF's [Idempotency-Key header draft](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) is **not a finalized RFC**. At this review it is `draft-ietf-httpapi-idempotency-key-header-07`, expired/archived, latest revision October 15, 2025. It is design input, not a current normative guarantee. An application may define its own explicitly versioned idempotency contract; using that header name does not supply persistence, deduplication or vendor agreement.

For an operation chosen for safe replay:

1. Obtain an unpredictable bounded request key before the first attempt and retain it across lost replies, reconnects and permitted retries. A genuinely different business action gets a different key.
2. Derive trusted agency and operation scope on the server. Include the documented resource/actor scope where necessary; never key only by a caller-supplied agency ID or raw GUID. Do not let one tenant enumerate another tenant's operation result.
3. Persist a versioned canonical fingerprint of the accepted semantic request. Define field normalization and contract version; distinguish the same request from changed amounts, recipients, documents, accounts or revisions.
4. Atomically reserve the scoped key with a database uniqueness rule or equivalent durable control. Record processing/completed/refused/uncertain state and the stable result/reference in the same protected workflow. Avoid check-then-insert races.
5. Same key with the same fingerprint returns the documented retained result or in-progress state. The same key with changed meaning is a conflict, never permission to overwrite the first effect. Reauthorize before releasing a replay response; a key does not bypass revocation.
6. Publish key lifetime, maximum retry/redelivery/restore horizon, in-progress behavior and conflict policy. Do not expire unresolved external outcomes or silently treat an old business identity as a new original. Protect replay records as official evidence where applicable.

These are Sati application design requirements, not claimed HTTP guarantees. Existing generation/chat/receipt identities are useful owners to extend. General note-create replay remains a separate inspected gap. A unique claim line per Note ID does not prove two newly created Note IDs cannot represent the same underlying work.

## Local transactions, outbox and inbox

A database transaction can atomically commit related business state, required audit and durable outbound intent. The relay then delivers the saved intent. The receiver must deduplicate a durable incoming identity and commit its effects with the receipt/checkpoint; delivery attempts can occur more than once. Microsoft's [transactional outbox explanation](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos) illustrates the pattern; Sati's implementation must use its actual SQL/EF owners and provider tests rather than copying a Cosmos-specific design.

Keep three identities distinct: a client's request, the underlying business event/claim, and the provider's received file/message. A new request/file key is not permission to repeat an accepted original claim. Preserve original/correction lineage and current release eligibility independently of deduplication.

If a broker is proposed, [Service Bus duplicate detection](https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection) operates on configured message identity and a finite history window. It cannot replace business uniqueness, effect deduplication, tenant authorization, or recovery after the window/history is lost. Sati is not assumed to use Service Bus. Broker ordering or exclusive sessions also do not establish fair tenant service.

Never promise universal “exactly once.” State the bounded guarantee: one durable effect for a specified scoped identity and supported lifetime, under a named transaction/receiver contract. Remote delivery ambiguity, restore rollback, alternate keys, distinct providers and expired records remain explicit boundaries.

## Retry and outcome states

A deadline must cover admission, lease wait, request headers, response body, processing and commit as appropriate; name those component budgets and the total. Sati's Claim.MD client uses ResponseHeadersRead, so a configured HttpClient timeout alone does not prove the body is bounded. Test a body that never finishes. Cancellation stops local waiting; it does not prove the remote service did nothing.

The October 8 local SATI-WRK-001 slice now supplies that connector HTTP I/O deadline and
stalled-body fail-first proof, with a single timer owner. [The sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md#http-exchange-deadline--local-source-october-8-2026)
defines its component boundary; [working evidence](work-evidence.md) records actual results.
Admission/pacing, processing/commit/cleanup and total operation/pass budgets remain separate
unclosed requirements. Synthetic cancellation acceptance is not live-provider or fairness proof.

Use [Microsoft's retry guidance](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry) to classify transient faults and bound attempts. Apply exponential delay with jitter, respect trusted provider retry instructions, and cap aggregate concurrent/retried work. Keep retry ownership in one named layer so nested SDK/API/client retries do not multiply the budget. Log safe shapes/codes/counts, not provider payloads.

Use a [circuit breaker](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker) only at the scope actually failing. One bad agency credential must not open a breaker for healthy independent accounts. A truly shared dependency failure can justify shared protection. Bound half-open probes across hosts and drain recovery backlog fairly; do not flood a recovering provider.

| Operation/result | Permitted response |
|---|---|
| Read failed before a result | Bounded retry after reauthorization, within the read's budget. |
| Local transaction definitively rolled back | Retry only the documented safe workflow with the original operation identity and current authorization. |
| Local commit reply lost | Query/replay the durable operation identity; do not create a fresh business action. |
| Validation or permission refusal | Return the safe terminal/refusal state; do not retry as a transient fault. |
| Stale revision/precondition | Refetch and require review; do not blindly overwrite with a refreshed revision. |
| Provider rate limit or recoverable known-unsent fault | Capped deferred retry under the real shared/account quota, only if remote non-effect is established or the provider's idempotency contract makes it safe. |
| Queued preflight fails before send intent | Preserve truthful unsent/backoff/held state; allow healthy lanes to progress. |
| Sending committed, timeout/host death/lost reply | Retain Sending/OutcomeUnknown and hold for exact reconciliation. A timeout is not nonreceipt. |
| Remote accepted, local evidence commit fails | Quarantine the durable send identity; investigate provider receipt before any separately approved resend. |
| Incoming duplicate receipt/webhook | Authenticate and scope it, return the documented accepted duplicate outcome, and apply no second effect. |
| Restored database or rolled-back client loses outcome knowledge | Pause affected writers/relays and reconcile external identities, later valid records and cursors before resuming. |
| Mail-provider submission is uncertain | Preserve provider operation and lease/generation state; use the notification's documented reconciliation path, never an unrestricted resend. |

Existing ClearinghouseDispatchWorker commits Sending first, refuses automatic Sending/OutcomeUnknown replay and disables its EF upload retry scope. Preserve those safeguards. Signature mail has a different provider operation and recovery protocol; do not impose claim resend semantics without inspecting that owner.

## Identity, ordering and lease safety

Every background message, scheduled row and callback carries the immutable trusted owner binding and is rechecked at execution. Include agency/account/feed identity in durable uniqueness and cursor keys. Authenticate a provider callback, bound bytes, bind provider event identity and account, reject stale/altered replay, and commit inbox effects before acknowledgment. Webhooks are an investigation category; do not imply they already exist.

A lease provides exclusive ownership only while its conditions hold. On expiry/replacement/connection loss, stale holders must be unable to commit newer state or publish another effect. Use documented fencing/compare-and-write tokens or provider operation identities appropriate to the workflow. Database application locks coordinate independent hosts; process-local locks do not. Preserve required reset/reconciliation lock ordering and avoid holding unrelated SQL connections across long network waits.

Order only the events requiring it. An identity allocation or timestamp is not necessarily commit order; durable feed sequence/checkpoint rules must prove completeness. Agency fairness, a provider-wide quota and per-record exclusivity solve different problems and require separate tests.

## Minimum acceptance evidence

For every new or changed operation contract, record the owning rule/route/job, scope, uniqueness/transaction boundary, response states, maximum bytes/time/retries, retained result lifetime and permitted recovery. Test two independent hosts, same/different tenant keys, same/different payloads, stale authorization, lost replies, before/after-commit faults, lease expiry and restore/history loss. Demonstrate regressions fail against the unfixed safeguard.

Keep primary standards as design references and execution artifacts as acceptance evidence. Adoption of a protocol document does not raise a readiness score by itself. Changes to current endpoints, migrations, activation or provider use require their normal separate implementation and approval workflow.
