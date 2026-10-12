#Requires -Version 5.1
# Verify release privacy checks using empty fixtures, without opening user files.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-publish-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($testRoot)
$verifier = Join-Path $PSScriptRoot 'Verify-Publish.ps1'

function Assert-Rejected([string]$Directory, [string]$ExpectedMessage) {
    $message = $null
    try { & $verifier -Path $Directory }
    catch { $message = $_.Exception.Message }
    if (-not $message -or $message -notlike ($ExpectedMessage + '*')) {
        throw "Unexpected verification result for ${Directory}: $message"
    }
}

try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $privateNames = @('snapshot.db', 'snapshot.db-wal', 'snapshot.db-shm', 'snapshot.db-journal',
        'snapshot.sqlite-wal', 'snapshot.sqlite3-shm', 'export.ksdpkg', 'export.ksdpkg-wal', 'group.ksdgrp', 'service.log', 'service.log.1',
        'settings.json', 'settings.json.tmp', 'draft.txt', 'service.json', '.env', '.env.local',
        'group.ksdgrp.0123456789abcdef0123456789abcdef.tmp',
        'signing.pfx', 'signing.p12', 'signing.key', 'signing.pem')
    foreach ($name in $privateNames) {
        $fixture = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($fixture) | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture $name), 'private fixture')
        Assert-Rejected $fixture 'Private state or development files found in release package:'
    }
    foreach ($name in 'Data', '.git', '.vs', '__pycache__') {
        $fixture = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory((Join-Path $fixture $name)) | Out-Null
        Assert-Rejected $fixture 'Private state or development files found in release package:'
    }
    $dataRoot = Join-Path $testRoot 'Data'
    [IO.Directory]::CreateDirectory($dataRoot) | Out-Null
    Assert-Rejected $dataRoot 'Private state or development files found in release package:'

    # Ordinary package names reach the missing-runtime check instead of a privacy rejection.
    $ordinary = Join-Path $testRoot 'ordinary'
    [IO.Directory]::CreateDirectory($ordinary) | Out-Null
    foreach ($name in 'LICENSE', 'NOTICE', 'README.md', 'server.py', 'System.Data.dll') {
        [IO.File]::WriteAllText((Join-Path $ordinary $name), 'public fixture')
    }
    Assert-Rejected $ordinary 'Missing or empty release file:'
    Write-Output 'Publish privacy checks passed (28 fixtures).'
}
finally {
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing an unexpected fixture cleanup path.'
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
