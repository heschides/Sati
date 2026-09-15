# Sati launch security review
September 10, 2026 — repository assessment after clearinghouse intake development

## Follow-up 2026-09-11: account lifecycle implemented in source

B05's password/session-revocation and explicit disablement gap is closed at the reviewed API and
ordinary local service entry points. Retained account state uses an enabled flag and monotonic
security version; password change/reset, explicit revocation and enabled-state changes invalidate
previous sign-ins. Renewal cannot adopt a newer version for an old credential. Missing-version
JWTs are intentionally refused after upgrade. Chat invalidation follows its bounded lease check,
not an immediate push that erases previously delivered content.

Same-agency administrator controls retain users and records, prevent self-disable, exclude platform
operators and audit changes. Review also found and closed a related password-reset/profile-change
takeover: supervisors must not manage an assigned user who also has Billing, Administration or any
other stronger capability. The shared target rule now requires case-management-only targets for
non-administrators. Local template administration and EDI replay/generation now check the current
session before accessing records; EDI additionally requires Billing.

API/JWT, local SQLite, client-transport and concurrency regression tests captured failures before
correction. Final verification and change scope are recorded in the lifecycle handoff. The schema
migration is source only: no real accounts, passwords, database records or deployed services were
changed. Deployment rehearsal, coordinated updated clients/server and fresh sign-ins remain
required. Session inventory/per-device revocation, MFA, full account recovery, direct-SQL trust,
global maintenance and ordinary in-flight-operation serialization remain separate work. This
follow-up is not a renewed complete audit or launch clearance.

Final solution verification: 2,719 tests passed across desktop (1,874), API (715), signatures
(118), portal (8) and Carika (4); one optional native-AI competence test was skipped. The API and
desktop suites include 160 additional cases compared with the preceding permission-revocation
handoff. The schema snapshot and SQL Server migration-script generation were checked without
connecting to SQL Server. This does not replace a controlled production-engine concurrency rehearsal.

## Follow-up 2026-09-11: ordinary consumer-record permission revocation

The authorized follow-up fixes B01's local login projection and B02's ordinary consumer
service/route capability gaps. Retained assignments no longer authorize own casework after
CaseManagement removal. API queries check current capability and person/owner agency; local
services check persisted identity/permissions instead of trusting an old session. SSN preparation,
AI context, consumer documents, review appointments, AT/PCP sources, and local supervisory/Admin
record services are included. Note reads/transitions, review and billing candidates enforce the
existing note-tenant reconciliation invariant; invalid markers are not inferred from assignment.

Billing remains independent, including eligible claim creation; supervision-only and
administration-only workflows retain their scope. This does not promise a Billing user sees no
approved-note narrative: the existing candidate DTO contains it. That deserves a separate
minimum-necessary workflow review. Local login copies actual permissions/contact fields and no
password verifier. Local permission changes require a fresh sign-in; protected operations deny
meanwhile. Already delivered/cached information is not remotely erased.

Related local AT authority gaps were closed: publication derives its signer from the session,
Add/Update cannot inject an attestation, and published requests cannot be deleted through the
service. PCP supervisor source selection uses the consumer's assigned author. This does not close
pre-existing local AT publication-completeness, identity-snapshot or audit-parity gaps.

Synthetic HTTP/JWT and SQLite tests reproduced missing guards before repairs, checked denied
operations left records unchanged, and verified positive persisted writes and Billing-only claims.
The expanded old-API proof has 24 failures and 40 passing controls; final verification totals are
recorded in the permission-revocation handoff. No real records, migrations or deployment were used.
The detailed baseline findings below remain historical evidence, not present exploit claims.

Still open after the lifecycle follow-up: approved B05 rollout; B04 globally scoped local maintenance;
direct SQL trust; in-flight revocation races; the other billing/launch findings. Supervisory SQL
target-user bit filters need alignment with the full supported-mask predicate for corrupted target
rows (actor masks already fail closed). Local `PersonService.EditPerson` attaches a caller-supplied
graph after root ownership validation; deep child ownership/workflow enforcement warrants a
separate proof rather than an assumption of safety.

## Follow-up: standalone form-deletion bypass closed in source

The subsequent user-authorized fix retires destructive standalone form deletion in both API and
local service. Current permission/tenant/caseload checks precede a shared retention refusal; no
persisted form is removed regardless of attestation or current requirement settings. Both new
regressions failed against the original implementation. Stored billing-window dates are unchanged.
The original audit evidence below is retained as a historical finding, not a claim that the
standalone operation remains exploitable after this fix.

Verification after the fix: 27 new API/local cases pass; the complete API suite passes 622 tests
and the complete desktop suite passes 1,610 tests with one optional native-AI competence test
skipped. No ordinary regression test fails. The original API's expanded baseline had 11 intended
failures and 5 unaffected controls passing; the original local deletion also failed its secure
retention assertion before the implementation changed.

This does not repair previously missing rows or already-created claims, implement cloud cycle
rollover, or revalidate compliance at every EDI export. Those require follow-up evidence-preserving
work. The permission follow-up above updates B01/B02; session and other launch findings remain open. No deployment or real-data
reconciliation was performed.

