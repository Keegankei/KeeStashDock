[CmdletBinding()]
param([string] $RuntimeDirectory = '')
. (Join-Path $PSScriptRoot 'Common.ps1')
$taskRuntime = Resolve-OfflineRuntime $RuntimeDirectory
$process = Get-OfflineProcess $taskRuntime
if ($null -eq $process) { Write-Host 'The background offline engine is not running.'; return }
Stop-Process -Id $process.Id -ErrorAction Stop
Write-Host 'Offline engine stopped.'
