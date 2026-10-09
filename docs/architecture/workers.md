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

The fourth slice rotates completion-cache storage when the captured `ApiClock.Today` changes,
under `runGate` before the global completed-day idle check. Same-day completion entries and
retry/skip behavior remain. [DEC-0225](../decisions/current/2026-10-08-DEC-0225.md), W8 and working
evidence distinguish source storage replacement, tested retained cardinality and passing local checks.
Rotation follows an enabled gate-acquired check; there is no new idle cleanup scheduler or
measured memory bound. Current-day cardinality/churn and same-day removed-agency entries remain.

The October 9 approved missing-key slice adds Contracts readiness rules, shared Persistence
account state and API-owned due/hold/reopen. [DEC-0235](../decisions/current/2026-10-09-DEC-0235.md)
and [W8](../../BACKGROUND_WORKERS_HANDOFF.md#october-9--known-unsent-dispatch-isolation-implementation)
own exact recovery, staging, account session ownership, safe API/UI and additive source migration.
[Working evidence](../readiness/work-evidence.md#2026-10-09--missing-key-dispatch-isolation-implementation)
distinguishes fake-time/source tests from private SQL ownership and operating evidence.

The October 9 [idle/wake proposal](../../BACKGROUND_WORKERS_HANDOFF.md#october-9--dispatch-idle-wake-scheduling-proposal)
inventories current cadence and proposes one API-owned activity/wait boundary. [DEC-0236](../decisions/current/2026-10-09-DEC-0236.md)
is pending policy review; no new scheduling service or runtime cadence is implemented. It uses
validated/committed existing requests as hints, not authorization or guaranteed user presence.

Remaining W8: adoption and implementation of idle/wake integration (the three-second idle loop is unchanged), sustained
fairness, current-day completion capacity, total operation/pass deadlines, aggregate/tenant
sessions and dependency budgets, API admission and synthetic load/live owner evidence. Account
leases and missing-key deferral do not establish complete shared-dependency isolation or global
latency bounds. [The agenda](../../AGENDA.md#next-eligible-work) owns the next bounded slice;
[billing release admission](../../CLAIMMD_SANDBOX_RUNBOOK.md#coordinated-release-and-current-subset-compliance--local-source-october-9-2026)
remains authoritative. No existing/cloud migration or activation is established by source checks.

## Canonical detailed owners

- [BACKGROUND_WORKERS_HANDOFF.md](../../BACKGROUND_WORKERS_HANDOFF.md)
- [OPERATIONS.md](../../OPERATIONS.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)
