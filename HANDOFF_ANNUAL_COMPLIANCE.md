# Annual compliance correction — continuation checkpoint

Branch: `codex/clarify-project-notes`. User requests commits and pushes at useful checkpoints so Claude can continue if needed.

## Scope and authority

Implement the clarified annual compliance and service-date billing rules. Source changes and commits/pushes are authorized. No deployment or operational database migration has been performed. Do not access Production PHI. Read the active September 14 sections of ARCHITECTURE.md, DECISIONS.md, and AGENDA.md for the detailed rules and remaining integration work.

## Saved work

- `f1db749`: main implementation checkpoint.
- `0c90675`: Josh's manual commit, verified on GitHub. Contains additional missing-obligation projections, draft submission revalidation, transactional policy application, and superseding recovery migration.
- Current follow-up corrects the snapshot: ClaimLines.NoteId stays unique; BillingComplianceRecoveryNotes.NoteId becomes nonunique, with decision+note uniqueness retained. The previous snapshot accidentally changed the wrong NoteId index. The runtime model and migration were already targeting recovery correctly.
- Updates synthetic tests that assumed absent CA/PCP rows counted as compliant; duplicate repair tests explicitly isolate review requirements.

## Verification

- Full API suite: 739 passed at the prior checkpoint.
- First full desktop run: 1,970 passed, 74 failed, one skipped. Many failures came from a temporary build directory one level too shallow for source-location tests; DPAPI tests also require the Windows user profile outside the sandbox. Remaining behavioral and snapshot failures are being corrected.
- Correct rerun command (synthetic tests only):
  `dotnet test Sati.Tests/Sati.Tests.csproj --no-restore -p:BaseOutputPath=C:\Users\SatiLogica\source\repos\heschides\Sati\.test-artifacts6\bin\ -p:SelfContained=false --logger "trx;LogFileName=compliance-full.trx"`
- Use an isolated output folder because the running Sati app can lock normal output binaries. Preserve the extra `bin` depth so source-based tests resolve the repository correctly.

## Next work

1. Record the corrected full desktop result and fix remaining real failures without weakening billing assertions.
2. Run signature/portal tests where relevant to the signature-attestation bridge.
3. Update stale active architecture text: annual packet exact-target lookup is already implemented; clinical approval is separate from billing eligibility; recovery can be superseded by another immutable decision after facts change; missing required form/release rows are synthesized from annual/assignment facts for billing.
4. Review unchecked active AGENDA integration items honestly before declaring completion. Several fixed-release/artifact-target compatibility paths remain listed; do not call these complete based only on passing suites.
5. Commit and push checkpoints to origin's existing branch. Never apply migrations or deploy as a side effect.
