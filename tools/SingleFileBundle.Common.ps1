# Inspect the .NET 6+ bundle directory without executing or extracting a package.
# Format reference: dotnet/runtime src/installer/managed/Microsoft.NET.HostModel/Bundle/{Manifest,FileEntry,Bundler}.cs
# This is a packaging check; the update manifest still verifies the complete EXE hash.

function Read-BundleString([IO.BinaryReader]$Reader, [int]$MaximumBytes = 512) {
    [int]$length = 0
    [int]$shift = 0
    do {
        [int]$part = $Reader.ReadByte()
        if ($shift -gt 21) { throw 'Invalid bundle string length.' }
        $length = $length -bor (($part -band 127) -shl $shift)
        $shift += 7
    } while (($part -band 128) -ne 0)
    if ($length -lt 1 -or $length -gt $MaximumBytes) { throw 'The bundle string exceeds its size limit.' }
    [byte[]]$bytes = $Reader.ReadBytes($length)
    if ($bytes.Length -ne $length) { throw 'The bundle string is truncated.' }
    return (New-Object Text.UTF8Encoding($false, $true)).GetString($bytes)
}

function Get-BundleDirectoryOffset([IO.BinaryReader]$Reader) {
    $stream = $Reader.BaseStream
    $stream.Position = 0
    if ($Reader.ReadUInt16() -ne 0x5a4d) { throw 'The bundled executable is not a Windows PE file.' }
    $stream.Position = 60
    [long]$peOffset = $Reader.ReadUInt32()
    if ($peOffset -lt 64 -or $peOffset -gt ($stream.Length - 24)) { throw 'The PE header is out of range.' }
    $stream.Position = $peOffset
    if ($Reader.ReadUInt32() -ne 0x00004550) { throw 'The executable has an invalid PE signature.' }
    $null = $Reader.ReadUInt16() # Machine
    [int]$sectionCount = $Reader.ReadUInt16()
    $stream.Position = $peOffset + 20
    [int]$optionalSize = $Reader.ReadUInt16()
    [long]$sectionTable = $peOffset + 24 + $optionalSize
    [long]$sectionTableEnd = $sectionTable + 40 * $sectionCount
    if ($sectionCount -lt 1 -or $sectionCount -gt 96 -or $optionalSize -lt 64 -or $optionalSize -gt 4096 -or
        $sectionTableEnd -gt $stream.Length) { throw 'The PE section table is invalid or truncated.' }
    $stream.Position = $peOffset + 24
    [int]$magic = $Reader.ReadUInt16()
    if (($magic -ne 0x10b -and $magic -ne 0x20b) -or
        ($magic -eq 0x10b -and $optionalSize -lt 96) -or ($magic -eq 0x20b -and $optionalSize -lt 112)) {
        throw 'The PE optional header has an unsupported or truncated format.'
    }
    $stream.Position = $peOffset + 24 + 60
    [long]$headersSize = $Reader.ReadUInt32()
    if ($headersSize -lt $sectionTableEnd -or $headersSize -gt $stream.Length) { throw 'The PE headers have an invalid size.' }

    $sections = New-Object 'System.Collections.Generic.List[object]'
    [long]$nativeEnd = $headersSize
    [long]$scanBytes = 0
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $stream.Position = $sectionTable + 40 * $index + 16
        [long]$size = $Reader.ReadUInt32()
        [long]$offset = $Reader.ReadUInt32()
        if ($size -eq 0) { continue } # Uninitialized sections have no file bytes.
        if ($offset -lt $headersSize -or $size -gt $stream.Length -or $offset -gt ($stream.Length - $size)) {
            throw 'The PE section has an invalid file range.'
        }
        [long]$end = $offset + $size
        foreach ($section in $sections) {
            if ($offset -lt $section.End -and $end -gt $section.Start) { throw 'The PE file sections overlap.' }
        }
        $sections.Add([pscustomobject]@{ Start = $offset; End = $end })
        $nativeEnd = [Math]::Max($nativeEnd, $end)
        $scanBytes += $size
        if ($scanBytes -gt 64MB) { throw 'The native PE sections exceed the inspection size limit.' }
    }

    # The .NET 10 native host can exceed 4 MiB. Inspect only declared PE sections,
    # never the managed bundle overlay, using bounded blocks with a 39-byte overlap
    # to preserve the eight-byte pointer and 32-byte marker across block boundaries.
    [byte[]]$marker = @(0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
    $encoding = [Text.Encoding]::GetEncoding(28591)
    $markerText = $encoding.GetString($marker)
    [long]$markerOffset = -1
    [long]$header = -1
    foreach ($section in $sections) {
        for ([long]$block = $section.Start; $block -lt $section.End; $block += 64KB) {
            $stream.Position = $block
            [int]$blockSize = [Math]::Min(64KB + 39, $section.End - $block)
            [byte[]]$bytes = $Reader.ReadBytes($blockSize)
            if ($bytes.Length -ne $blockSize) { throw 'The PE section is truncated.' }
            $text = $encoding.GetString($bytes)
            [int]$position = $text.IndexOf($markerText, [StringComparison]::Ordinal)
            while ($position -ge 0) {
                if ($position -ge 8 -and $block + $position -ne $markerOffset) {
                    if ($markerOffset -ge 0) { throw 'The executable contains multiple .NET bundle markers.' }
                    $markerOffset = $block + $position
                    $header = [BitConverter]::ToInt64($bytes, $position - 8)
                }
                $position = $text.IndexOf($markerText, $position + 1, [StringComparison]::Ordinal)
            }
        }
    }
    if ($markerOffset -lt 0) { throw 'The executable has no supported .NET bundle marker.' }
    if ($header -lt $nativeEnd -or $header -le ($markerOffset + $marker.Length) -or $header -gt ($stream.Length - 52)) {
        throw 'The executable has no valid .NET bundle directory.'
    }
    return $header
}

