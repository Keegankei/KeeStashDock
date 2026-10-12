Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A .cmd launched from PowerShell 7 can inherit its incompatible module search path.
# Prefer this child interpreter's built-in modules without changing the parent environment.
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath

function Resolve-OfflineRuntime([string] $RuntimeDirectory) {
    if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
        $portableRuntime = Join-Path $PSScriptRoot 'runtime'
        if (Test-Path -LiteralPath (Join-Path $portableRuntime 'installed.json')) {
            $RuntimeDirectory = $portableRuntime
        } else {
            $RuntimeDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KeeStashDock\OfflineTranslation'
        }
    }
    return [IO.Path]::GetFullPath($RuntimeDirectory)
}

function Get-OfflineProcess([string] $RuntimeDirectory) {
    $stateFile = Join-Path $RuntimeDirectory 'service.json'
    if (-not (Test-Path -LiteralPath $stateFile)) { return $null }
    try {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ($null -eq $state -or $null -eq $state.PSObject.Properties['pid'] -or
            $null -eq $state.PSObject.Properties['startTicks']) { return $null }
        $servicePid = 0
        if (-not [int]::TryParse([string]$state.pid, [ref]$servicePid) -or $servicePid -le 0) { return $null }
        $process = Get-Process -Id $servicePid -ErrorAction SilentlyContinue
        if ($null -eq $process) { return $null }
        $expectedPath = Join-Path $RuntimeDirectory 'python\pythonw.exe'
        if ($process.Path -ne $expectedPath -or $process.StartTime.ToUniversalTime().Ticks.ToString() -ne [string]$state.startTicks) {
            # A stale PID may have been reused. Ignore it without touching that process;
            # a successful start writes the new engine's identity over the stale state.
            return $null
        }
        return $process
    } catch {
        # A truncated state file or inaccessible/recycled process is not our engine.
        return $null
    }
}
