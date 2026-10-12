# Downloads only pinned, SHA-256-verified official Python/PyPI/Argos artifacts.
[CmdletBinding()]
param([string] $RuntimeDirectory = '')
. (Join-Path $PSScriptRoot 'Common.ps1')
$taskRuntime = Resolve-OfflineRuntime $RuntimeDirectory
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    throw 'This offline engine requires Windows x64. ARM64-native packages are not included.'
}
if ($null -ne (Get-OfflineProcess $taskRuntime)) {
    throw 'Stop the offline engine with Stop.ps1 before running setup again.'
}
New-Item -ItemType Directory -Path (Join-Path $taskRuntime 'downloads') -Force | Out-Null
$pythonZip = Join-Path $taskRuntime 'downloads\python-3.12.10-embed-amd64.zip'
$pythonHash = '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3'
if (-not (Test-Path -LiteralPath $pythonZip) -or (Get-FileHash -LiteralPath $pythonZip -Algorithm SHA256).Hash -ne $pythonHash) {
    Write-Host 'Downloading isolated Python runtime from python.org...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' -OutFile $pythonZip
}
if ((Get-FileHash -LiteralPath $pythonZip -Algorithm SHA256).Hash -ne $pythonHash) {
    throw 'Python archive checksum mismatch. Delete the downloaded archive and retry setup on a trusted connection.'
}
# From this point setup may replace runtime files. A cancelled/failed repair must
# not leave a success marker that allows an incomplete engine to be started.
$installedMarker = Join-Path $taskRuntime 'installed.json'
if (Test-Path -LiteralPath $installedMarker) { Remove-Item -LiteralPath $installedMarker -Force }
$pythonDirectory = Join-Path $taskRuntime 'python'
Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonDirectory -Force
# Embeddable Python ignores global/user site packages and PYTHONPATH.
@('python312.zip', '.', '..\packages', 'import site') | Set-Content -LiteralPath (Join-Path $pythonDirectory 'python312._pth') -Encoding ASCII
$manifestJson = & (Join-Path $pythonDirectory 'python.exe') (Join-Path $PSScriptRoot 'setup_runtime.py') --manifest
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the pinned download manifest.' }
foreach ($artifact in ($manifestJson | ConvertFrom-Json)) {
    $destination = Join-Path $taskRuntime ('downloads\' + $artifact.name)
    if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $artifact.sha256) { continue }
    Write-Host ('Downloading ' + $artifact.name + '...')
    $temporaryDownload = $destination + '.partial'
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri ($artifact.url + '?download=1') -OutFile $temporaryDownload
    if ((Get-FileHash -LiteralPath $temporaryDownload -Algorithm SHA256).Hash -ne $artifact.sha256) {
        throw ('Checksum mismatch for ' + $artifact.name + '. Retry setup on a trusted connection.')
    }
    Move-Item -LiteralPath $temporaryDownload -Destination $destination -Force
}
& (Join-Path $pythonDirectory 'python.exe') (Join-Path $PSScriptRoot 'setup_runtime.py') --runtime $taskRuntime
if ($LASTEXITCODE -ne 0) { throw 'Offline setup failed. Check the message above, then retry Setup.ps1; valid downloads will be reused.' }
Write-Host "Setup complete. Runtime: $taskRuntime"
Write-Host 'Start with Start.ps1, then select Local offline translation in KeeStashDock.'