function Get-SingleFileBundleEntries([string]$Executable) {
    $stream = [IO.File]::OpenRead($Executable)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $stream.Length -gt 512MB) { throw 'The bundled executable has an invalid size.' }
        [long]$header = Get-BundleDirectoryOffset $reader
        $stream.Position = $header
        $major = $reader.ReadUInt32()
        $minor = $reader.ReadUInt32()
        $count = $reader.ReadInt32()
        if ($major -ne 6 -or $minor -ne 0 -or $count -lt 1 -or $count -gt 20000) {
            throw 'The executable uses an unsupported or invalid .NET bundle directory.'
        }
        $null = Read-BundleString $reader 256
        foreach ($configuration in @('dependencies', 'runtime')) {
            [long]$offset = $reader.ReadInt64()
            [long]$length = $reader.ReadInt64()
            if ($offset -le 0 -or $length -le 0 -or $length -gt $header -or $offset -gt ($header - $length)) {
                throw "The bundle has an invalid $configuration configuration range."
            }
        }
        if ($reader.ReadUInt64() -ne 0) { throw 'All-content extraction would change the portable directory contract.' }
        $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $entries = New-Object 'System.Collections.Generic.List[object]'
        for ($index = 0; $index -lt $count; $index++) {
            [long]$offset = $reader.ReadInt64()
            [long]$length = $reader.ReadInt64()
            [long]$compressed = $reader.ReadInt64()
            [int]$type = $reader.ReadByte()
            $name = Read-BundleString $reader
            if ($offset -le 0 -or $length -le 0 -or $length -gt 512MB -or $compressed -lt 0 -or $compressed -gt $length -or
                $type -lt 0 -or $type -gt 5 -or -not $seen.Add($name)) { throw 'The bundle contains an invalid file entry.' }
            [long]$stored = $length
            if ($compressed -gt 0) { $stored = $compressed }
            if ($stored -gt $header -or $offset -gt ($header - $stored) -or $stream.Position - $header -gt 8MB) {
                throw 'The bundle contains an out-of-range file entry.'
            }
            Assert-UpdateRelativePath $name
            $entries.Add([pscustomobject]@{ Path = $name; Type = $type; Length = $length })
        }
        return $entries.ToArray()
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}
