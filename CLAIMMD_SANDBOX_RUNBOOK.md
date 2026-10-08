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
