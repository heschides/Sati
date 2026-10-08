# Test execution ledger — October 8, 2026

Source: `a1af92129f60728a0bbcf0dd27c42190644b1e3b` on `master`.

Tests used their inspected synthetic/disposable fixtures. No working Sati database, deployed SQL data, real account, mail provider, clearinghouse, or client PHI was accessed. No source fixes or regression mutations were performed. All test binaries were freshly built from source; `--no-build` runs followed those builds.

## Build and runner conditions

Initial `dotnet test --no-restore`/ordinary build attempts failed with no reported MSBuild error details. Sequential builds using `--disable-build-servers -m:1 -p:UseSharedCompilation=false --no-restore` succeeded for each test project. API dependency build reported 7 warnings and desktop dependency build 11 warnings; these counts overlap shared dependencies and are not a full solution warning count. Other three test-project builds reported zero warnings/errors. The private SQL harness built/restored its Release API dependencies separately.

Two default-sandbox runs aborted after VSTest could not connect to its child testhost within 90 seconds. Their `api.trx` and `desktop.trx` do not establish test results. Approved test-only execution outside that communication restriction completed the substantive runs below. These were not application test failures.

## Executed results

| Project/run | Passed | Skipped | Failed | Total | Result artifact |
|---|---:|---:|---:|---:|---|
| Sati.Api.Tests Debug | 1,094 | 25 | 0 | 1,119 | `test-results/api-verified.trx` |
| Sati.Tests Debug | 2,949 | 6 | 0 | 2,955 | `test-results/desktop-verified.trx` |
| Sati.Signatures.Tests Debug | 119 | 0 | 0 | 119 | `test-results/signatures.trx` |
| Sati.Portal.Tests Debug | 8 | 0 | 0 | 8 | `test-results/portal.trx` |
| Carika.Tests Debug | 4 | 0 | 0 | 4 | `test-results/carika.trx` |
| **Five-project totals** | **4,174** | **31** | **0** | **4,205** | All of the preceding artifacts |
| Private SQL API selection Release | 34 | 0 | 0 | 34 | `../TestResults/IsolatedSqlServer/Api/Joshu_LONGCHENPA_2026-10-08_10_56_45_net10.0.trx` |
| Portal Node page harness | 9 | 0 | 0 | 9 | Tool execution output; `Sati.Portal.Tests/portal-ui.test.cjs` |

The API run lasted 3 minutes 51 seconds, desktop 15 minutes 27 seconds, signatures 6 seconds, portal C# 5 seconds, Carika 1 second, SQL selection 1 minute 11 seconds, and Node approximately 221 milliseconds. The broad desktop run was slow and consumed CPU but completed successfully; no hang cause was established.

The SQL selection contains cases also present in the normal API run. Its result cannot be added to five-project totals as distinct tests. Default skipped theories may be counted differently when expanded with an enabled provider; do not compare raw totals across gates without examining the cases.

## Reproduction commands

In each default project test process, `SATI_RUN_SQLSERVER_TESTS=0` and `SATI_RUN_LOCAL_AI_MODEL_EVAL=0` were set. The principal commands were:

```powershell
dotnet build Sati.Api.Tests/Sati.Api.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal
dotnet build Sati.Tests/Sati.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal
dotnet build Sati.Signatures.Tests/Sati.Signatures.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal
dotnet build Sati.Portal.Tests/Sati.Portal.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal
dotnet build Carika.Tests/Carika.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -v minimal

dotnet test Sati.Api.Tests/Sati.Api.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=api-verified.trx' --results-directory assessment-working/test-results -v minimal
dotnet test Sati.Tests/Sati.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=desktop-verified.trx' --results-directory assessment-working/test-results -v minimal
dotnet test Sati.Signatures.Tests/Sati.Signatures.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName!~BuildsSyntheticEvidencePackageForVisualValidation' --logger 'trx;LogFileName=signatures.trx' --results-directory assessment-working/test-results -v minimal
dotnet test Sati.Portal.Tests/Sati.Portal.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=portal.trx' --results-directory assessment-working/test-results -v minimal
dotnet test Carika.Tests/Carika.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=carika.trx' --results-directory assessment-working/test-results -v minimal

./scripts/Test-IsolatedLocalDb.ps1 -ApiOnly
node --test Sati.Portal.Tests/portal-ui.test.cjs
```

The signature filter matched no exclusion; all 119 discovered signature cases executed, including synthetic package output. It should not be described as a reduced suite. Some existing tests write synthetic QA output in their established LocalAppData directories; no real working records were queried/copied.

The fully inspected private SQL harness generated `SatiSqlTests_9f63f7ecece84a968b45a59d6756b72f`, enabled SQL only for its owned run, restored its prior process environment, stopped and deleted that exact instance afterward. It never used the shared working MSSQLLocalDB instance. The harness output confirmed both stop and deletion.

That selection exercised joined billing outcomes and lost-successful-response/host-restart recovery, real SQL service-time and billing-submission coordination, amendment migration/serialization guards, synthetic fixture safety, reset/worker coordination, single polling/request budget and schema compilation. It did not execute all optional assessment, payer/governance migration suites, cloud PITR, deployed grants, live vendor transport or real device installer acceptance.

No test was added for the new source-inspected fresh-original submission, intervening form-revocation dispatch, generic note-create ambiguity, missing API note audit or arbitrary unhandled-log sentinel scenarios. Those remain explicitly unexecuted findings. No regression test was claimed to fail against an unfixed implementation.
