#Requires -Version 5.1
# Exercise ownership manifest generation and tampering checks on disposable files.
[CmdletBinding()]
param([string]$AssemblyPath, [string]$ExecutablePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '../src/KeeStashDock.Desktop/bin/Release/net10.0-windows/KeeStashDock.dll'
}
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = [IO.Path]::ChangeExtension($AssemblyPath, '.exe')
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-manifest-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($testRoot)
$generator = Join-Path $PSScriptRoot 'New-UpdateManifest.ps1'
$verifier = Join-Path $PSScriptRoot 'Verify-UpdateManifest.ps1'

function Assert-Rejected([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected unsafe or tampered package to be rejected.' }
}

try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    Copy-Item -LiteralPath $AssemblyPath -Destination (Join-Path $testRoot 'KeeStashDock.dll')
    [IO.File]::WriteAllText((Join-Path $testRoot 'KeeStashDock.exe'), 'Disposable fixture, not executable.')
    $payload = Join-Path $testRoot 'LICENSE'
    [IO.File]::WriteAllText($payload, 'fixture license')
    & $generator -Path $testRoot
    & $verifier -Path $testRoot
    $manifestPath = Join-Path $testRoot 'update-manifest.json'
    $original = [IO.File]::ReadAllText($manifestPath)
    & $generator -Path $testRoot | Out-Null
    if ([IO.File]::ReadAllText($manifestPath) -cne $original) { throw 'Manifest generation must be deterministic.' }

    [IO.File]::WriteAllText($payload, 'corrupted content')
    Assert-Rejected { & $verifier -Path $testRoot }
    [IO.File]::WriteAllText($payload, 'fixture license')
    Remove-Item -LiteralPath $payload
    Assert-Rejected { & $verifier -Path $testRoot }
    [IO.File]::WriteAllText($payload, 'fixture license')
    $extra = Join-Path $testRoot 'extra.dll'
    [IO.File]::WriteAllText($extra, 'unowned file')
    Assert-Rejected { & $verifier -Path $testRoot }
    Remove-Item -LiteralPath $extra

    foreach ($change in @('version', 'duplicate', 'traversal')) {
        $manifest = $original | ConvertFrom-Json
        switch ($change) {
            'version' { $manifest.version = '99.0.0' }
            'duplicate' { $manifest.files[1].path = $manifest.files[0].path }
            'traversal' { $manifest.files[0].path = '../outside.dll' }
        }
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6))
        Assert-Rejected { & $verifier -Path $testRoot }
    }
    [IO.File]::WriteAllText($manifestPath, $original)
    $private = Join-Path $testRoot 'Data'
    [IO.Directory]::CreateDirectory($private) | Out-Null
    Assert-Rejected { & $generator -Path $testRoot }
    Assert-Rejected { & $verifier -Path $testRoot }
    Remove-Item -LiteralPath $private
    $private = Join-Path $testRoot 'settings.json'
    [IO.File]::WriteAllText($private, 'private fixture')
    Assert-Rejected { & $generator -Path $testRoot }
    Remove-Item -LiteralPath $private
    $linkTarget = Join-Path $testRoot 'link-target'
    $linkPath = Join-Path $testRoot 'linked'
    [IO.Directory]::CreateDirectory($linkTarget) | Out-Null
    New-Item -ItemType Junction -Path $linkPath -Target $linkTarget | Out-Null
    try {
        Assert-Rejected { & $generator -Path $testRoot }
        Assert-Rejected { & $verifier -Path $testRoot }
    }
    finally {
        # Delete the junction itself; never recursively traverse its target.
        if (Test-Path -LiteralPath $linkPath) { [IO.Directory]::Delete($linkPath) }
        [IO.Directory]::Delete($linkTarget)
    }
    & $verifier -Path $testRoot
    # An organized package reads version metadata from the native apphost;
    # ownership verification must not depend on a loose managed assembly.
    $organized = Join-Path $testRoot 'organized-fixture'
    [IO.Directory]::CreateDirectory((Join-Path $organized 'Licenses')) | Out-Null
    Copy-Item -LiteralPath $ExecutablePath -Destination (Join-Path $organized 'KeeStashDock.exe')
    [IO.File]::WriteAllText((Join-Path $organized 'Licenses/LICENSE'), 'fixture license')
    & $generator -Path $organized
    & $verifier -Path $organized
    $organizedManifest = Get-Content -LiteralPath (Join-Path $organized 'update-manifest.json') -Raw | ConvertFrom-Json
    if ($organizedManifest.version -cne ($original | ConvertFrom-Json).version) {
        throw 'Flat and organized packages must expose the same application version.'
    }
    [IO.File]::WriteAllText((Join-Path $organized 'KeeStashDock.exe'), 'not a versioned application')
    Assert-Rejected { & $generator -Path $organized }
    Assert-Rejected { & $verifier -Path $organized }
    Write-Output 'Update manifest checks passed: flat/organized versions, valid/deterministic, corrupt, missing, extra, duplicate, traversal, private-state, and junction cases.'
}
finally {
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing an unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
