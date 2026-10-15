#Requires -Version 5.1
# Synthetic bundle directories exercise bounded inspection without launching code.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UpdateManifest.Common.ps1')
. (Join-Path $PSScriptRoot 'SingleFileBundle.Common.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-bundle-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($testRoot)

function Write-BundleFixture([string]$Path, [string]$Variant = 'valid') {
    $stream = [IO.File]::Create($Path)
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write((New-Object byte[] 512))
        $stream.Position = 0
        $writer.Write([byte]0x4d)
        $writer.Write([byte]0x5a)
        $stream.Position = 32
        $writer.Write([long]512)
        [byte[]]$marker = @(0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
            0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
        if ($Variant -eq 'marker') { $marker[0] = 0 }
        $writer.Write($marker)
        $stream.Position = 512
        [uint32]$major = 6
        if ($Variant -eq 'version') { $major = 99 }
        $writer.Write($major)
        $writer.Write([uint32]0)
        [int]$count = 4
        if ($Variant -eq 'count') { $count = 20001 }
        $writer.Write($count)
        $writer.Write('synthetic-bundle-id')
        [long]$configOffset = 128
        if ($Variant -eq 'configuration') { $configOffset = 512 }
        $writer.Write($configOffset)
        $writer.Write([long]1)
        $writer.Write([long]129)
        $writer.Write([long]1)
        [uint64]$flags = 0
        if ($Variant -eq 'all-content') { $flags = 1 }
        $writer.Write($flags)
        $names = @('KeeStashDock.dll', 'KeeStashDock.deps.json', 'KeeStashDock.runtimeconfig.json', 'ja/KeeStashDock.resources.dll')
        if ($Variant -eq 'duplicate') { $names[3] = 'keestashdock.dll' }
        if ($Variant -eq 'traversal') { $names[3] = '../outside.dll' }
        if ($Variant -eq 'absolute') { $names[3] = 'C:/outside.dll' }
        if ($Variant -eq 'private') { $names[3] = 'Data/settings.json' }
        if ($Variant -eq 'long-string') { $names[3] = ('x' * 513) }
        for ($index = 0; $index -lt $names.Length; $index++) {
            [long]$offset = 128 + $index
            if ($Variant -eq 'range') { $offset = 512 }
            $writer.Write($offset)
            $writer.Write([long]1)
            [long]$compressed = 0
            if ($Variant -eq 'compression') { $compressed = 2 }
            $writer.Write($compressed)
            $writer.Write([byte]1)
            $writer.Write($names[$index])
        }
        if ($Variant -eq 'truncated') { $stream.SetLength($stream.Length - 1) }
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $fixture = Join-Path $testRoot 'bundle.exe'
    Write-BundleFixture $fixture
    $entries = @(Get-SingleFileBundleEntries $fixture)
    if ($entries.Count -ne 4 -or $entries[0].Path -cne 'KeeStashDock.dll' -or
        $entries[3].Path -cne 'ja/KeeStashDock.resources.dll' -or $entries[0].Length -ne 1) {
        throw 'The valid bundle directory was not decoded correctly.'
    }
    foreach ($variant in @('marker', 'version', 'count', 'configuration', 'all-content', 'duplicate',
            'traversal', 'absolute', 'private', 'long-string', 'range', 'compression', 'truncated')) {
        Write-BundleFixture $fixture $variant
        $rejected = $false
        try { Get-SingleFileBundleEntries $fixture | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw ('Expected an invalid bundle to be rejected: ' + $variant) }
    }
    Write-Output 'Bundle inspection checks passed: valid entries, unsupported/missing headers, extraction flags, unsafe/duplicate paths, bounds, compression, and truncation.'
}
finally {
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing an unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
