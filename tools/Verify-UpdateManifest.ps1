#Requires -Version 5.1
# Verify ownership metadata against the exact package contents and assembly version.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UpdateManifest.Common.ps1')
$packageDirectory = (Resolve-Path -LiteralPath $Path).Path
$files = @(Get-UpdatePackageFiles $packageDirectory)
$version = Get-UpdatePackageVersion $packageDirectory
$manifestPath = Join-Path $packageDirectory 'update-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    (Get-Item -LiteralPath $manifestPath).Length -gt 8MB) { throw 'The update ownership manifest is missing or too large.' }
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if (@($manifest.PSObject.Properties).Count -ne 3 -or
    @($manifest.PSObject.Properties.Name | Where-Object { $_ -cnotin @('format', 'version', 'files') }).Count -ne 0) {
    throw 'The update ownership manifest has unsupported fields.'
}
if ($manifest.format -ne 1 -or $manifest.version -cne $version) { throw 'The update manifest format or version does not match the package.' }
if (@($manifest.files).Count -ne $files.Count) { throw 'The update manifest does not list the exact package file set.' }
$actual = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $files) { $actual.Add($file.Path, $file) }
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.files) {
    if (@($file.PSObject.Properties).Count -ne 3 -or
        @($file.PSObject.Properties.Name | Where-Object { $_ -cnotin @('path', 'length', 'sha256') }).Count -ne 0) {
        throw 'An update file record has unsupported fields.'
    }
    Assert-UpdateRelativePath $file.path
    if (-not $seen.Add($file.path) -or -not $actual.ContainsKey($file.path) -or
        $file.path -ceq 'update-manifest.json') { throw 'The update manifest contains a duplicate or unknown path.' }
    $existing = $actual[$file.path]
    if ($file.length -ne $existing.Length -or $file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-FileHash -LiteralPath $existing.FullName -Algorithm SHA256).Hash -ine $file.sha256) {
        throw ('Update manifest length or SHA-256 mismatch: ' + $file.path)
    }
}
Write-Output ('Update ownership manifest verified: version {0}, {1} files.' -f $version, $files.Count)
