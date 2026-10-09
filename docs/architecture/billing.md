# Billing and clearinghouse boundaries

**Status:** current architecture reference, October 9, 2026. Detailed rules remain with the linked canonical owners.

Implemented: approved-note projection is a query, not a separate message queue; claim promotion freezes inputs, generation retains immutable bytes, dispatch stages durable intent, and receipt/effect/feed-cursor writes commit together. Agency/account/generation identity is verified. Sending/OutcomeUnknown is quarantined; timeout never proves nonreceipt.

The local connector deadline now covers response-body I/O as well as headers. The
[sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md#http-exchange-deadline--local-source-october-8-2026)
owns the component boundary; [working evidence](../readiness/work-evidence.md) records synthetic
upload timeout acceptance. No retry, quarantine or account/generation policy changes.

`Sati.Contracts.V1.BillingExportGate` is the shared API/transitional-local export and replay
owner. It checks frozen source/exception integrity and retains the callers' authoritative
service-date compliance errors after exact-obligation exceptions and Admin recovery have been
applied. A matching stored exception cannot discard an unrelated remaining blocker. The
[sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md#current-export-and-replay-compliance--local-source-october-8-2026)
owns this bounded source behavior and its release limits; [DEC-0226](../decisions/current/2026-10-08-DEC-0226.md)
records the choice and rejected alternatives. Working evidence records actual verification.

Original/correction lifecycle admission (assessment R1) and current retained-subset release
checks (R2) are now implemented locally. Contracts owns `OriginalClaimReleaseRules`,
`ClaimCorrectionRules`, `EdiReplayRules` and `BillingExportGate`; Persistence owns the common
validated history projection and `ClaimReleaseWriteScope`. API adapters own trusted scope and
current exact-subset validation. The transitional local service uses the same original rule,
projection and admitted transaction rather than adding a local cloud/receipt scheduler.

The [coordinated release owner](../../CLAIMMD_SANDBOX_RUNBOOK.md#coordinated-release-and-current-subset-compliance--local-source-october-9-2026)
and [DEC-0233](../decisions/current/2026-10-09-DEC-0233.md) own transaction order, wrapping outside
SQL, refreshed decisions, single-attempt operation, safe recovery, late-receipt precedence and
current correction purpose. Void preserves standing-bill withdrawal; Resubmit/Replace must pass
current positive billability for their selected subset. GET options are advisory; commands enforce
current facts inside admission. An unused render never becomes a received claim.

Josh's [adopted policy](../decisions/current/2026-10-09-DEC-0230.md),
[request-kind replay](../decisions/current/2026-10-09-DEC-0231.md) and
[shared projection](../decisions/current/2026-10-09-DEC-0232.md) remain authoritative.
[Working evidence](../readiness/work-evidence.md#2026-10-09--coordinated-claim-release-and-compliance)
records actual mutation proofs, private SQL barriers, rollback and suite results, including limits.
The earlier proposal/review remains a dated design record explicitly superseded within these
source boundaries. Fresh keys are not business lifecycle permission. No live payer/vendor,
operating, capacity/fairness, complete-service recovery, legal or sealed-readiness closure follows.

## Canonical detailed owners

- [PAYER_BILLING_REQUIREMENTS.md](../../PAYER_BILLING_REQUIREMENTS.md)
- [PAYER_BILLING_CERTIFICATION.md](../../PAYER_BILLING_CERTIFICATION.md)
- [CLAIMMD_SANDBOX_RUNBOOK.md](../../CLAIMMD_SANDBOX_RUNBOOK.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)

Account preparation readiness is separate from physical delivery and claim lifecycle.
[W8](../../BACKGROUND_WORKERS_HANDOFF.md#october-9--known-unsent-dispatch-isolation-implementation)
owns the approved shared readiness rules, persistence and authorized recovery route. Missing
keys defer Queued work without clearing reservations or creating send evidence; restoration
resumes ordinary R1/R2 checks, and Sending/OutcomeUnknown require existing reconciliation.
