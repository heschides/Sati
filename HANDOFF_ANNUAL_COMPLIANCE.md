# Annual compliance correction — continuation checkpoint

Branch: `codex/clarify-project-notes`. User requests commits and pushes at useful checkpoints so Claude can continue if needed.

## Scope and authority

Implement the clarified annual compliance and service-date billing rules. Source changes and commits/pushes are authorized. No deployment or operational database migration has been performed. Do not access Production PHI. Read the active September 14 sections of ARCHITECTURE.md, DECISIONS.md, and AGENDA.md for the detailed rules and remaining integration work.

## Saved work

- `f1db749`: main implementation checkpoint.
- `0c90675`: Josh's manual commit, verified on GitHub. Contains additional missing-obligation projections, draft submission revalidation, transactional policy application, and superseding recovery migration.
- `9c6c224` (pushed) corrects the snapshot: ClaimLines.NoteId stays unique; BillingComplianceRecoveryNotes.NoteId becomes nonunique, with decision+note uniqueness retained. The previous snapshot accidentally changed the wrong NoteId index. The runtime model and migration were already targeting recovery correctly.
- Updates synthetic tests that assumed absent CA/PCP rows counted as compliant; duplicate repair tests explicitly isolate review requirements.

## Verification

- Full API suite: 739 passed at the prior checkpoint.
- First full desktop run: 1,970 passed, 74 failed, one skipped. Many failures came from a temporary build directory one level too shallow for source-location tests; DPAPI tests also require the Windows user profile outside the sandbox. Remaining behavioral and snapshot failures are being corrected.
- Correct full desktop rerun passed: 2,044 passed, zero failed, one optional native-AI test skipped. Snapshot and all updated behavioral tests pass. Signatures: 119/119 passed. Portal: 8/8 passed. Signature and portal suites required escalation for their local output/data-protection access; sandbox portal failures were HTTP 503s and disappeared in the authorized rerun.
- Correct rerun command (synthetic tests only):
  `dotnet test Sati.Tests/Sati.Tests.csproj --no-restore -p:BaseOutputPath=C:\Users\SatiLogica\source\repos\heschides\Sati\.test-artifacts6\bin\ -p:SelfContained=false --logger "trx;LogFileName=compliance-full.trx"`
- Use an isolated output folder because the running Sati app can lock normal output binaries. Preserve the extra `bin` depth so source-based tests resolve the repository correctly.

## Next work

1. Review unchecked active AGENDA compatibility/rollout items honestly before declaring the entire rollout complete. PersonSaveRules already excludes generic release forms, local person creation/caseload loading and API assignment changes reconcile exact releases; the checklist item grouping generation and presentation is partly stale. Verify presentation before closing that combined item.
2. Safety Plan supports selected annual targets/configured availability, DHHS selects exact release obligations, and annual packet persistence uses exact targets. Annual Documents still suggests a target using its separate configured packet window (default 30 days). Underlying attestation does not require a packet. Decide whether the remaining packet-window/UI compatibility cleanup belongs in this correction or later rollout; do not silently change the user's clinical deadlines.
3. Core synthetic suites are now green. Architecture/decision text has been corrected for exact packet identity, clinical approval, repeat recovery, missing-obligation projection, atomic policy changes, and draft submission revalidation.
4. Commit and push checkpoints to origin's existing branch. Never apply migrations or deploy as a side effect. Migration rehearsal needs an approved non-production copy, and release requires separate authorization.
