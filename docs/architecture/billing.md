# Billing and clearinghouse boundaries

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented: approved-note projection is a query, not a separate message queue; claim promotion freezes inputs, generation retains immutable bytes, dispatch stages durable intent, and receipt/effect/feed-cursor writes commit together. Agency/account/generation identity is verified. Sending/OutcomeUnknown is quarantined; timeout never proves nonreceipt.

The local connector deadline now covers response-body I/O as well as headers. The
[sandbox runbook](../../CLAIMMD_SANDBOX_RUNBOOK.md#http-exchange-deadline--local-source-october-8-2026)
owns the component boundary; [working evidence](../readiness/work-evidence.md) records synthetic
upload timeout acceptance. No retry, quarantine or account/generation policy changes.

The October 8 assessment identifies server original/correction lifecycle and current documentation/compliance release gaps (R1/R2). Fair scheduling must preserve these separate gates; formatter/parser tests do not certify vendor or payer acceptance.

## Canonical detailed owners

- [PAYER_BILLING_REQUIREMENTS.md](../../PAYER_BILLING_REQUIREMENTS.md)
- [PAYER_BILLING_CERTIFICATION.md](../../PAYER_BILLING_CERTIFICATION.md)
- [CLAIMMD_SANDBOX_RUNBOOK.md](../../CLAIMMD_SANDBOX_RUNBOOK.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)
