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

The October 8 assessment's server original/correction lifecycle (R1) and full current
documentation/compliance recheck at queue and immediately before Sending (R2) remain open.
This residual-error repair does not extract the exact retained claim subset for dispatch,
classify physical send history or establish a correction/void release policy. Fresh generation
keys are not business lifecycle permission. Fair scheduling must preserve these separate gates;
formatter/parser tests do not certify vendor or payer acceptance.

The [October 9 R1 design](../../CLAIMMD_SANDBOX_RUNBOOK.md#proposed-original-release-guard--october-9-2026)
proposes one `OriginalClaimReleaseRules` Contracts owner, a shared retained-content projection,
three admission points and a common SQL decision boundary. These owners and locks do not yet
exist. Cross-mode, generic rejection and queued-reservation choices require policy review;
deterministic fail-first and private SQL acceptance are specified but not executed. The design
preserves the existing correction owner, safe result replay and late-receipt precedence. It does
not change implemented architecture, close R1/full R2 or advance sealed readiness.

## Canonical detailed owners

- [PAYER_BILLING_REQUIREMENTS.md](../../PAYER_BILLING_REQUIREMENTS.md)
- [PAYER_BILLING_CERTIFICATION.md](../../PAYER_BILLING_CERTIFICATION.md)
- [CLAIMMD_SANDBOX_RUNBOOK.md](../../CLAIMMD_SANDBOX_RUNBOOK.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)
