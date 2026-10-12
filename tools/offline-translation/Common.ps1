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
    $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    $process = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    $expectedPath = Join-Path $RuntimeDirectory 'python\pythonw.exe'
    if ($process.Path -ne $expectedPath -or $process.StartTime.ToUniversalTime().Ticks.ToString() -ne $state.startTicks) {
        throw 'The saved process identity no longer matches. No process was stopped. Check the service.json file in the offline runtime folder.'
    }
    return $process
}
