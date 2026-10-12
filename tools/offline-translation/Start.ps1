[CmdletBinding()]
param([string] $RuntimeDirectory = '', [ValidateRange(1024,65535)][int] $Port = 5000, [switch] $Foreground)
. (Join-Path $PSScriptRoot 'Common.ps1')
$taskRuntime = Resolve-OfflineRuntime $RuntimeDirectory
if (-not (Test-Path -LiteralPath (Join-Path $taskRuntime 'installed.json'))) {
    throw 'The offline engine is not installed. Run Setup.ps1 first (the first installation requires internet access).'
}
$existingProcess = Get-OfflineProcess $taskRuntime
if ($null -ne $existingProcess) {
    $existingReady = $false
    try {
        $existingHealth = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2
        $existingReady = $existingHealth.service -eq 'keestashdock-offline' -and $existingHealth.pid -eq $existingProcess.Id
    } catch { }
    if (-not $existingReady) {
        throw 'The offline engine is running but is not ready at the requested port. Stop it from Settings or Stop.ps1, then start it again with the desired port.'
    }
    Write-Host "The offline engine is already ready at http://127.0.0.1:$Port."
    return
}
$server = Join-Path $PSScriptRoot 'server.py'
if ($Foreground) {
    & (Join-Path $taskRuntime 'python\python.exe') $server --runtime $taskRuntime --port $Port
    if ($LASTEXITCODE -ne 0) { throw 'Offline engine failed. Check the startup message above.' }
    return
}
$arguments = @('"' + $server + '"', '--runtime', '"' + $taskRuntime + '"', '--port', $Port.ToString())
$process = Start-Process -FilePath (Join-Path $taskRuntime 'python\pythonw.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRuntime 'service.stdout.log') -RedirectStandardError (Join-Path $taskRuntime 'service.stderr.log')
try {
    $process.Refresh()
    @{ pid = $process.Id; startTicks = $process.StartTime.ToUniversalTime().Ticks.ToString(); port = $Port } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRuntime 'service.json') -Encoding UTF8
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw 'Offline engine exited. Read service.stderr.log in the runtime folder. If a DLL is missing, install Microsoft Visual C++ Redistributable x64.' }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 1
            if ($health.service -eq 'keestashdock-offline' -and $health.pid -eq $process.Id) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }
    if (-not $ready) { throw 'Offline engine did not become ready. Check service.stderr.log and whether the selected port is in use.' }
    Write-Host "Offline engine ready at http://127.0.0.1:$Port. It does not download anything while running."
    Write-Host 'Use Stop.ps1 to stop it. It is not automatically started with Windows.'
} catch {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    throw
}
