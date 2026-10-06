[CmdletBinding()]
param(
    [ValidateSet('ValidMicrosoft','InvalidSignature','OtherSigner','MissingSigner','MissingBundle','UnsupportedFormat','InvalidBounds')]
    [string]$Case = 'ValidMicrosoft'
)

# Run each case in a fresh PowerShell process: signature cases supply an extractor
# and signature provider; format cases exercise the real reader on malformed bytes.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SatiPrerequisiteChecks_' + [Guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    $installer = Join-Path $fixture 'synthetic.exe'
    $formatCase = $Case -in @('MissingBundle','UnsupportedFormat','InvalidBounds')
    if (-not $formatCase) {
        Add-Type -TypeDefinition @'
using System.IO;
public static class SatiInstallerBundleReader {
    public static void Extract(string path, string destination) { File.Copy(path, destination, false); }
}
'@
        function Get-AuthenticodeSignature {
            param($LiteralPath)
            $status = if ($Case -eq 'InvalidSignature') { 'HashMismatch' } else { 'Valid' }
            $signer = if ($Case -eq 'MissingSigner') { $null } else {
                $subject = if ($Case -eq 'OtherSigner') { 'O=Another Company, CN=Test' } else { 'O=Microsoft Corporation, CN=Test' }
                [pscustomobject]@{ Subject=$subject }
            }
            [pscustomobject]@{ Status=$status; SignerCertificate=$signer }
        }
        [IO.File]::WriteAllText($installer, 'synthetic prerequisite')
    } elseif ($Case -eq 'MissingBundle') {
        [IO.File]::WriteAllText($installer, 'not a bundle')
    } else {
        $stream = [IO.File]::Open($installer, [IO.FileMode]::CreateNew)
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([long]100)
            $writer.Write([byte[]](0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae))
            $writer.Write([byte[]]::new(60))
            $major = if ($Case -eq 'UnsupportedFormat') { 7 } else { 6 }
            $writer.Write([uint32]$major); $writer.Write([uint32]0); $writer.Write([int]1)
            $writer.Write('synthetic')
            foreach ($i in 1..5) { $writer.Write([long]0) }
            $writer.Write([long]0); $writer.Write([long]-1); $writer.Write([long]0); $writer.Write([byte]2)
            $writer.Write('SatiLocalSetup.dll')
        } finally { $writer.Dispose() }
    }
    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'installer/Read-LocalInstallerPrerequisite.ps1')
    $result=$null; $failure=''
    try { $result = Test-SatiEmbeddedLocalDbPrerequisite -InstallerPath $installer -WorkingRoot $fixture }
    catch { $failure=$_.Exception.Message }
    if ($Case -eq 'ValidMicrosoft') {
        if ($failure -or $result.Status -cne 'Valid' -or $result.Sha256 -cne (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Valid signature evidence failed: $failure" }
    } else {
        $expected = switch ($Case) {
            'MissingBundle' { 'Bundle manifest unavailable.' }
            'UnsupportedFormat' { 'Unsupported bundle format.' }
            'InvalidBounds' { 'Managed bootstrap bounds invalid.' }
            default { 'Embedded LocalDB prerequisite lacks a valid Microsoft signature.' }
        }
        if ($result -or -not $failure.Contains($expected)) { throw "Prerequisite guard accepted invalid input or failed for another reason: $Case $failure" }
    }
    if (@(Get-ChildItem -LiteralPath $fixture -Directory -Filter 'prerequisite-*').Count) { throw 'Prerequisite inspection left its scratch directory behind.' }
    Write-Output "LOCAL_PREREQUISITE_CHECK_TEST_PASSED Case=$Case"
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ine [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -cnotmatch '^SatiPrerequisiteChecks_[0-9a-f]{32}$') { throw 'Unexpected prerequisite fixture path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
