# Shared format-1 package ownership validation for PowerShell 5.1 and later.
# Keep path/size rules aligned with Infrastructure/Updates/UpdatePackageManifest.cs.
. (Join-Path $PSScriptRoot 'ArtifactPolicy.ps1')

function Assert-UpdateNoReparseAncestors([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Update package paths must not contain symbolic links or junctions.'
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Assert-UpdateRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 512 -or
        $Path -match '[\\<>:"|?*\x00-\x1f]' -or $Path.StartsWith('/')) {
        throw 'An update manifest path is not a safe relative Windows path.'
    }
    $segments = $Path.Split('/')
    $protected = @('Data', 'AppData', 'Logs', 'Backups', 'Drafts', 'Screenshots',
        'settings.json', 'credentials.json', 'secrets.json', 'draft.txt',
        'keestashdock.db', 'keestashdock.db-wal', 'keestashdock.db-shm',
        'snippets.db', 'snippets.db-wal', 'snippets.db-shm', '.git', '.codex', '.agents', '.aws', '.ssh', '.env')
    if ($segments[0] -in $protected) { throw 'An update manifest cannot own user data or repository metadata.' }
    if ($segments[0] -ieq 'theme' -and $segments.Length -gt 1 -and $segments[1] -ine 'builtin') {
        throw 'An update manifest may only maintain built-in themes, never custom themes or staging data.'
    }
    foreach ($segment in $segments) {
        if (-not $segment -or $segment -in @('.', '..') -or $segment.EndsWith('.') -or $segment.EndsWith(' ')) {
            throw 'An update manifest path contains an ambiguous or traversing segment.'
        }
        $stem = $segment.Split('.')[0]
        if ($stem -match '^(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³])$') {
            throw 'An update manifest path contains a reserved Windows device name.'
        }
    }
}

function Get-UpdatePackageVersion([string]$Directory) {
    $assembly = Join-Path $Directory 'KeeStashDock.dll'
    # Legacy flat packages expose the managed assembly; organized single-file
    # packages carry the same version resource on their root apphost executable.
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        $assembly = Join-Path $Directory 'KeeStashDock.exe'
    }
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'The package must contain its versioned application.' }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly)
    if ([string]::IsNullOrWhiteSpace($info.FileVersion)) { throw 'The application must contain a file version.' }
    $version = [Version]$info.FileVersion
    if ($version.Build -lt 0 -or $version.Revision -gt 0) {
        throw 'Release packages require a three-part stable version and a zero file-version revision.'
    }
    return '{0}.{1}.{2}' -f $version.Major, $version.Minor, $version.Build
}

function Get-UpdatePackageFiles([string]$Directory) {
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    Assert-UpdateNoReparseAncestors $root
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'The package path must be a directory.' }
    $pending = New-Object 'System.Collections.Generic.Stack[IO.FileSystemInfo]'
    $pending.Push((Get-Item -LiteralPath $root -Force))
    $files = New-Object 'System.Collections.Generic.List[object]'
    [long]$total = 0
    while ($pending.Count -gt 0) {
        $entry = $pending.Pop()
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'An update package must not contain symbolic links or junctions.'
        }
        $privateReason = Get-PersonalArtifactReason $entry
        if ($privateReason -or $entry.Name -in @('.git', '.vs', '__pycache__')) {
            throw ('Private state or development files found in update package: ' + $entry.Name)
        }
        if ($entry.FullName -ne $root) {
            $relative = $entry.FullName.Substring($root.Length + 1).Replace('\', '/')
            Assert-UpdateRelativePath $relative
        }
        if ($entry.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $entry.FullName -Force) { $pending.Push($child) }
            continue
        }
        if ($relative -ieq 'update-manifest.json') {
            if ($relative -cne 'update-manifest.json') { throw 'The update manifest filename must use its canonical lowercase spelling.' }
            continue
        }
        if ($relative -iin @('theme', 'theme/builtin')) {
            throw 'Theme roots are directories and cannot be owned as update files.'
        }
        if ($entry.Length -gt 512MB) { throw ('An update package file exceeds 512 MiB: ' + $relative) }
        $total += $entry.Length
        if ($total -gt 2GB -or $files.Count -ge 20000) { throw 'The update package exceeds its expanded-size or file-count limit.' }
        $files.Add([pscustomobject]@{ Path = $relative; FullName = $entry.FullName; Length = [long]$entry.Length })
    }
    if (-not ($files | Where-Object Path -CEQ 'KeeStashDock.exe')) { throw 'The package must contain KeeStashDock.exe.' }
    # Ordinal ordering is stable across maintainer/CI display languages.
    $byPath = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($file in $files) { $byPath.Add($file.Path, $file) }
    $paths = [string[]]@($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    foreach ($relative in $paths) { $byPath[$relative] }
}
