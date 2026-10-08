# Read-only Demo deployment evidence — October 8, 2026

Repository under review: `a1af92129f60728a0bbcf0dd27c42190644b1e3b`.

These are bounded Azure control-plane metadata observations made during this assessment. No SQL records, application settings, connection strings, credentials, issued tokens, or PHI were inspected. No cloud resource, firewall rule, database, or application setting was changed. Azure CLI used its existing sign-in session; its initial sandbox invocation failed because its normal session-cache file was not writable. Approved read-only invocations succeeded afterward.

Resource pins were taken from `scripts/Invoke-DemoRestoreVerification.ps1:39–45` and deployment scripts: resource group `rg-sati-demo`, SQL logical server `sati-demo-satilogica-central`, database `SatiDemo`, API app `sati-demo-api-satilogica`, plan `asp-sati-demo-central-f1`.

| Read operation | Observed result |
|---|---|
| `az sql db show`, projected service/backup fields | Online; Basic tier, capacity 5; current/requested backup redundancy Local; zoneRedundant false; readScale Disabled; autoPauseDelay and minCapacity null. |
| Same operation, earliest restore metadata | `2026-10-01T14:51:13.622339+00:00`. This is availability metadata, not a successful restore. |
| `az sql db str-policy show` | retentionDays 7; diffBackupIntervalInHours 12. |
| `az sql db ltr-policy show` | weekly/monthly/yearly retention `PT0S`; weekOfYear 0. |
| `az sql db replica list-links` | Empty list. |
| `az sql failover-group list` on the named server | Empty list. |
| `az webapp show`, safe projection | Running; HTTPS-only true; SystemAssigned identity; expected F1 plan. Identity permissions were not queried. |
| `az webapp config show`, safe projection | alwaysOn false; minimum TLS 1.2; webSocketsEnabled false. |
| `az appservice plan show` | F1, Free, Central US; reported capacity 0. |
| Initial `az sql server firewall-rule list`, names only | Temporary `datt-workstation-20261007` rule was present, alongside API and refresh outbound rules. Rule addresses were deliberately not requested. No removal was attempted by the assessment. See the follow-up verification below. |
| `az resource show` for exact planned watchdog rules | Both `sati-demo-watchdog-finding` and `sati-demo-watchdog-missing` returned ResourceNotFound. |
| `az resource list` in Demo group, restricted to scheduled query rules, metric alerts and action groups | Only `Application Insights Smart Detection` action group returned. This does not prove absence of other alert types or monitors in another resource group/service. |
| `az resource show` for refresh Application Insights component | Component RetentionInDays 90 and a linked default Central US Log Analytics workspace. Effective table/workspace retention, ingestion, access control and alert delivery were not verified. |

No restore was initiated, backup was exported, live application log was downloaded, or vendor call was made. These observations establish the listed current Demo settings only; they do not establish future Production deployment, application availability targets, recovery correctness, SQL permissions, encryption/key custody, external monitoring, or receipt of alerts by a named human.

## Follow-up: temporary rule removal verified

On October 8, 2026, after Josh reported removing the rule, the following read-only query completed successfully (exit code 0) and returned `[]`:

```powershell
az sql server firewall-rule list --subscription 253e5008-51c0-434b-80b9-ae3ac94bd66b --resource-group rg-sati-demo --server sati-demo-satilogica-central --query "[?name=='datt-workstation-20261007'].name" --output json --only-show-errors
```

This verifies that the exact temporary rule is absent from the pinned Demo SQL server. The corresponding assessment hygiene item is closed. This follow-up read neither changed security settings nor inspected database records or other rule addresses.