## Bottom line

The bounded clearinghouse importer is implemented for synthetic Demo/Testing use. I would not approve a general real-consumer, multi-user launch yet. The most important remaining work is authorization and billing-integrity hardening, not another feature.

This review found four existing defects through five repeatable synthetic tests. Those tests intentionally asserted secure behavior and failed, demonstrating the defects. They are separate from the ordinary passing regression suite. The new intake's review findings were corrected; the pre-existing platform defects below were surfaced, not silently changed outside this implementation's scope.

## Fix order

| Priority | Finding | Evidence and impact |
|---|---|---|
| 1 — standalone bypass closed; historical reconciliation pending | Deleting a never-attested overdue requirement removed a billing block (B03) | Both public deletion boundaries now refuse all persisted forms. Existing missing obligations and claims created before the fix still require controlled review; no dates are guessed or historical records silently rewritten. |
| 2 — ordinary consumer boundaries fixed in source | Capability removal did not consistently stop casework access (B02) | Reproduced and fixed across reviewed ordinary API/local consumer operations. Billing/Supervision/Admin remain independent. Global maintenance, direct SQL and session lifecycle are not covered by this closure. |
| 3 — implemented in source; approved rollout pending | Password/session revocation and account disablement (B05) | Reproduced and corrected with enabled state, mandatory security versions, password/reset revocation and administrator controls. Schema/client/server rollout, MFA and operations remain prerequisites. |
| 4 — fixed in source | Local login reconstructed permissions from the legacy role (B01) | Login preserves persisted capabilities and omits password verifiers; ordinary consumer services recheck the current database actor. |
| 5 — before claiming pre-submission prevention | Overdue notes can enter the supervisor queue through the API (B08) | Reproduced: direct submission persists Logged. Supervisor approval still rejects it; this finding is not proof that every invalid note becomes a payable claim. |
| 6 — before concurrent real billing | Overlap validation has a read-then-write race (B09) | Source finding, not dynamically reproduced. Serialize conflicting staff/day writes and prove the behavior using SQL Server. |
| 7 — before agency rollout | Privileged maintenance, direct database trust, distribution integrity and operations (B04/B06/B07/B10) | Local maintenance can operate globally; Local Production is not an API security boundary; signing, runtime SQL grants, restore/key recovery and operational controls require closure. |

Additional findings cover audit/amendment and legal-hold completeness, workload limits, password hash upgrade policy, CI/dependency governance, excess workforce disclosure and stale maintenance SQL. The detailed appendix provides affected code and practical remediation. No unauthenticated remote compromise was demonstrated; that is not assurance that none exists.

## What the new intake does

- Imports a bounded original 999, 277CA or 835 from Billing Submissions in API-backed synthetic environments.
- Matches authenticated-agency retained submissions automatically, including exact original controls/claim references, mode, interchange parties, charges, payee and supplied service references.
- Saves encrypted original evidence, immutable matches, financial observations and safe audit metadata in one transaction.
- Replays exact or re-enveloped duplicates without another payment/deposit; conflicting identities fail closed.
- Preserves receipt-only, accepted, rejected, partial, reversed, repeated-payment and needs-review distinctions.
- Protects file selection, upload credentials and delayed results across account switches.
- Leaves bank EFT unknown. An imported remittance is not proof that money arrived.

No live transmission, deployment, production activation, real database migration or real-consumer processing was performed. Existing user work was preserved.

## Still incomplete — do not advertise as finished

1. Production API/clearinghouse activation and actual vendor/payer acceptance. Source gates currently permit exact Demo/Testing identities only. A publicly hosted portal is not equivalent to approval for real-consumer signing; live hosting and configuration were not inspected.
2. Full X12/companion-guide coverage. One ISA/GS/ST is supported; TA1/997, multi-transaction interchanges, aggregate/provider-only responses, unsupported financial constructs, and same-advice reversal/replacement pairs are rejected. Originals rejected before import must remain in the agency's approved external intake process.
3. Audited rejected/corrected/void/resubmission workflows and reconciliation across generations. Multiple partial payments are retained conservatively, not automatically certified settled.
4. Authorized original-receipt review/download and recovery procedure. Raw evidence is encrypted and retained but currently has no biller-facing readback route.
5. Actual bank reconciliation and payment/offset resolution.
6. SQL Server migration/concurrency/deadlock rehearsal and runtime append-only grants. SQLite tests and EF guards do not establish production SQL behavior.
7. Purpose-specific key policy, backup/key-version recovery, alerts, restore drills, incident response, access/offboarding procedures and qualified privacy/billing review.
8. Production-readiness checks for signatures, chat, optional local AI and Carika. The appendix distinguishes positive controls from remaining work. eMAR is outside Sati's case-management scope.

## Validation and limits

Final validation results are recorded in the companion implementation handoff. The ordinary suite includes API authorization, encryption/rollback/idempotency, desktop account-switch tests and rendered WPF command binding, signature/portal tests and Carika tests. The native local-AI competence test is opt-in and was not run.

Three concrete mutation/regression checks establish that safeguards are actually tested: the old parser misread A7 and accepted invalid money; removing the desktop account-current guard failed two isolation tests; removing 277 service-reference matching failed the forged-reference test. All production code guards were restored.

