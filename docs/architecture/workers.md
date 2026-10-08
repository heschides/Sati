# Background execution and shared capacity

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented source: maintenance catches up after host wake, uses bounded batches and revision-checked transactional system summaries, and coordinates with reset and other hosts through SQL leases. Dispatch/poll/package/mail owners retain their established uncertainty, cursor and retry policies.

The first October 8 local SATI-WRK-001 slice adds narrowly classified agency-sweep failure continuation:
failed agencies remain due while healthy later agencies can complete that pass. Shared/unknown
faults and cancellation propagate. The [W8 owner](../../BACKGROUND_WORKERS_HANDOFF.md) and
[working evidence](../readiness/work-evidence.md) record the policy, tests and evidence limits.

The second slice puts the HTTP exchange deadline in `ClaimMdSandboxConnector`, with the same
linked token through headers, stream acquisition and body reads. Client registration uses the
connector's configuration helper and has no competing timer. [DEC-0223](../decisions/current/2026-10-08-DEC-0223.md)
and [the sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md) own the timer policy and uncertainty
boundary. This is local cooperative I/O acceptance; admission, processing and cleanup remain
separate components, and whole-operation/pass bounds remain open.

The third slice bounds note-worker discovery to named 100-agency keyset pages within a captured
nullable maximum ID. The nullable first cursor preserves damaged-ID rejection; observed higher-ID
growth keeps the day due, with an any-agency check for an initially empty range. This is a finite
key range, not membership isolation: within-range insertions after the last discovery query,
lower-ID insertions behind the cursor and post-check growth remain unguaranteed.
[DEC-0224](../decisions/current/2026-10-08-DEC-0224.md), W8 and working evidence own the paging
policy, exact passing acceptance and evidence limits. The distinct 100-note sweep and existing coordination,
failure, audit, cancellation and cadence owners are retained.

Remaining W8: current-day completion bookkeeping, fair healthy-agency wait bounds, total
operation/pass deadlines, per-account failure/dependency isolation, aggregate and tenant
budgets, API admission and synthetic
multi-host/load evidence. Agency IDs and a worker per agency do not by themselves isolate
shared SQL or vendor capacity. Next is stale-local-day cache retirement under the existing run
gate; this bounds historical retention, not current-day cardinality or total memory. Business-rule,
route and persistence boundaries are unchanged.

## Canonical detailed owners

- [BACKGROUND_WORKERS_HANDOFF.md](../../BACKGROUND_WORKERS_HANDOFF.md)
- [OPERATIONS.md](../../OPERATIONS.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)
