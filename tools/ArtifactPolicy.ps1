# Shared filename-only safeguards. Never read personal file contents.
function Get-PersonalArtifactReason([IO.FileSystemInfo]$Entry) {
    if (($Entry.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        if ($Entry.Name -ieq 'Data') { return 'a Data directory' }
        return $null
    }

    $name = $Entry.Name
    if ($name -imatch '^(?:settings\.json|draft\.txt|service\.json|secrets\.json)(?:\.(?:tmp|bak))?$' -or
        $name -imatch '\.ksdgrp(?:\.[a-f0-9]{32})?\.tmp$' -or
        $name -imatch '^\.env(?:\..*)?$' -or
        $name -imatch '\.(?:db|sqlite|sqlite3|ksdpkg)(?:-(?:wal|shm|journal))?$' -or
        $name -imatch '\.log(?:\..*)?$' -or
        $Entry.Extension -in @('.pfx', '.p12', '.key', '.pem', '.ksdgrp')) {
        return "potentially personal data: $name"
    }
    return $null
}
