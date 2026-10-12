#Requires -Version 5.1
# Exercises cleanup in a disposable checkout, never in the developer's actual tree.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-clean-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($testRoot)
try {
    foreach ($directory in 'tools', 'src\Sample\bin', 'src\Sample\obj', 'tests',
        'TestResults', '.vs', 'artifacts\disposable', 'publish\saved\Data', 'publish\database',
        'publish\Data', 'publish\wal-only', 'publish\logs', 'publish\keys', 'publish\environment',
        'publish\export', 'publish\settings-temp', 'publish\group-temp') {
        [IO.Directory]::CreateDirectory((Join-Path $testRoot $directory)) | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $testRoot 'KeeStashDock.sln'), '')
    $protectedFiles = @('publish\saved\Data\personal.txt', 'publish\database\snapshot.db',
        'publish\Data\personal.txt', 'publish\wal-only\snapshot.sqlite3-wal',
        'publish\settings-temp\settings.json.tmp', 'publish\group-temp\export.ksdgrp.0123456789abcdef0123456789abcdef.tmp',
        'publish\export\export.ksdpkg', 'publish\logs\service.log.1', 'publish\keys\signing.key', 'publish\environment\.env.local')
    foreach ($protected in $protectedFiles) {
        [IO.File]::WriteAllText((Join-Path $testRoot $protected), 'must survive')
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArtifactPolicy.ps1') -Destination (Join-Path $testRoot 'tools\ArtifactPolicy.ps1')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Clean.ps1') -Destination (Join-Path $testRoot 'tools\Clean.ps1')
    $cleaner = Join-Path $testRoot 'tools\Clean.ps1'

    & $cleaner -IncludeIdeState -IncludeBuildOutputs -WhatIf
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'src\Sample\bin'))) { throw 'WhatIf deleted files.' }

    & $cleaner
    if (Test-Path -LiteralPath (Join-Path $testRoot 'src\Sample\bin')) { throw 'Default cleanup left bin.' }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'src\Sample\obj')) { throw 'Default cleanup left obj.' }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'TestResults')) { throw 'Default cleanup left test results.' }
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot '.vs'))) { throw 'Default cleanup removed IDE state.' }
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'artifacts\disposable'))) { throw 'Default cleanup removed outputs.' }

    & $cleaner -IncludeIdeState -IncludeBuildOutputs
    if (Test-Path -LiteralPath (Join-Path $testRoot '.vs')) { throw 'Optional cleanup left IDE state.' }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'artifacts\disposable')) { throw 'Optional cleanup left outputs.' }
    foreach ($protected in $protectedFiles) {
        if ([IO.File]::ReadAllText((Join-Path $testRoot $protected)) -ne 'must survive') {
            throw "Cleanup changed protected content: $protected"
        }
    }
    Write-Output 'Cleanup safety checks passed.'
}
finally {
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing an unexpected fixture cleanup path.'
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
