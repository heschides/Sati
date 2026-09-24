function Get-SatiInstallerRunningProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string[]]$ProcessNames,

        [scriptblock]$ProcessProvider
    )

    $expectedNames = @($ProcessNames |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim() } |
        Select-Object -Unique)
    if ($expectedNames.Count -eq 0) {
        throw 'At least one Sati process name is required.'
    }

    try {
        $processes = if ($null -eq $ProcessProvider) {
            @(Get-Process -ErrorAction Stop)
        }
        else {
            @(& $ProcessProvider)
        }

        return @($processes | Where-Object {
            $processName = [string]$_.ProcessName
            if ([string]::IsNullOrWhiteSpace($processName)) {
                $processName = [string]$_.Name
            }

            $expectedNames -contains $processName
        })
    }
    catch {
        throw [InvalidOperationException]::new(
            'Setup could not verify whether Sati is running. Close every Sati and Sati Demo window, then run the installer again.',
            $_.Exception)
    }
}
