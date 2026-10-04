$ErrorActionPreference = 'Stop'
$shared = Join-Path $PSScriptRoot '..\Sati.DemoRefresh\Shared\DemoReset.ps1'
. $shared

function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# Construct a real SqlException without opening SQL. The text deliberately contains
# a sentinel that must never appear in the reset's public error or audit metadata.
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
$sqlErrorType = [System.Data.SqlClient.SqlError]
$sqlErrorConstructor = $sqlErrorType.GetConstructor($flags, $null,
    [Type[]]@([int], [byte], [byte], [string], [string], [string], [int], [Exception]), $null)
$sqlError = $sqlErrorConstructor.Invoke(@(51012, [byte]16, [byte]1, 'mock-only.invalid',
    'SENTINEL_EXTERNAL_DATA', 'SatiAssertCanonicalResetAllowed', 1, $null))
$sqlErrorsType = [System.Data.SqlClient.SqlErrorCollection]
$sqlErrors = [Activator]::CreateInstance($sqlErrorsType, $true)
$addError = $sqlErrorsType.GetMethod('Add', $flags)
[void]$addError.Invoke($sqlErrors, @($sqlError))
$createException = [System.Data.SqlClient.SqlException].GetMethod('CreateException',
    ([Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static), $null,
    [Type[]]@($sqlErrorsType, [string]), $null)
$global:BlockedSqlException = $createException.Invoke($null, @($sqlErrors, 'mock-only.invalid'))
$global:PreflightException = $global:BlockedSqlException

$env:SATI_DEMO_SQL_SERVER = 'mock-only.invalid'
$global:ResetGuardEvents = [Collections.Generic.List[string]]::new()
$global:ResetGuardOutcomes = [Collections.Generic.List[object]]::new()

function Get-DemoSqlToken { return 'mock-token' }
function New-DemoConnection {
    param([string]$Server, [string]$Token)
    Assert-Test ($Server -eq 'mock-only.invalid' -and $Token -eq 'mock-token') 'Unexpected external SQL boundary.'
    $connection = [pscustomobject]@{ State = [Data.ConnectionState]::Open }
    $connection | Add-Member ScriptMethod CreateCommand {
        $parameters = [pscustomobject]@{}
        $parameters | Add-Member ScriptMethod AddWithValue { param($Name, $Value) return $null }
        $command = [pscustomobject]@{ CommandText = ''; CommandTimeout = 0; Parameters = $parameters }
        $command | Add-Member ScriptMethod ExecuteScalar {
            Assert-Test ($this.CommandText -like '*sp_getapplock*') 'Unexpected scalar command.'
            [void]$global:ResetGuardEvents.Add('lock')
            return 0
        }
        $command | Add-Member ScriptMethod ExecuteNonQuery {
            if ($this.CommandText -eq 'EXEC dbo.SatiAssertCanonicalResetAllowed;') {
                [void]$global:ResetGuardEvents.Add('preflight')
                throw $global:PreflightException
            }
            if ($this.CommandText -like '*SatiResetToCanonicalBaseline*') {
                throw 'The restore procedure ran after external-state rejection.'
            }
            if ($this.CommandText -like '*sp_releaseapplock*') {
                [void]$global:ResetGuardEvents.Add('release')
                return 0
            }
            throw 'Unexpected SQL command after external-state block.'
        }
        return $command
    }
    $connection | Add-Member ScriptMethod Dispose { $this.State = [Data.ConnectionState]::Closed }
    return $connection
}
function Write-DemoResetOutcome {
    param($Server, $Token, $RequestId, $ActorUserId, $Trigger, $Succeeded, $Stage, $Seconds, $Detail)
    [void]$global:ResetGuardOutcomes.Add([pscustomobject]@{
        Succeeded = $Succeeded; Stage = $Stage; Detail = $Detail
    })
}
function Invoke-DemoComplianceSeed { throw 'Compliance seed must not run after external-state block.' }

$caught = $null
try { Invoke-DemoFullReset -RequestId ([Guid]::NewGuid()) -ActorUserId 1 -Trigger Manual }
catch { $caught = $_ }
Assert-Test ($null -ne $caught) 'External-state rejection did not fail the reset.'
Assert-Test ($caught.Exception.Message -like '*Stage=DemoResetBlockedByExternalClearinghouseState*') `
    'Reset failure did not identify the stable blocked stage.'
Assert-Test (-not $caught.Exception.Message.Contains('SENTINEL_EXTERNAL_DATA')) 'External data leaked into reset error.'
Assert-Test (($global:ResetGuardEvents -join ',') -eq 'lock,preflight,release') `
    'A reset action ran after the SQL guard rejected external state.'
Assert-Test ($global:ResetGuardOutcomes.Count -eq 1) 'Blocked reset must attempt one failed outcome audit.'
$outcome = $global:ResetGuardOutcomes[0]
Assert-Test (-not $outcome.Succeeded) 'Blocked reset was audited as successful.'
Assert-Test ($outcome.Stage -eq 'DemoResetBlockedByExternalClearinghouseState') 'Audit stage was not stable.'
Assert-Test ($outcome.Detail.SqlErrorNumbers -contains 51012) 'SQL guard error number was lost.'
Assert-Test (-not (($outcome | ConvertTo-Json -Depth 6).Contains('SENTINEL_EXTERNAL_DATA'))) `
    'External data leaked into audit metadata.'

# A newly published Function must also fail closed if the owner-executed guard
# procedure has not yet been installed on an older Demo database.
$global:PreflightException = [InvalidOperationException]::new('SENTINEL_MISSING_GUARD_DETAIL')
$global:ResetGuardEvents.Clear()
$global:ResetGuardOutcomes.Clear()
$missingGuardFailure = $null
try { Invoke-DemoFullReset -RequestId ([Guid]::NewGuid()) -ActorUserId 1 -Trigger Manual }
catch { $missingGuardFailure = $_ }
Assert-Test ($null -ne $missingGuardFailure) 'Missing guard did not fail the reset.'
Assert-Test ($missingGuardFailure.Exception.Message -like '*Stage=AssertCanonicalResetAllowed*') `
    'Missing guard was not attributed to preflight.'
Assert-Test (-not $missingGuardFailure.Exception.Message.Contains('SENTINEL_MISSING_GUARD_DETAIL')) `
    'Missing-guard exception content leaked.'
Assert-Test (($global:ResetGuardEvents -join ',') -eq 'lock,preflight,release') `
    'The restore procedure ran while the guard was unavailable.'
Assert-Test ($global:ResetGuardOutcomes.Count -eq 1 -and
    -not $global:ResetGuardOutcomes[0].Succeeded -and
    $global:ResetGuardOutcomes[0].Stage -eq 'AssertCanonicalResetAllowed') `
    'Missing guard did not produce one safely attributed failure audit.'
Write-Output 'DEMO_RESET_EXTERNAL_GUARD_TEST_PASSED'
