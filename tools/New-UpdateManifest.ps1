#Requires -Version 5.1
# Generate the ownership manifest after publishing and before package verification.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UpdateManifest.Common.ps1')
$packageDirectory = (Resolve-Path -LiteralPath $Path).Path
$files = @(Get-UpdatePackageFiles $packageDirectory)
$version = Get-UpdatePackageVersion $packageDirectory
$manifest = [ordered]@{
    format = 1
    version = $version
    files = @($files | ForEach-Object {
        [ordered]@{
            path = $_.Path
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
$manifestPath = Join-Path $packageDirectory 'update-manifest.json'
$temporary = Join-Path $packageDirectory ('.update-manifest-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))
    if ((Get-Item -LiteralPath $temporary).Length -gt 8MB) { throw 'The ownership manifest exceeds its size limit.' }
    Move-Item -LiteralPath $temporary -Destination $manifestPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
Write-Output ('Update ownership manifest generated: version {0}, {1} files.' -f $version, $files.Count)