The 99-migration chain passed its symbolic consistency checker. The new migration was reviewed as additive, with refusal to roll back populated receipt evidence. It was not applied to SQL Server.

The public NuGet advisory query covered direct and transitive packages in all 13 solution projects and reported no known vulnerable packages on this date. That means no advisories reported by that feed, not that dependencies are free of vulnerabilities. No dependency upgrades or new packages were necessary.

A bounded Git-history pattern scan examined 3,926 text blobs, skipping no oversized matching blobs. It found 114 password-pattern candidates, all in seven Demo/test documentation, test and smoke-script paths; no private-key, GitHub/OpenAI/AWS token, Azure key/SAS or SQL-password pattern hits were reported. Candidate values were not printed. This is a limited pattern scan, not a dedicated secret-scanner certification; historical/demo credentials must never be reused for production. Local secret files, binary history, cloud secret stores, build agents and deployed resources were not audited.

Source review included staff authentication/tenant boundaries, billing/compliance/note transitions, local direct-data paths, encryption and sensitive logging, migrations, signature portal, chat, local AI, maintenance, CI and installation. Tests used synthetic fixtures. Production infrastructure, vendor agreements, legal compliance, physical/device controls and a live penetration test remain outside this repository-only assessment.

## References for supported parsing and password guidance

