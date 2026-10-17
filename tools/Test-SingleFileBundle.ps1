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
        [long]$sectionStart = 512
        [long]$sectionSize = 512
        [long]$markerOffset = 600
        if ($Variant -eq 'large-host') { $sectionSize = 6MB; $markerOffset = 5MB + 512 }
        if ($Variant -eq 'section-budget') { $sectionSize = 65MB }
        if ($Variant -eq 'cross-block') { $sectionSize = 128KB; $markerOffset = $sectionStart + 64KB - 16 }
        if ($Variant -eq 'cross-block-pointer') { $sectionSize = 128KB; $markerOffset = $sectionStart + 64KB + 4 }
        [long]$nativeEnd = $sectionStart + $sectionSize
        [long]$header = $nativeEnd + 512
        if ($Variant -eq 'section-end') { $markerOffset = $nativeEnd - 32 }
        if ($Variant -eq 'section-start') { $markerOffset = $sectionStart + 8 }
        if ($Variant -eq 'overlay-marker') { $markerOffset = $nativeEnd + 64 }
        if ($Variant -eq 'marker-outside-start') { $markerOffset = $sectionStart }
        if ($Variant -eq 'marker-outside-end') { $markerOffset = $nativeEnd - 31 }
        $stream.SetLength($header)
        $stream.Position = 0
        $writer.Write([uint16]0x5a4d)
        $stream.Position = 60
        $writer.Write([uint32]128) # DOS e_lfanew
        $stream.Position = 128
        $writer.Write([uint32]0x00004550)
        $writer.Write([uint16]0x8664)
        [uint16]$sectionCount = 1
        if ($Variant -eq 'overlap') { $sectionCount = 2 }
        if ($Variant -eq 'section-count') { $sectionCount = 97 }
        $writer.Write($sectionCount)
        $stream.Position = 148
        $writer.Write([uint16]240)
        $stream.Position = 152
        $writer.Write([uint16]0x20b)
        $stream.Position = 212 # Optional header SizeOfHeaders
        $writer.Write([uint32]512)
        $stream.Position = 392
        $writer.Write([Text.Encoding]::ASCII.GetBytes(".data`0`0`0"))
        $stream.Position = 408
        $writer.Write([uint32]$sectionSize)
        $writer.Write([uint32]$sectionStart)
        if ($Variant -eq 'overlap') {
            $stream.Position = 448
            $writer.Write([uint32]256)
            $writer.Write([uint32]768)
        }
        $stream.Position = $markerOffset - 8
        [long]$directoryPointer = $header
        if ($Variant -eq 'header-in-host') { $directoryPointer = $nativeEnd - 64 }
        $writer.Write($directoryPointer)
        [byte[]]$marker = @(0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
            0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
        if ($Variant -eq 'marker') { $marker[0] = 0 }
        $writer.Write($marker)
        if ($Variant -eq 'duplicate-marker') {
            $stream.Position = $markerOffset + 64 - 8
            $writer.Write($header)
            $writer.Write($marker)
        }
        if ($Variant -eq 'overlay-decoy') {
            $stream.Position = $nativeEnd + 64 - 8
            $writer.Write([long]0)
            $writer.Write($marker)
        }
        $stream.Position = $header
        [uint32]$major = 6
        if ($Variant -eq 'version') { $major = 99 }
        $writer.Write($major)
        $writer.Write([uint32]0)
        [int]$count = 4
        if ($Variant -eq 'count') { $count = 20001 }
        $writer.Write($count)
        $writer.Write('synthetic-bundle-id')
        [long]$configOffset = $nativeEnd + 128
        if ($Variant -eq 'configuration') { $configOffset = $header }
        $writer.Write($configOffset)
        $writer.Write([long]1)
        $writer.Write([long]($nativeEnd + 129))
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
            [long]$offset = $nativeEnd + 128 + $index
            if ($Variant -eq 'range') { $offset = $header }
            $writer.Write($offset)
            $writer.Write([long]1)
            [long]$compressed = 0
            if ($Variant -eq 'compression') { $compressed = 2 }
            $writer.Write($compressed)
            $writer.Write([byte]1)
            $writer.Write($names[$index])
        }
        if ($Variant -eq 'truncated') { $stream.SetLength($stream.Length - 1) }
        switch ($Variant) {
            'dos' { $stream.Position = 0; $writer.Write([uint16]0) }
            'pe-offset' { $stream.Position = 60; $writer.Write([uint32]::MaxValue) }
            'pe-signature' { $stream.Position = 128; $writer.Write([uint32]0) }
            'optional-size' { $stream.Position = 148; $writer.Write([uint16]80) }
            'optional-magic' { $stream.Position = 152; $writer.Write([uint16]0) }
            'headers-small' { $stream.Position = 212; $writer.Write([uint32]400) }
            'headers-large' { $stream.Position = 212; $writer.Write([uint32]::MaxValue) }
            'section-offset' { $stream.Position = 412; $writer.Write([uint32]256) }
            'section-range' { $stream.Position = 408; $writer.Write([uint32]::MaxValue) }
            'section-empty' { $stream.Position = 408; $writer.Write([uint32]0) }
            'section-table' { $stream.SetLength(420) }
        }
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $fixture = Join-Path $testRoot 'bundle.exe'
    $validVariants = @('valid', 'large-host', 'cross-block', 'cross-block-pointer', 'section-start', 'section-end', 'overlay-decoy')
    foreach ($variant in $validVariants) {
        Write-BundleFixture $fixture $variant
        $entries = @(Get-SingleFileBundleEntries $fixture)
        if ($entries.Count -ne 4 -or $entries[0].Path -cne 'KeeStashDock.dll' -or
            $entries[3].Path -cne 'ja/KeeStashDock.resources.dll' -or $entries[0].Length -ne 1) {
            throw ('The valid bundle directory was not decoded correctly: ' + $variant)
        }
    }
    $invalidVariants = @('marker', 'version', 'count', 'configuration', 'all-content', 'duplicate',
        'traversal', 'absolute', 'private', 'long-string', 'range', 'compression', 'truncated',
        'dos', 'pe-offset', 'pe-signature', 'section-count', 'optional-size', 'optional-magic',
        'headers-small', 'headers-large', 'section-offset', 'section-range', 'section-empty', 'section-table', 'section-budget',
        'overlap', 'overlay-marker', 'marker-outside-start', 'marker-outside-end', 'duplicate-marker', 'header-in-host')
    foreach ($variant in $invalidVariants) {
        Write-BundleFixture $fixture $variant
        $rejected = $false
        try { Get-SingleFileBundleEntries $fixture | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw ('Expected an invalid bundle to be rejected: ' + $variant) }
    }
    Write-Output ('Bundle inspection checks passed: {0} valid and {1} invalid fixtures; large native hosts, block/PE boundaries, overlay rejection, headers, flags, paths, ranges, compression, and truncation.' -f
        $validVariants.Count, $invalidVariants.Count)
}
finally {
    if ([IO.Path]::GetFullPath($testRoot) -ne $expectedRoot -or
        -not $expectedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing an unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
