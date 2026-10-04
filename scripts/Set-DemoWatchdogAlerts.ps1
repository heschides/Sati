[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$EmailAddress,
    [decimal]$ReviewedMonthlyEstimateUsd,
    [string]$PricingEvidence,
    [datetime]$PricingReviewedOn
)

# Source-only operator script. Josh runs this after publishing DemoWatchdog and
# setting DemoWatchdogSchedule=0 0 4 * * * on sati-demo-refresh-satilogica.
# It PUTs only one dedicated action group and two dedicated log-search rules;
# it does not publish code, alter SQL or firewall settings, or send a test mail.
#
# Pricing model checked against Microsoft on 2026-10-03:
# - Two enabled log-search alert rules are billable by their evaluation interval.
#   These evaluate hourly, without dimension splitting. Microsoft's public
#   pricing page renders the dollar figure dynamically. The exact current
#   regional price could not be independently resolved from this environment.
# - Azure Monitor includes 1,000 email notifications per month, then bills
#   additional emails. Application Insights ingestion may also be billable.
# - Before applying, Josh must supply a recent USD estimate from the Azure
#   pricing calculator or portal, with evidence covering two hourly log rules,
#   one email receiver, and expected notification volume. These parameters are
#   an operator review gate, not a price guarantee or an Azure bill limit.
# Sources: https://azure.microsoft.com/en-us/pricing/details/monitor/
#          https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/cost-estimate
#          https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices

$ErrorActionPreference = 'Stop'
$subscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
$resourceGroup = 'rg-sati-demo'
$functionApp = 'sati-demo-refresh-satilogica'
$insightsName = 'sati-demo-refresh-satilogica'
$actionGroupName = 'sati-demo-watchdog-email'
$findingRuleName = 'sati-demo-watchdog-finding'
$missingRuleName = 'sati-demo-watchdog-missing'
$insightsId = "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Insights/components/$insightsName"
$actionGroupId = "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Insights/actionGroups/$actionGroupName"
$management = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Insights"

try { $parsedEmail = [System.Net.Mail.MailAddress]::new($EmailAddress) }
catch { throw 'EmailAddress must be a valid individual email address.' }
if ($parsedEmail.Address -cne $EmailAddress -or $EmailAddress.Length -gt 254) {
    throw 'EmailAddress must be one valid individual email address.'
}

# The first query sees each daily finding for two hours. It is stateless so a
# known poison message cannot permanently mask a new finding on a later day.
# Action muting suppresses the second hourly evaluation of that same trace.
$findingQuery = 'traces | where timestamp >= ago(2h) | where message startswith "SATI_WATCHDOG_FINDING"'
# summarize count() always returns one numeric row, including when no trace
# exists. The result is measured, rather than counting returned rows.
$missingQuery = 'traces | where timestamp >= ago(26h) | where (message startswith "SATI_WATCHDOG_OK" or message startswith "SATI_WATCHDOG_FINDING") | summarize WatchdogRuns=count()'

$tags = @{ SatiManagedBy = 'Set-DemoWatchdogAlerts'; SatiScope = 'DemoWatchdog' }
$actionGroup = @{
    location = 'global'
    tags = $tags
    properties = @{
        enabled = $true
        groupShortName = 'SatiDemo'
        emailReceivers = @(@{ name = 'Josh'; emailAddress = $EmailAddress; useCommonAlertSchema = $true })
    }
}
function New-RuleBody {
    param([string]$DisplayName, [string]$Description, [string]$Query,
        [string]$WindowSize, [string]$Operator, [int]$Threshold,
        [string]$TimeAggregation, [string]$MeasureColumn, [bool]$AutoMitigate,
        [string]$MuteActionsDuration, [string]$Location, [string]$QueryTimeRange)
    $criterion = @{
        query = $Query
        timeAggregation = $TimeAggregation
        operator = $Operator
        threshold = $Threshold
        failingPeriods = @{ numberOfEvaluationPeriods = 1; minFailingPeriodsToAlert = 1 }
    }
    if ($MeasureColumn) { $criterion.metricMeasureColumn = $MeasureColumn }
    $properties = @{
        displayName = $DisplayName
        description = $Description
        severity = 2
        enabled = $true
        evaluationFrequency = 'PT1H'
        scopes = @($insightsId)
        windowSize = $WindowSize
        criteria = @{ allOf = @($criterion) }
        actions = @{ actionGroups = @($actionGroupId) }
        autoMitigate = $AutoMitigate
    }
    if ($MuteActionsDuration) { $properties.muteActionsDuration = $MuteActionsDuration }
    if ($QueryTimeRange) { $properties.overrideQueryTimeRange = $QueryTimeRange }
    return @{ kind = 'LogAlert'; location = $Location; tags = $tags; properties = $properties }
}