A1 denotes receipt rather than acceptance; A2 denotes acceptance. See [X12 claim-status category codes](https://x12.org/codes/claim-status-category-codes). Claim-level 277CA tracing uses the original CLM01 as described in [X12 RFI 1926](https://x12.org/resources/requests-for-interpretation/rfi-1926-005010x222). These references inform the supported reader; they do not establish complete implementation-guide conformance or payer certification.

Current [OWASP password-storage guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) lists 600,000 PBKDF2-HMAC-SHA256 iterations; Sati uses 100,000. Introduce versioned hashes and an upgrade path, with measured login resource limits. This is security guidance, not a claim that a particular legal requirement mandates that number.

## Detailed evidence appendix

The following independent review records baseline source locations and subsequent intake corrections. Line numbers may shift after integration; the named files, methods and routes identify the affected code. “No advisory queries” in the reviewer's scope refers to that sub-review; the coordinator performed the dependency/history checks summarized above.

## Sati security inventory — September 10, 2026

Baseline source review for the requested post-development audit. Reviewed the isolated working copy at `work/sati-clearinghouse`; no live resources, real records, deployment state, real databases, credentials, or external mail were accessed. After this baseline, the coordinator authorized synthetic audit proofs, recorded below. No implementation edits, advisory-feed queries, or Git mutations were performed by this reviewer. Other agents are changing the intake in this same copy, so endpoint line numbers must be refreshed in the final report. Findings below concern the existing non-intake surface unless explicitly stated.

This is source evidence, not penetration-test or compliance certification. Historical audit statements were compared to current code rather than copied as present findings.

## Highest-priority confirmed source findings

### B01 — High: Local login discards persisted permissions

- Evidence: `Data/AuthService.cs:40-49` calls `User.Create` with role and agency but no persisted permission set. `Sati.Persistence/Models/User.cs:37-53` derives `Permissions` from the legacy role. This factory does not currently have the optional permissions argument the September 3 report assumed.
- Trigger: an administrator grants billing-only or another non-legacy permission combination but the persisted legacy role remains CaseManager/Supervisor/Admin; the user signs in through Local Production.
- Consequence: the desktop session receives role-derived access rather than the permissions selected by the administrator. Local methods that trust session permissions retain unauthorized capabilities, while stricter methods comparing session to database permissions deny otherwise legitimate access. This is both escalation and revocation failure.
- Concrete sensitive path: `Data/DhhsFormService.cs:128-136` loads a person using only owner ID and agency. `RevealSsnAsync` decrypts at lines 96-112 with no current CaseManagement check. Thus this is not merely incorrect tab visibility.
- Remediation: safe session projection with exact persisted permissions; revalidate local current identity and permissions in each authoritative service. Do not retain password hashes/salts in ordinary session projections.
- Historical finding: SATI-SEC-001 remains open.

### B02 — High: Several API routes ignore current capability and some omit tenant predicates

- `ValidatedActorFilter` in `Sati.Api/Security/TenantAccess.cs:105-151` reloads current permissions correctly, but the endpoint must actually use them. `OwnsPersonAsync` at lines 68-84 is a correct centralized own-caseload guard.
- Confirmed insufficient paths in `Sati.Api/Endpoints/ApiEndpoints.cs`: journal GET at 1836-1847 (owner only); journal PUT at 1859-1864 (owner + tenant, no capability); note PUT at 3940-3954; note DELETE at 4031-4036 (owner only); notes/year at 4121-4130 (owner only); abandonment at 4139-4148 (owner only); form deletion at 6127-6144 (owner only); form opened-date update at 6155-6180 (owner only). Person and contact operations in the same file also need comprehensive route-matrix verification; the original twelve-route count should not be reasserted without final enumeration.
- Trigger: remove CaseManagement while preserving client assignments, retaining an otherwise supported permission such as Billing. A valid/new JWT carries current validated Billing permissions, but these handlers still process owned client records.
- Consequence: continued PHI access and editing following intended revocation. Under inconsistent/stale cross-agency ownership, owner-only routes additionally fail to enforce the row agency boundary. This review did not demonstrate a normal API workflow that creates such inconsistent ownership.
- Remediation: add feature policies and centralized record-scope guard; test every operation after capability removal while assignments remain. Tenant predicates should apply to both the person and child rows where present.
- Historical finding: SATI-SEC-002 remains materially open; SATI-SEC-008 is best described as a structural risk with these concrete examples, not a separate demonstrated cross-tenant exploit.

### B03 — High: Missing un-attested compliance records can still remove billing blocks

- Evidence: `ApiEndpoints.cs:6110-6145` refuses deletion if an attestation exists, but otherwise performs an unaudited `ExecuteDeleteAsync`. `Data/FormService.cs:274-292` likewise hard-deletes owned forms without history.
- `Sati.Contracts/V1/BillingComplianceGate.cs:49-59` expressly ignores effective date and evaluates only supplied form rows. `EvaluateBillingWindow` at 80-91 similarly checks only existing rows. Empty input has no reason to block.
- Trigger: an overdue required form has no attestation history and is deleted through the exposed endpoint. Direct claim creation operates without a UI reload that might rebuild cycles. The synthetic proof below confirmed an initially blocked claim is accepted after the form is deleted.
- Consequence: loss of compliance obligation/history and potential billing acceptance after removing the record that caused the block. This is a stronger problem than opened-date editing.
- What changed since Sept 3: completion dates now require append-only attest/revoke; `UpdateForm` rejects completion-date overwrite (6172-6177); attested forms cannot be deleted; form concurrency/attestation protections exist. The old claim that *all* forms are freely overwritten/deleted is stale.
- Remediation: remove generic deletion or use reasoned/audited retirement; calculate expected requirements independently of rows and fail closed for missing required cycles. Verify transaction/race protection when checking history before deletion.
- Historical finding: SATI-SEC-003 is partially fixed, with the missing-requirement deletion exposure still open.

### B04 — High: Local maintenance remains globally scoped and exports PHI to Desktop

- Evidence: `Data/FormDueDateBackfill.cs:74-80,110-125` queries all people/forms without actor/agency; constructor receives context/settings only. Commit recomputes rows and saves without verifying that the new plan matches the dry-run content (only the passed old count is checked).
- `Data/FormBulkCompletion.cs:40-44,51-59,95-126` also loads all forms; no actor, current role, or tenant validation. It now emits system attestations/audit rows, but identifies actor as system/-1 rather than the initiating staff member. Dry-run latch is not an authorization boundary.
- `App.xaml.cs:470-471` still registers both; Settings invokes them (UI visibility is not service authorization).
- `Data/FormBulkCompletion.cs:189-197` writes consumer full names, form types and dates to Desktop. `Data/FormDueDateBackfill.cs:268-269` writes IDs/dates to Desktop.
- Trigger/consequence: invoking retained commands or service methods can rewrite obligations across the local database; even an authorized dry run exports sensitive information to a location that may synchronize through consumer cloud storage. This is local-only, not an exposed API utility.
- Remediation: retire completed maintenance tools or make them authorized, tenant-scoped, explicitly reviewed operations with protected/redacted report locations and identified audit actor.
- Historical finding: SATI-SEC-004 remains open; its assertion of no bulk audit is partially stale.

### B05 — High, historical baseline: password changes/resets did not revoke bearer sessions

Status: password/session revocation and disablement are implemented in the September 11 source
follow-up above. This evidence predates that fix; deployment, MFA and broader operations remain open.

- Evidence: `Sati.Api/Security/TokenIssuer.cs:35-45` includes random jti/auth time/database generation but no user security version or session record. `ApiEndpoints.cs:1268-1299` renews using auth time/user existence. `ApiEndpoints.cs:1408-1425` password change writes hash/salt only. `ServerUser` in `Sati.Api/Data/ApiDbContext.cs` has no active/disabled/security-stamp field. `ApiAuthenticationOptions` defaults to 30-minute JWT and 720-minute renewal window; Program permits maximum session 1,440 minutes.
- Trigger/consequence: a stolen JWT remains usable and renewable after password change until original authentication age limit. Role/agency changes can invalidate old claims, but there is no reliable dedicated user/session revocation operation. Empty permission sets are not a supported disable mechanism.
- Remediation: inactive state + security version checked every request and renewal; revoke on password reset and offboarding; session inventory/revocation; privileged MFA as deployment requirement.
- Historical finding: SATI-SEC-005 remains open.

### B06 — High deployment boundary: Local Production is a direct-database application

- Evidence: `Data/DataEnvironment.cs:35-64` gives only Demo an API; Production resolves a connection string. `App.xaml.cs:463-501` registers local services and EF, and lines 252-273 run local migrations before login. `Data/DpapiKeyWrapper.cs:24-36` accurately describes protection against a copied database, not software running as that Windows account.
- Trigger/consequence: anyone able to run another process as the Windows/SQL owner can read or modify records outside Sati login/audit controls and can decrypt DPAPI-protected SSNs as that user. This is the intended single-operator local architecture; it cannot supply a multi-user agency security boundary merely by adding Sati usernames.
- API-mediated Production is also unfinished: API code/config references SatiDemo; `ValidatedActorFilter` validates only a Demo database marker. No claim that hosted Production works should be made from API maturity alone.
- Remediation: make launch supported architecture explicit; complete approved API-mediated Production and separate least-privilege runtime/migration identities before distributing hosted agency access. Do not remove existing local use without user direction.
- Historical finding: SATI-SEC-007 remains a deployment constraint, not newly discovered remote compromise.

### B07 — High release-integrity gap: Installer authenticity is not established

- Evidence: `installer/README.md:75` says installers are not code-signed. `installer/Sati.LocalBootstrap/Program.cs:21-42` extracts embedded payload then starts PowerShell with ExecutionPolicy Bypass. No outer Authenticode/signed manifest verification is present. Adjacent SHA-256 identifies content but is not proof of publisher if replaced together.
- Trigger/consequence: attacker who can replace distributed installer/adjacent checksum can deliver arbitrary code to a PHI workstation. Actual distribution permissions and live signing were not inspected.
- Remediation: protected signing service/certificate, timestamped executables and installer, verified signed manifest, authenticated distribution and release acceptance checks.
- Historical finding: SATI-SEC-006 remains open at source/package-process level.

## Additional confirmed integrity and availability gaps

### B08 — Medium: Compliance gate is bypassable before supervisory submission

- Evidence: POST `/notes` at 3890-3927 and PUT `/notes/{id}` at 3931-4015 check ownership, basic values/status and service time, but never load/evaluate consumer compliance. `NoteWorkflow.cs:39-40` permits a caller to author Logged. The actual compliance read occurs on supervisory approval at 1661-1672.
- Trigger: direct authorized API caller sends `Status=Logged` for an overdue consumer (or updates an editable note to Logged).
- Consequence: invalid notes reach supervisory queue despite desktop pre-submission gating. This is not proof a claim becomes payable; supervisor and claim checks remain separate protections.
- Remediation: authoritative transition-to-Logged validation shared with UI; return actionable held/blocked response rather than trusting the caller's chosen status. Preserve allowed documentation as unbillable instead of losing it.
- New baseline finding; reproduced by the synthetic proof below.

### B09 — Medium: Overlap prevention has a simultaneous-write gap

- Evidence: API queries day conflicts at 3905 then saves at 3927 without transaction/serialization around both; PUT does same at 3982/4015. `Data/NoteService.cs:22-25` mirrors query-then-insert. `FindServiceTimeProblemAsync` at 6902-6913 reads existing records; no database exclusion constraint or user/day application lock was found.
- Trigger: two requests for the same CM/day both read no conflict before either saves, or two different notes are simultaneously moved into overlapping slots.
- Consequence: both overlapping intervals can persist despite each passing validation. Revision tokens on individual records cannot prevent the two-record race. Batch approval rechecks overlap when MaximumUnits is supplied; ordinary approval does not run that branch (1675-1694).
- Remediation: serialize by tenant/CM/service-date or equivalent database transaction locking around overlap read/write, including moves across dates; deterministic two-request regression with SQL Server.
- This source-level race is documented in AGENDA; no dynamic reproduction yet.

### B10 — Medium: Audit and retention controls are partial

- Positive change: `ApiDbContext.EnsureAuditEventsAreAppendOnly` now rejects EF update/delete of audit/person-version/form-attestation/document-ack/billing exchange records; signature/chat protections are called too. The historical claim that no code-level append-only enforcement exists is stale.
- Remaining: `ExecuteUpdate/Delete`, raw SQL, or broad runtime SQL permissions do not traverse that ChangeTracker guard; actual runtime grants are unverified and `OPERATIONS.md:350` still requires them. Note create/update/delete API paths do not record ordinary create/edit/delete history (reassignment alone is audited); abandonment at 4143-4148 changes status/revision without per-note audit event. The old note value is not kept as a version through return/correction/resubmit.
- Legal holds exist and fail closed for person deletion (`Sati.Api/Security/ApiLegalHoldRegistry.cs:16-33`); the old statement that no registry exists is stale. Hold release endpoint at 706-741 permits one admin to release without second reviewer or required nonempty reason. It does not provide broad record/blob/backup/chat preservation semantics.
- Remediation: complete clinical amendment/version and audit coverage, tested append-only SQL permissions, dual-control release and scope-aware preservation policy. No automatic purge should be added merely to satisfy a retention checkbox.

### B11 — Medium: Authenticated workload remains insufficiently bounded

- API rate limiting in Program protects login and chat-post only; authenticated journal, large note, reports/PDF, and other expensive operations have no common per-user/concurrency limit.
- `ValidateNote` permits 1,000,000-character narrative; journal PUT accepts unrestricted string before storage/versioning; some other inputs are explicitly bounded (assessment 4,000,000 characters, screenshot/logo upload checks). Do not describe all input as unbounded.
- Kestrel's defaults still apply; this is not unlimited HTTP request bytes. However accepted repeated expensive requests can exhaust a small shared instance or inflate PHI/version storage. Login attempts are held in-process, so scaling across instances divides protection.
- Remediation: route-specific size/page/row budgets, streaming/read caps before deserialization where useful, authenticated per-user and expensive-operation concurrency limits, shared login abuse state.
- Historical SATI-SEC-010 remains material.

### B12 — Medium/defense-in-depth: Credential/build-chain governance

- `Data/PasswordHasher.cs:12` and `Sati.Api/Security/PasswordVerifier.cs:10` use PBKDF2-SHA256 with fixed 100,000 iterations; no versioned upgrade-on-login metadata. The coordinator checked the current official [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html), which recommends 600,000 iterations for PBKDF2-HMAC-SHA256. Sati's setting is one-sixth that recommended iteration count; this is security guidance, not a claim about a specific statutory minimum. A lower work factor reduces the cost of offline guessing if hashes are stolen; no password cracking was performed. Introduce versioned hash parameters and a measured upgrade-on-login path rather than changing the constant in a way that invalidates existing hashes.
- `.github/workflows/build.yml:13,15,17` references mutable major action tags and rolling .NET 10.0.x, restores without a locked package manifest, builds/tests all projects, and runs portal UI tests. No current CI vulnerability/secret scanning, SBOM, provenance/signing or dependency update workflow was found. These are governance gaps; they do not establish a presently vulnerable dependency.
- No current advisory-feed or secret-history verdict belongs to this sub-review. September 3's package counts/versions are historical only.

### B13 — Low: Workforce disclosure and unsafe/stale maintenance artifact

- `ApiEndpoints.cs:1314-1325` lets each authenticated agency user fetch full `UserProfileDto` for all agency users through `/users/switchable`, not just display-name/identifier. Check exact DTO fields and narrow to what switch UI needs; same-agency is not always an adequate audience for phone/email/permission information.
- `SQLQuery2.sql:1-2` remains an unconditional `UPDATE Forms SET IsCompliant = 1`. IsCompliant is now computed, so current schema should reject this command rather than silently bypass compliance. Remove/label this stale SQL foot-gun; do not report a current successful unrestricted mutation without reproducing it on an isolated schema.
- Response hardening: main API now sends nosniff/referrer-policy; ordinary PHI responses still lack a general no-store rule. Portal has much stronger no-store/CSP/HSTS/CSRF handling. Context matters because API is principally a bearer desktop endpoint, not a cookie-authenticated public website.

## Newer feature surfaces: enabled versus intentionally incomplete

- **Signature portal:** `Sati.Signatures/SignatureInfrastructure.cs:21-29` allows only exact Demo/SatiDemo or Testing/SatiApiTests and requires explicit Enabled. Production cannot be activated by setting Enabled alone. Unconfigured storage/key providers fail closed. Portal middleware requires HTTPS/exact host, bounded 8 KiB JSON POST, antiforgery token, Origin check when present, no-store, restrictive CSP/referrer policy, and safe error wording. Protected actions and storage/IAM must still be audited after final changes; no live hosting, email deliverability, signatory authority, accessible PDF, restore, or consent/retention claims verified. This is a gated feature, not proof of live signing readiness.
- **Chat:** `Sati.Api/Infrastructure/ChatInfrastructure.cs:19-24` uses same Demo/Testing gate and explicit Enabled. No local Production service. Room membership plus consumer authorization are used; WebSocket count is bounded and chat-post has a 30/min user limiter. Retention, hold/discovery coverage, restricted-record handling and account disablement remain prerequisites described in the guide/AGENDA. No automatic all-agency PHI room or production bypass was found in this baseline.
- **Local AI:** `appsettings.template.json:23` defaults Enabled false. `FoundryLocalCaseNoteFormatter.cs:41-51` requires feature enabled; lines 77-86 require successful model unload on consumer switch; fact packet excludes history, uses local native model; note UI at 1735-1749 rechecks fingerprint and current user before explicit acceptance. No silent cloud narrative fallback observed. Model alias/download is dynamic, source/generated/final acceptance provenance is not persisted as a server audit record, and runtime disk/swap/crash/telemetry exposure is not proven by source tests. Native model/license/update/telemetry/device review remains unfinished if agencies enable it.
- **Notes and approved records:** logged/approved records are locked; only draft-like states can delete (`NoteWorkflow.cs:93-97`); returned records permit corrections. Approved-note amendment path still absent. This avoids silent approved edits but leaves lawful correction workflow incomplete.
- **Check requests:** immutable published snapshots and named local publication exist; submission/assigned-supervisor approval/return, requester revision, chairperson approval and finance processing remain future workflow per AGENDA 5439 onward. Printed signature labels do not mean actual electronic approval exists.
- **Cross-agency OADS/Karuna sharing/mobile:** designs and platform branding are not proof of those workflows. eMAR is outside Sati scope. API-mediated Production, Carika Android/iOS availability, shared-account security and vendor agreements need explicit launch scope.
- **Operations:** source contains backup-before-local-migration safeguards and schema identity checks; the current docs still require runtime grants, external alerting, restore drills, incident response and retention evidence. Do not turn an unchecked doc line into a statement about what is actually deployed; inspect deployment evidence separately with authorization.

## Suggested final audit validation

1. Synthetic API regression: revoke CaseManagement while retaining assignment and Billing; test every clinical read/write route.
2. Synthetic note transition test: overdue PCP, POST or PUT Logged; verify current bypass and then intended fixed behavior if in scope.
3. Synthetic deletion/gate test: delete never-attested overdue requirement then request claim; bypass UI reload and verify required-cycle handling.
4. Token test: issue token, reset/change password, use and renew old token; account disable behavior is not available today.
5. SQL Server concurrency test for overlapping note inserts/updates; SQLite/in-memory success would not validate SQL Server lock semantics.
6. Production-equivalent identity test: attempt SQL UPDATE/DELETE on append-only histories as runtime principal; source guard alone is insufficient.
7. Review final new response intake independently: actual raw size bound, X12 references, tenant and environment, duplicate/concurrent receipts, atomicity, safe retention permissions, unknown/ambiguous/unsupported documents, no legacy bypass route, no production enablement.

No critical unauthenticated remote compromise was demonstrated in this inventory. High findings are practical authorization, integrity, session, deployment-boundary and supply-chain issues. Absence of another finding does not establish the corresponding feature is secure.

## Synthetic reproducibility evidence

Executed the five secure-behavior assertions in the appendix using the existing in-memory SQLite `SatiApiFactory`, real HTTP handlers, real JWT issue/validation, and synthetic data only. Command:

```powershell
$env:DOTNET_CLI_HOME = 'C:\Users\SatiLogica\Documents\Codex\2026-09-10\rig\work\dotnet-home'
dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~SecurityAuditProofTests --logger 'trx;LogFileName=security-audit-baseline.trx' --results-directory C:\Users\SatiLogica\Documents\Codex\2026-09-10\rig\work\security-test-results
```

Build succeeded (three existing unrelated analyzer/nullability warnings). Result: **5 failed as expected, 0 passed, 0 skipped; approximately 7 seconds**. Failures were assertion failures for the intended unsafe behaviors, not startup/environment errors:

| Proof | Secure expected behavior | Observed behavior |
|---|---|---|
| Password change | Old token denied for clinical read and renewal | Both returned HTTP 200 after a successful password change |
| CaseManagement revoked, journal | Clinical read denied despite retained caseload assignment | Journal returned HTTP 200; central caseload control correctly denied |
| CaseManagement revoked, annual notes | Clinical read denied despite retained caseload assignment | Year notes returned HTTP 200; central caseload control correctly denied |
| Overdue requirement deletion | Required compliance gate survives deletion, or deletion is refused | Claim returned HTTP 400 before deletion; delete succeeded; same claim returned HTTP 200 afterward |
| Overdue note submission | Overdue consumer cannot enter supervisory queue | POST persisted `Logged`; supervisory-approval control correctly returned `compliance_required` |

The password and permission changes were restored in `finally` blocks. The test process used no live database. Temporary test source was removed from the build after capturing the proofs, to avoid leaving intentionally failing tests in the feature branch or treating insecure behavior as a successful regression. Exact runnable source is preserved at `work/security-audit-proof-tests.cs.txt`; detailed runner evidence is `work/security-test-results/security-audit-baseline.trx`. These five failures must not be blended into the final green regression-suite count; they are independent evidence that the pre-existing vulnerabilities remain unresolved.

## Independent review of the new intake (after implementation)

Read-only review covered `ClaimResponseIngestion`, both response endpoints including the legacy period route, new receipt/match EF configuration, migration, changed outbound generation, X12 parser, financial progress reducer, encryption owner, and the new tests. No additional build/test run was performed by this reviewer during this pass; final test execution belongs to the coordinator. The code was still under active correction during review, and the items below were checked again in source after the owning agents addressed them.

### Findings corrected during this review

- **Evidence rollback:** the first migration version dropped populated encrypted receipt/match history. `AddClearinghouseResponseIntake.Down` now begins with a SQL Server guard that refuses rollback when either table contains evidence. Its schema changes are otherwise additive on upgrade. This guard is intentionally SQL Server-specific, which matches deployment; no SQL Server execution proof was performed by this reviewer.
- **277 claim/service mismatch:** the first matcher checked 835 service-line references but omitted references supplied in 277 acknowledgements. `MatchAsync` now checks both remitted and acknowledged line references against the exact retained submitted claim.
- **Invented acceptance:** receipt-only A0/A1 or unresolved 277 categories initially reduced to `PartiallyAccepted`. The reducer now records explicit `ClaimReceived` or `ClaimNeedsReview` states and preserves genuinely rejected/accepted distinctions.
- **Repeat financial advice:** another 835 with a new payment identity could initially report the already-paid claim as settled again. The implementation now queries prior outcomes for the matched generation/claim and classifies repeated financial advice as `RemittanceNeedsReview`. Original financial evidence remains append-only.
- **Correlation history append:** the initial immutable guard prohibited changed/deleted matches but allowed a new match to be appended to an existing receipt. It now allows new matches only while the matching parent receipt is also being inserted. Outbound `EdiGeneration` modification/deletion is guarded in both API and local EF models.
- **Overpayment wording:** the parser initially classified positive payment greater than billed as `PartiallyPaid` and described it as below billed charges. The parser owner corrected this to `NeedsReview` with overpayment wording, and added a specific test. Unsupported 835 financial segments such as MOA/MIA/TS2/TS3 now fail explicitly rather than being silently ignored.

### Controls observed in final source

- Authentication and current Billing permission on both HTTP routes; the auto-match endpoint never accepts a billing-period authority. The legacy parameter is checked as an assertion against resolved match results, including idempotent replay.
- Exact test/environment gate: only Demo/SatiDemo or isolated Testing/SatiApiTests; Production interchanges refused. This is an active source gate, not a claim about live deployment configuration.
- Sender/receiver identity and qualifier reversal against retained 837; exact original group/ST controls for 999; exact CLM trace, billed amount when supplied, service-line references and payee NPI for claim/remittance matching; ambiguous or foreign matches abort the entire file.
- Bounded ASCII file/transaction parser with one interchange, group and transaction; no real network transmission or bank write occurs on import.
- Receipt, matches, outcomes, deposits and import audit commit in one serializable transaction. Raw/semantic/document/payment identities have database unique indexes; replay adds no duplicate financial effects. Current tests cover exact/re-enveloped duplicates, concurrency, rollback on a failing deposit insert, tenant/mode mismatches, and encryption binding, but SQLite tests alone cannot validate SQL Server lock/deadlock behavior.
- Original response retained with AES-GCM envelope encryption and a random per-receipt data key; authenticated binding includes agency, receipt ID, raw hash and parser version. No caller-provided filename is retained or echoed. Wrapper unavailability prevents import. The desktop reads exact ASCII bytes within its limit and clears the temporary byte buffer.
- Out-of-order lower-stage acknowledgements do not replace already recorded financial progress. Denials/reversals/repeated advice require review. A reported remittance is explicitly not a verified bank deposit.

### Remaining new-feature limitations to surface

1. **Restricted pilot capability, not live clearinghouse readiness.** Production remains disabled. Office Ally/payer validation, real transport, sender/receiver conventions, production credentials/key roles, approved deployment and operational evidence are still required. Matching header fields and a known trace identifies the related claim; it does not cryptographically authenticate the file's external origin.
2. **Deliberate parser subset.** Exactly one ISA/GS/ST; TA1/997, multi-transaction interchanges, provider-only adjustments, unsupported debit/financial constructs and same-advice duplicate claim references (including reversal/correction pairs) are refused. Numeric/control formatting and allowed response layouts need vendor certification. Safe rejection must not be presented as comprehensive X12 support.
3. **Cross-attempt financial reconciliation is unfinished.** Prior-outcome detection is keyed to generation/claim reference. Regenerating the same underlying note gives a new claim reference; this intake does not resolve duplicate payment/correction/replacement/void relationships across those submissions. The original approved service must gain a controlled claim-attempt lifecycle before general live billing.
4. **No automatic release of a review state.** A denial, reversal, repeated payment or ambiguous lifecycle requires staff reconciliation. Separate partial remittances are retained; there is no complete aggregate resolution that proves all expected claims/amounts have been settled. Preserve the conservative flag until an audited reviewer workflow exists.
5. **Receipt review tooling remains limited.** The encrypted original is durably stored and decryptable by the shared protector, but no narrowly authorized receipt readback/download UI or controlled operational recovery/export procedure was observed. Billing staff should not be sent to direct SQL as the normal means of reviewing imported evidence.
6. **Runtime SQL grants/recovery remain unverified.** EF guards do not constrain a database owner or raw SQL. Test append-only permissions and raw receipt/key recovery under the actual runtime and operations identities. Key-version preservation is necessary to recover encrypted originals; source-only AES tests do not establish backup usability. The receipt protector currently shares the environment key wrapper configured under `Ssn:KeyUri`, so the import requires that configuration even for an agency not storing SSNs; separate purpose-specific production key policy remains an operations decision.
7. **Dedicated import rate/concurrency limits remain a platform gap; body limits are implemented.** `Sati.Api/Program.cs:216-231` now caps both response-import routes before model binding at `2 MiB * 6 + 1024` wire bytes, accounting for JSON's six-byte Unicode escape representation. It rejects an oversized declared Content-Length and configures the server request-body feature to bound reads where available. The decoded X12 and desktop file reader separately enforce the 2 MiB content limit. There is still no dedicated per-user expensive-import concurrency/rate policy; that remaining work belongs with B11's availability hardening.
8. **No bank reconciliation.** `EftDepositAmount` remains null. Import records payer advice and maintains signed adjustment totals; it does not prove money reached the bank, issue refunds, resolve offsets across advice files, or automatically resubmit claims.

The corrected intake findings should be reported as fixed in this work, not left in the outstanding vulnerability count. The pre-existing defects demonstrated by the five audit proofs above remain outstanding unless the coordinator subsequently implements and verifies a fix.
