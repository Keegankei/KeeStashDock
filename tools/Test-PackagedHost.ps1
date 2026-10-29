#Requires -Version 5.1
# Execute an explicitly supplied package's isolated verification entry point.
# It exercises native loading and rendering without starting the normal app.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [ValidateRange(10, 180)][int]$TimeoutSeconds = 90,
    [switch]$AllowInstalledComponents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/')
$executable = Join-Path $package 'KeeStashDock.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The package executable is missing.' }
$reportPath = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-host-test-' + [Guid]::NewGuid().ToString('N') + '.json')
$process = $null
try {
    # Start-Process joins ArgumentList with spaces, so quote the generated path
    # explicitly. Windows file paths cannot contain a double quotation mark.
    $arguments = @('--verify-package', ('"' + $reportPath + '"'))
    if ($AllowInstalledComponents) { $arguments += '--installed-ocr' }
    $process = Start-Process -FilePath $executable -ArgumentList $arguments `
        -WorkingDirectory $package -WindowStyle Hidden -PassThru
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (-not $process.WaitForExit(1000)) {
        if ($deadline.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            # This handle belongs only to the test process. Never select a
            # user's running application by executable name.
            $process.Kill()
            $null = $process.WaitForExit(5000)
            throw 'The packaged host verification timed out.'
        }
    }
    $process.Refresh()
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw ('The packaged host produced no report. Exit code: ' + $process.ExitCode)
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $report.Success) {
        throw ('The packaged host verification failed: ' + ($report | ConvertTo-Json -Depth 6 -Compress))
    }
    if ([IO.Path]::GetFullPath($report.BaseDirectory).TrimEnd('\', '/') -ine $package -or
        [IO.Path]::GetFullPath($report.ProcessPath) -ine $executable) {
        throw 'The packaged process moved its application/data root or entry point.'
    }
    if ($report.Version -notmatch '^\d+\.\d+\.\d+$' -or $report.LocaleCount -lt 20 -or
        [string]::IsNullOrWhiteSpace($report.SqliteVersion) -or
        -not $report.ProjectedTextRendered) {
        throw 'The packaged host report is missing a required runtime check.'
    }
    if ([Version]$report.Version -ge [Version]'1.3.0') {
        if (-not $report.CompleteThemeValidated -or $report.BuiltInThemeCount -lt 4 -or
            $report.BuiltInThemeCount -ne @($report.BuiltInThemeIds).Count -or
            $report.BuiltInThemeIds -notcontains 'sunlit-gallery' -or
            $report.BuiltInThemeIds -notcontains 'twilight-study' -or
            $report.BuiltInThemeIds -notcontains 'wisteria-atelier' -or
            $report.BuiltInThemeIds -notcontains 'wisteria-reverie') {
            throw 'The complete bundled light/dark theme validation is missing.'
        }
    }
    if ($AllowInstalledComponents) {
        if ($report.OcrMode -ne 'Installed' -or -not $report.SyntheticOcrRecognized) {
            throw 'The installed OCR recognition check is missing.'
        }
    } elseif ($report.OcrMode -ne 'Optional' -or -not $report.MissingOcrHandled -or -not $report.OptionalComponentsAbsent) {
        throw 'The fresh-package optional component checks are missing.'
    }
    Write-Output ($report | ConvertTo-Json -Depth 6)
}
finally {
    if ($null -ne $process) { $process.Dispose() }
    # Only the unique report created by this invocation is removed. The
    # package, installed components, runtime cache and user data are untouched.
    if (Test-Path -LiteralPath $reportPath -PathType Leaf) { Remove-Item -LiteralPath $reportPath -Force }
}
