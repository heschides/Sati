[CmdletBinding()]
param()

# In-process Azure CLI fake: exercises the operator script without any network
# call, Azure login, secret, or resource mutation.
$ErrorActionPreference = 'Stop'
function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Demo watchdog alert assertion failed: $Message" }
}
$scriptPath = Join-Path $PSScriptRoot 'Set-DemoWatchdogAlerts.ps1'
$global:WatchdogAlertCalls = [Collections.Generic.List[object]]::new()

function az {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    [void]$global:WatchdogAlertCalls.Add(@($Arguments))
    $global:LASTEXITCODE = 0
    switch ("$($Arguments[0]) $($Arguments[1])") {
        'account show' { return '253e5008-51c0-434b-80b9-ae3ac94bd66b' }
        'functionapp show' {
            return '{"id":"/subscriptions/253e5008-51c0-434b-80b9-ae3ac94bd66b/resourceGroups/rg-sati-demo/providers/Microsoft.Web/sites/sati-demo-refresh-satilogica"}'
        }
        'resource show' {
            return '{"id":"/subscriptions/253e5008-51c0-434b-80b9-ae3ac94bd66b/resourceGroups/rg-sati-demo/providers/Microsoft.Insights/components/sati-demo-refresh-satilogica","type":"Microsoft.Insights/components","location":"centralus"}'
        }
        'functionapp config' { return '[{"name":"DemoWatchdogSchedule","value":"0 0 4 * * *"}]' }
        'rest --method' { return '' }
        default { throw 'The operator script called an unexpected Azure CLI command.' }
    }
}

$plan = & $scriptPath -EmailAddress 'josh@example.invalid' -WhatIf
Assert-Test ($global:WatchdogAlertCalls.Count -eq 0) '-WhatIf made an Azure CLI call.'
Assert-Test (@($plan | Where-Object { $_ -like '*two*' -or $_ -like '*rule*' }).Count -gt 0) '-WhatIf did not show its alert plan.'

try {
    & $scriptPath -EmailAddress 'josh@example.invalid' -Confirm:$false | Out-Null
    throw 'The operator script accepted an unreviewed price.'
}
catch {
    Assert-Test ($_.Exception.Message -like '*provide -ReviewedMonthlyEstimateUsd*') 'The pricing gate did not reject an unreviewed price.'
}
Assert-Test ($global:WatchdogAlertCalls.Count -eq 0) 'The pricing gate made an Azure CLI call.'

& $scriptPath -EmailAddress 'josh@example.invalid' -ReviewedMonthlyEstimateUsd 12.34 `
    -PricingEvidence 'Synthetic Azure pricing calculator quote' -PricingReviewedOn ([datetime]::Today) -Confirm:$false | Out-Null
$puts = @($global:WatchdogAlertCalls | Where-Object { $_[0] -eq 'rest' })
Assert-Test ($puts.Count -eq 3) 'Expected exactly three resource PUTs.'
$urls = [Collections.Generic.List[string]]::new()
$bodies = [Collections.Generic.List[object]]::new()
foreach ($call in $puts) {
    Assert-Test ($call[1] -eq '--method' -and $call[2] -eq 'put') 'An alert resource used a non-idempotent method.'
    $url = $call[[array]::IndexOf($call, '--url') + 1]
    $body = $call[[array]::IndexOf($call, '--body') + 1] | ConvertFrom-Json
    [void]$urls.Add($url)
    [void]$bodies.Add($body)
}
Assert-Test ($urls[0] -like '*/actionGroups/sati-demo-watchdog-email?api-version=2021-09-01') 'Wrong action group target.'
Assert-Test ($urls[1] -like '*/scheduledQueryRules/sati-demo-watchdog-finding?api-version=2021-08-01') 'Wrong finding rule target.'
Assert-Test ($urls[2] -like '*/scheduledQueryRules/sati-demo-watchdog-missing?api-version=2021-08-01') 'Wrong missing rule target.'
Assert-Test ($bodies[0].properties.emailReceivers.Count -eq 1 -and
    $bodies[0].properties.emailReceivers[0].emailAddress -eq 'josh@example.invalid') 'Wrong email receiver.'
Assert-Test ($bodies[1].properties.autoMitigate -eq $false -and
    $bodies[1].properties.muteActionsDuration -eq 'PT20H' -and
    $bodies[1].properties.criteria.allOf[0].query -like '*SATI_WATCHDOG_FINDING*') 'Finding alert could mask future daily findings.'
Assert-Test ($bodies[2].properties.autoMitigate -eq $true -and
    $bodies[2].properties.criteria.allOf[0].metricMeasureColumn -eq 'WatchdogRuns' -and
    $bodies[2].properties.criteria.allOf[0].operator -eq 'LessThan' -and
    $bodies[2].properties.criteria.allOf[0].query -like '*ago(26h)*') 'Missing-result alert does not detect 26 hours without a trace.'
Assert-Test ((@($global:WatchdogAlertCalls | Where-Object { $_ -join ' ' -match 'firewall|sql server|functionapp config appsettings set' }).Count -eq 0)) 'Operator script touched an unrelated Azure setting.'
Write-Output 'DEMO_WATCHDOG_ALERT_TEST_PASSED'
