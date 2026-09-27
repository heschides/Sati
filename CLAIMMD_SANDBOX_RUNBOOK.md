# Claim.MD sandbox readiness — source-only Phase 5

This is an operator checklist, not an activation authorization. No Claim.MD account has been contacted by this work. The transport remains default-off and Demo-only. Do not use real consumer PHI or Production billing.

## Before enabling the Demo transport

1. Obtain a dedicated Claim.MD developer/test account and current companion guide. Confirm its test interchange behavior, account number, receiver/submitter values, and `remote_claimid` duplicate-field setting with Claim.MD. Keep the API key only in approved API-host secret storage.
2. Review and apply the still-unapplied clearinghouse migration through the normal backup, restore, and deployment procedure. Provision one agency-scoped test account with a permanent environment/account namespace; never reuse that namespace for another agency or environment.
3. Review each existing status and ERA in that Claim.MD account before choosing separate starting cursors. A new, verified-empty feed can start at `0`; an existing feed requires a documented reconciliation of earlier records and an approved high-water mark. Cursor creation needs an audited, controlled operator procedure; no provisioning endpoint is supplied in this phase. Without a valid checkpoint for a feed, its poller makes no vendor request. Never insert a cursor merely to make polling start.
4. Rehearse one-account and two-host behavior with synthetic data. SQL Server application locks must serialize Claim.MD requests across API hosts and permit only one poller run. If coordination storage is unavailable, the connector fails closed; do not bypass its gate. The code waits at least one second under the request lock before each API call, below Claim.MD's documented 100-call-per-minute ceiling.
5. Validate actual vendor response variants, encrypted evidence readback, rejection/resend/replacement/void correlation, status and ERA processing, and bank reconciliation in the sandbox. Review operational monitoring and the unresolved security gates before any broader rollout.

## When an upload is uncertain

`Sending` after interruption and `OutcomeUnknown` are quarantine states. Do not retry, generate another file for the same period, manually change a dispatch state, or treat a matching filename in `uploadlist` as proof of receipt. Record the dispatch and agency/account IDs, retained generation and SHA-256, CLM01 and REF*D9 identities, attempt time window, encrypted vendor-response reference if present, and any Claim.MD file ID. Handle all claim content and vendor evidence as sensitive data.

The agency billing lead and Sati operator must compare retained evidence with Claim.MD's authoritative account/file/claim records. A positive finding needs unambiguous linkage to this exact generation and claim identities; a negative finding needs documented evidence that the file was not received, accounting for delayed processing. If neither is proven, leave the dispatch blocked and contact Claim.MD support. There is currently no authorized software path to resolve a quarantined dispatch or resend it. Implement and test an audited, tenant-scoped reconciliation workflow only after the vendor's evidence semantics have been verified; an operator should not repair rows directly in SQL.

The sandbox is not certified, connected, or ready for production billing merely because this checklist or the source path exists.
