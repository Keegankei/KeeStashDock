#Requires -Version 5.1
# Exercises cleanup in a disposable checkout, never in the developer's actual tree.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-clean-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($testRoot)
$outsideRoot = $expectedRoot + '-outside'
$junctions = New-Object 'System.Collections.Generic.List[string]'

function Assert-PreservedFiles([string[]]$Paths) {
    foreach ($relative in $Paths) {
        if ([IO.File]::ReadAllText((Join-Path $testRoot $relative)) -ne 'must survive') {
            throw "Cleanup changed protected content: $relative"
        }
    }
}

try {
    foreach ($directory in 'tools', 'src\Sample\bin\Release\net10.0', 'src\Sample\obj', 'tests',
        'TestResults', '.vs', 'artifacts\publish') {
        [IO.Directory]::CreateDirectory((Join-Path $testRoot $directory)) | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $testRoot 'KeeStashDock.sln'), '')
    [IO.File]::WriteAllText((Join-Path $testRoot 'src\Sample\bin\Release\net10.0\Sample.dll'), 'rebuildable output')

    # Put safeguards inside eligible build directories so exclusion alone cannot
    # accidentally make a broken privacy/component check pass.
    $protectedFiles = @(
        'src\Saved\bin\Data\personal.txt', 'src\Database\bin\snapshot.db',
        'src\WalOnly\bin\snapshot.sqlite3-wal', 'src\Journal\bin\snapshot.db-journal',
        'src\Settings\bin\settings.json.tmp', 'src\Draft\bin\draft.txt',
        'src\GroupTemp\bin\export.ksdgrp.0123456789abcdef0123456789abcdef.tmp',
        'src\Export\bin\export.ksdpkg', 'src\Logs\bin\service.log.1',
        'src\Keys\bin\signing.key', 'src\Environment\bin\.env.local',
        'src\OcrModel\bin\OcrModels\eng.traineddata',
        'src\TranslationModel\bin\sample.argosmodel',
        'src\OcrEngine\bin\OcrEngine\engine.exe',
        'src\Tesseract\bin\x64\tesseract.exe',
        'src\Leptonica\bin\x64\leptonica-1.85.0.dll',
        'src\Runtime\bin\OfflineTranslation\runtime\python.exe',
        'src\Models\bin\OfflineTranslation\models\package\model.bin',
        'src\ArchivedRelease\bin\KeeStashDock-1.2.3-win-x64.zip',
        'src\ArchivedChecksum\bin\KeeStashDock-1.2.3-win-x64.zip.sha256',
        'src\Candidate\bin\review-1.2.3-r4\KeeStashDock.exe',
        'publish\release-1.2.3\KeeStashDock.exe',
        'publish\release-1.2.3\KeeStashDock-1.2.3-win-x64.zip',
        'publish\KeeStashDock-1.2.2-win-x64\KeeStashDock.exe',
        'publish\candidate-win-x64\KeeStashDock.exe',
        'publish\review-1.2.3-r4\KeeStashDock.exe',
        'publish\custom\notes.txt', 'publish\saved\Data\personal.txt',
        'artifacts\custom\keep.txt', 'artifacts\package-ocr\eng.traineddata',
        'tools\ocr\models\eng.traineddata',
        'tools\offline-translation\runtime\python.exe',
        'docs\plans\local\private-plan.md'
    )
    foreach ($relative in $protectedFiles) {
        $file = Join-Path $testRoot $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
        [IO.File]::WriteAllText($file, 'must survive')
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArtifactPolicy.ps1') -Destination (Join-Path $testRoot 'tools\ArtifactPolicy.ps1')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Clean.ps1') -Destination (Join-Path $testRoot 'tools\Clean.ps1')
    $cleaner = Join-Path $testRoot 'tools\Clean.ps1'

    & $cleaner -IncludeIdeState -IncludeBuildOutputs -WhatIf
    foreach ($relative in 'src\Sample\bin', 'src\Sample\obj', 'TestResults', '.vs', 'artifacts\publish') {
        if (-not (Test-Path -LiteralPath (Join-Path $testRoot $relative))) { throw "WhatIf deleted $relative." }
    }
    Assert-PreservedFiles $protectedFiles

    & $cleaner
    foreach ($relative in 'src\Sample\bin', 'src\Sample\obj', 'TestResults') {
        if (Test-Path -LiteralPath (Join-Path $testRoot $relative)) { throw "Default cleanup left $relative." }
    }
    foreach ($relative in '.vs', 'artifacts\publish') {
        if (-not (Test-Path -LiteralPath (Join-Path $testRoot $relative))) { throw "Default cleanup removed $relative." }
    }
    Assert-PreservedFiles $protectedFiles

    & $cleaner -IncludeIdeState -IncludeBuildOutputs
    foreach ($relative in '.vs', 'artifacts\publish') {
        if (Test-Path -LiteralPath (Join-Path $testRoot $relative)) { throw "Optional cleanup left $relative." }
    }
    Assert-PreservedFiles $protectedFiles

    # Even the smoke output must be kept if it holds a retained release.
    $nestedRelease = 'artifacts\publish\release-1.2.3\KeeStashDock.exe'
    $releasePath = Join-Path $testRoot $nestedRelease
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($releasePath)) | Out-Null
    [IO.File]::WriteAllText($releasePath, 'must survive')

    # Windows junctions need no symbolic-link privilege. Check a linked project
    # ancestor, a linked candidate itself, and a link below a real candidate.
    foreach ($relative in 'Project\bin', 'Store') {
        [IO.Directory]::CreateDirectory((Join-Path $outsideRoot $relative)) | Out-Null
    }
    $outsideFiles = @((Join-Path $outsideRoot 'Project\bin\keep.txt'), (Join-Path $outsideRoot 'Store\keep.txt'))
    foreach ($file in $outsideFiles) { [IO.File]::WriteAllText($file, 'outside must survive') }
    foreach ($link in @(
        @{ Path = 'src\LinkedProject'; Target = 'Project' },
        @{ Path = 'src\LinkedOutput\bin'; Target = 'Store' },
        @{ Path = 'src\LinkedChild\bin\external'; Target = 'Store' }
    )) {
        $linkPath = Join-Path $testRoot $link.Path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($linkPath)) | Out-Null
        New-Item -ItemType Junction -Path $linkPath -Target (Join-Path $outsideRoot $link.Target) | Out-Null
        $junctions.Add($linkPath)
    }
    & $cleaner -IncludeBuildOutputs
    Assert-PreservedFiles ($protectedFiles + $nestedRelease)
    foreach ($file in $outsideFiles) {
        if ([IO.File]::ReadAllText($file) -ne 'outside must survive') { throw 'Cleanup changed a junction target.' }
    }
    foreach ($linkPath in $junctions) {
        if (-not (Test-Path -LiteralPath $linkPath)) { throw 'Cleanup removed a junction.' }
    }
    Write-Output 'Cleanup safety checks passed: defaults, WhatIf, releases, candidates, components, privacy, and junctions.'
}
finally {
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFullPath($outsideRoot) -ne ($expectedRoot + '-outside')) {
        throw 'Refusing an unexpected fixture cleanup path.'
    }
    # Delete links themselves before recursively removing our two fixture roots.
    foreach ($linkPath in $junctions) {
        if (Test-Path -LiteralPath $linkPath) {
            $link = Get-Item -LiteralPath $linkPath -Force
            if (-not $link.FullName.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or
                ($link.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                throw 'Refusing an unexpected fixture junction cleanup path.'
            }
            [IO.Directory]::Delete($link.FullName)
        }
    }
    foreach ($fixtureRoot in $expectedRoot, $outsideRoot) {
        if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    }
}
