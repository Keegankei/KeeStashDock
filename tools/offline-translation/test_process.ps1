# Exercises persisted process identity handling without starting or stopping a service.
. (Join-Path $PSScriptRoot 'Common.ps1')
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('keestashdock-process-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
try {
    $statePath = Join-Path $fixtureDirectory 'service.json'
    foreach ($state in @(
        '{', '{}', 'null',
        '{"pid":"invalid","startTicks":"0"}',
        '{"pid":-1,"startTicks":"0"}',
        ('{"pid":' + $PID + ',"startTicks":"0"}'),
        (@{ pid = $PID; startTicks = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString() } | ConvertTo-Json)
    )) {
        Set-Content -LiteralPath $statePath -Value $state -Encoding UTF8
        if ($null -ne (Get-OfflineProcess $fixtureDirectory)) {
            throw 'A stale or malformed state file identified an unrelated process as the offline engine.'
        }
    }
    Write-Host 'Offline process identity checks passed; unrelated processes remain untouched.'
} finally {
    # Delete only the two explicitly created fixture paths, never recursively.
    if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -Force }
    if (Test-Path -LiteralPath $fixtureDirectory) { Remove-Item -LiteralPath $fixtureDirectory }
}