if ($WhatIfPreference) {
    Write-Output 'WHATIF: Require a current documented USD price estimate for two hourly log alerts and email notifications.'
    Write-Output "WHATIF: Validate existing Demo Function $functionApp and Application Insights $insightsName."
    Write-Output 'WHATIF: Require DemoWatchdogSchedule=0 0 4 * * * before alert activation.'
    Write-Output "WHATIF: PUT action group $actionGroupName with one email receiver."
    Write-Output "WHATIF: PUT hourly stateless finding rule $findingRuleName (2-hour trace window, 20-hour action mute)."
    Write-Output "WHATIF: PUT hourly stateful missing-result rule $missingRuleName (26-hour lookback)."
    return
}

if ($ReviewedMonthlyEstimateUsd -le 0 -or [string]::IsNullOrWhiteSpace($PricingEvidence) -or
    $PricingEvidence.Trim().Length -lt 10 -or $PricingReviewedOn -eq [datetime]::MinValue -or
    $PricingReviewedOn.Date -gt [datetime]::Today -or
    $PricingReviewedOn.Date -lt [datetime]::Today.AddDays(-7)) {
    throw 'Before applying, provide -ReviewedMonthlyEstimateUsd (>0), -PricingEvidence (calculator/portal quote reference), and -PricingReviewedOn (within seven days).'
}

function Invoke-AzJson([string[]]$Arguments) {
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Azure CLI request failed. No later alert resource was changed.' }
    return ($output | Out-String)
}

$activeSubscription = (Invoke-AzJson @('account', 'show', '--query', 'id', '-o', 'tsv')).Trim()
if ($activeSubscription -cne $subscription) { throw 'The active Azure subscription is not the reviewed Sati Demo subscription.' }
$app = Invoke-AzJson @('functionapp', 'show', '-g', $resourceGroup, '-n', $functionApp, '-o', 'json') | ConvertFrom-Json
if ($app.id -ine "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Web/sites/$functionApp") {
    throw 'The reviewed Demo Function App was not found.'
}
$insights = Invoke-AzJson @('resource', 'show', '--ids', $insightsId, '-o', 'json') | ConvertFrom-Json
if ($insights.id -ine $insightsId -or $insights.type -ine 'Microsoft.Insights/components' -or
    [string]::IsNullOrWhiteSpace([string]$insights.location)) {
    throw 'The reviewed Demo Application Insights resource was not found.'
}
$settings = Invoke-AzJson @('functionapp', 'config', 'appsettings', 'list', '-g', $resourceGroup, '-n', $functionApp, '-o', 'json') | ConvertFrom-Json
try {
    $schedule = @($settings | Where-Object name -eq 'DemoWatchdogSchedule')
    if ($schedule.Count -ne 1 -or $schedule[0].value -cne '0 0 4 * * *') {
        throw 'Set DemoWatchdogSchedule=0 0 4 * * * on the existing Function App before applying alert rules.'
    }
}
finally { $settings = $null }

$finding = New-RuleBody -DisplayName 'Sati Demo watchdog finding' `
    -Description 'The daily read-only Demo watchdog found an operational condition; investigate and recover manually.' `
    -Query $findingQuery -WindowSize 'PT2H' -Operator 'GreaterThan' -Threshold 0 `
    -TimeAggregation 'Count' -MeasureColumn '' -AutoMitigate $false `
    -MuteActionsDuration 'PT20H' -Location $insights.location -QueryTimeRange ''
$missing = New-RuleBody -DisplayName 'Sati Demo watchdog missing' `
    -Description 'No daily watchdog result was recorded in 26 hours; inspect the Function timer and telemetry.' `
    -Query $missingQuery -WindowSize 'P1D' -Operator 'LessThan' -Threshold 1 `
    -TimeAggregation 'Average' -MeasureColumn 'WatchdogRuns' -AutoMitigate $true `
    -MuteActionsDuration '' -Location $insights.location -QueryTimeRange 'P2D'

function Set-ExactResource([string]$Name, [string]$Url, $Body) {
    if (-not $PSCmdlet.ShouldProcess($Name, 'Create or update reviewed Demo watchdog resource')) { return }
    $json = $Body | ConvertTo-Json -Compress -Depth 18
    [void](Invoke-AzJson @('rest', '--method', 'put', '--url', $Url,
        '--body', $json, '--headers', 'Content-Type=application/json', '-o', 'none'))
    Write-Output "Applied $Name."
}

Set-ExactResource $actionGroupName "$management/actionGroups/${actionGroupName}?api-version=2021-09-01" $actionGroup
Set-ExactResource $findingRuleName "$management/scheduledQueryRules/${findingRuleName}?api-version=2021-08-01" $finding
Set-ExactResource $missingRuleName "$management/scheduledQueryRules/${missingRuleName}?api-version=2021-08-01" $missing
Write-Output 'Review both alert rules and the action group in Azure Monitor, then send a test notification through the action-group test control.'
