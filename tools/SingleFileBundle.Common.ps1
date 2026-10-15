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

function Get-SingleFileBundleEntries([string]$Executable) {
    $stream = [IO.File]::OpenRead($Executable)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $stream.Length -gt 512MB) { throw 'The bundled executable has an invalid size.' }
        # Our apphost header is well below 4 MiB. Read only that bounded prefix;
        # scanning assembly payloads would waste memory and accept false markers.
        [byte[]]$prefix = $reader.ReadBytes([int][Math]::Min($stream.Length, 4MB))
        if ($prefix[0] -ne 0x4d -or $prefix[1] -ne 0x5a) { throw 'The bundled executable is not a Windows PE file.' }
        [byte[]]$marker = @(0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
            0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
        $encoding = [Text.Encoding]::GetEncoding(28591)
        $position = $encoding.GetString($prefix).IndexOf($encoding.GetString($marker), [StringComparison]::Ordinal)
        if ($position -lt 8) { throw 'The executable has no supported .NET bundle marker.' }
        [long]$header = [BitConverter]::ToInt64($prefix, $position - 8)
        if ($header -le ($position + $marker.Length) -or $header -gt ($stream.Length - 52)) {
            throw 'The executable has no valid .NET bundle directory.'
        }
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
