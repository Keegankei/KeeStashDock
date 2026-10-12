#Requires -Version 5.1
# Exercises cleanup in a disposable checkout, never in the developer's actual tree.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-clean-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($testRoot)
try {
    foreach ($directory in 'tools', 'src\Sample\bin', 'src\Sample\obj', 'tests',
        'TestResults', '.vs', 'artifacts\disposable', 'publish\saved\Data', 'publish\database') {
        [IO.Directory]::CreateDirectory((Join-Path $testRoot $directory)) | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $testRoot 'KeeStashDock.sln'), '')
    [IO.File]::WriteAllText((Join-Path $testRoot 'publish\saved\Data\personal.txt'), 'must survive')
    [IO.File]::WriteAllText((Join-Path $testRoot 'publish\database\snapshot.db'), 'must survive')
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
    foreach ($protected in 'publish\saved\Data\personal.txt', 'publish\database\snapshot.db') {
        if ([IO.File]::ReadAllText((Join-Path $testRoot $protected)) -ne 'must survive') {
            throw "Cleanup changed protected content: $protected"
        }
    }
    Write-Output 'Cleanup safety checks passed.'
}
finally {
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing an unexpected fixture cleanup path.'
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
