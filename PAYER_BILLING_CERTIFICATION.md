# Payer certification and controlled activation checklist

This checklist is separate from software acceptance. Owner: **Josh**, who must name the agency's
authorized billing/enrollment reviewer and payer/vendor test contact before operational activation.
Source feature work does not authorize deployments, cloud migrations, firewall changes, real claims,
real credentials, access to SatiProduction or external submissions.

## Agency and payer evidence

- [ ] Obtain and retain the current MaineCare 837P companion guide after the announced October 12,
  2026 portal transition. Record title, version, retrieval/effective dates and a protected evidence ID.
  Resolve the Claim.MD crosswalk discrepancies identified in [the matrix](PAYER_BILLING_REQUIREMENTS.md).
- [ ] Confirm provider enrollment, authority to bill Section 13, population/code/modifier applicability,
  billing/rendering entity types, NPIs, taxonomies, tax ID and service locations with the named reviewer.
- [ ] Confirm the facility ID and exact 2310C REF qualifier (LU or G2) for this enrollment and vendor route.
  A synthetic test's G2 is not MaineCare approval. Unsupported qualifiers require a reviewed source change.
- [ ] Confirm `MEMCD` routing through Claim.MD and the licensed X222A1 implementation requirements;
  reconcile claim-level service date, POS qualifier, rendering/facility situational rules and diagnosis pointer.
- [ ] Verify the applicable Section 13/Chapter I unit and rounding policy, minimum-contact aggregation,
  current fee schedule and SPA-dependent conditions. Record the rate basis and service-date scope.
- [ ] Retain member/service/provider authorization evidence with decision date, covered dates and scope.
  Confirm quantities externally until prompt 6's utilization ledger is implemented.
- [ ] Supply protected evidence references and explicitly review them before publishing a profile.
  No evidence document, clinical narrative or credential belongs in operational audit metadata.

## Controlled migration (not executed against cloud or working data)

Migration: `20261004204633_AddPayerBillingConfigurationVersions`, after
`20261004120026_AddNoteAmendments` (125 -> 126 migrations). API and WPF must both be upgraded to
the matching source. There is one additive table with restrictive agency/creator FKs, unique
revision/start indexes and append-only EF protection. Existing financial rows/files are unchanged.

1. Capture reviewed backup/recovery evidence and the exact current migration inventory. Preflight
   must confirm Demo identity and the previous migration chain. Arrange a bounded maintenance window.
2. Supply Josh the workstation's verified public IPv4 and the existing user-run PowerShell helper
   for **both adding and removing** the rule. The assistant must never run either security change:

   ```powershell
   # Josh substitutes the public IPv4 reported by release preflight.
   .\scripts\Set-DemoWorkstationFirewallRule.ps1 -Ip '<verified-public-IPv4>' -RuleName 'datt-payer-billing-temp'
   # Josh runs this immediately after the bounded migration, including on failure.
   .\scripts\Set-DemoWorkstationFirewallRule.ps1 -Remove -RuleName 'datt-payer-billing-temp'
   ```

3. Review and run `Apply-PayerBillingMigration.ps1 -WhatIfOnly`, apply once, then rerun to verify
   idempotency. The pinned guarded SQL checks identity, predecessor/count and the complete new table
   schema and indexes under the existing exclusive Demo reset/mutation lock. Record script hash/outcome.
4. The Demo baseline predates the new table. The existing canonical reset correctly refuses a schema
   mismatch. Capture a reviewed replacement baseline through the established reset playbook before
   resuming reset acceptance; a database migration does not authorize a fresh reset or baseline capture.
5. Verify matching client/API health, protected configuration reads and a private synthetic profile/claim.
   Leave real configuration/enrollment and external transport inactive until this checklist is approved.

Private rehearsal command: `pwsh -NoProfile -File scripts/Test-IsolatedLocalDb.ps1 -PayerBillingOnly`.
It creates and removes a uniquely named LocalDB instance; it never uses the shared workstation instance.

