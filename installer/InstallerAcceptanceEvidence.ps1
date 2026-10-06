function Assert-SatiInstallerEvidencePath {
    param([string]$EvidencePath)
    if ([string]::IsNullOrWhiteSpace($EvidencePath)) { return }
    $resolved = [IO.Path]::GetFullPath($EvidencePath)
    if (Test-Path -LiteralPath $resolved) { throw "Refusing to overwrite installer evidence: $resolved" }
    return $resolved
}

function Write-SatiInstallerAcceptanceEvidence {
    param([string]$EvidencePath, [Collections.IDictionary]$Evidence)
    if ([string]::IsNullOrWhiteSpace($EvidencePath)) { return }
    $resolved = Assert-SatiInstallerEvidencePath $EvidencePath
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolved))
    # CreateNew also protects against a competing writer after the preflight check.
    $stream = [IO.File]::Open($resolved, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($Evidence | ConvertTo-Json -Depth 6))
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
    Write-Output "INSTALLER_EVIDENCE_WRITTEN path=$resolved"
}