Rollback is permitted only while the new table is empty and no v2 financial snapshots exist. Migration
Down refuses written configuration history or v2 claim/correction snapshots, including imported
snapshots when the configuration table is empty. Once used, recover forward with the matching binaries;
older binaries cannot interpret v2. Never delete provenance or downcast v2 to make rollback succeed.

## Synthetic and vendor acceptance

- [x] Run the reviewed synthetic tests: effective boundaries/expiry, field failures, authorization scope,
  stale revision, tenant/permission denial, atomic audit failure, local/API parity and retained legacy output.
- [x] Retain the v2 golden 837P and check loop placement, ISA width, ST/SE counts,
  multiple claims, ordered modifiers, D9/CLM/F8 identities and disallowed delimiter/control input.
- [x] Rehearse migration up/down-before-use, rollback refusal after publication or v2 financial use, guarded rerun and SQL
  contention against a private synthetic database. Retain result evidence.
- [ ] Obtain separately authorized Claim.MD sandbox testing with agency/payer-approved synthetic data;
  use existing server-owned accounts, secret references, test-mode and dispatch safeguards. No real
  credentials go into the desktop, profile JSON, golden fixtures or Git.
- [ ] Verify 999/277CA acceptance, payer claim identity and remittance correlation, including replacement
  and void, with the vendor/payer. Keep original files/receipts and decisions as immutable evidence.
- [ ] Obtain written direction for live submissions, credential operation, monitoring, recovery, privacy,
  retention and named ownership. Software tests are not payer certification or HIPAA compliance.

Remaining supported-scope limits: current single-line Section 13 unit basis; explicit manual
authorization evidence; no utilization ledger; no configuration rebase of an already submitted claim.
Each needs a separately reviewed extension where agency requirements demand it.

## Source verification evidence (October 4, 2026)

All fixtures are synthetic. Test scopes overlap and are recorded separately:

| Scope | Evidence and outcome |
|---|---|
| Desktop affected billing regression | `TestResults/PayerBilling/Green/billing-desktop-regression.trx`: 542 passed; 12 export cases hit sandbox file-write denial. All 20 export cases then passed in `local-export-regression.trx` with access to their own random-named test files. The combined 554-case scope has no unresolved failure. |
| Payer, formatter, persistence and affected local billing/administrator UI | `payer-desktop-green.trx`: 69 passed. Configuration/readiness views were rendered and inspected; administrators without Billing can reach editing while consumer preparation remains denied. Local preview/creation require note, consumer and owner agency markers to match. |
| API affected billing/clearinghouse/amendment regression | `billing-api-regression.trx`: 312 passed; 11 private SQL cases skipped outside their opt-in runner. |
| Payer API | `payer-api-green.trx`: 11 passed, including configured replacement, original-file replay and unchanged encrypted receipts. The routing case was then expanded: `payer-routing-green.trx` has four passing canonical/alias/case/whitespace variants. |
| Private SQL migration/concurrency | `TestResults/IsolatedSqlServer/Api/Joshu_LONGCHENPA_2026-10-04_17_51_06_net10.0.trx`: three passed, including up/down compatibility, v2 downgrade refusal, guarded SQL rollback/apply/rerun/schema rejection and two-connection publication serialization. Its owned LocalDB instance was removed. |

Red evidence in `TestResults/PayerBilling/Red` confirms the administrator-denial test fails
when its permission guard is weakened; local actor revocation fails without the fresh transaction
read; a foreign-owner local preview failed before its scope guard; edited inputs exposed stale busy state before repair; the Other-profile Maine routing bypass
failed before its guard; and rollback with retained v2 snapshots failed before the migration guard.
The API audit-failure probe also exposed and repaired an erroneous conflict response, while proving
the transaction retained no configuration or audit row. Vendor acceptance remains unchecked above.
