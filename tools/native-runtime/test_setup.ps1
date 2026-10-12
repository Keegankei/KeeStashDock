# Verify setup decisions and signature rejection without downloading or installing.
. (Join-Path $PSScriptRoot 'Setup.ps1')
function Assert-Equal($Expected, $Actual) {
    if ($Expected -ne $Actual) { throw "Expected $Expected, got $Actual." }
}
Assert-Equal $false (Test-NativeRuntimeInstalled $null)
Assert-Equal $false (Test-NativeRuntimeInstalled ([PSCustomObject]@{ Installed = 0; Version = 'v14.51.1.0' }))
Assert-Equal $false (Test-NativeRuntimeInstalled ([PSCustomObject]@{ Installed = 1; Version = 'v14.30.1.0' }))
Assert-Equal $false (Test-NativeRuntimeInstalled ([PSCustomObject]@{ Installed = 1; Version = 'invalid' }))
Assert-Equal $true (Test-NativeRuntimeInstalled ([PSCustomObject]@{ Installed = 1; Version = 'v14.40.0.0' }))
Assert-Equal $true (Test-NativeRuntimeInstalled ([PSCustomObject]@{ Installed = 1; Version = 'v14.51.1.0' }))
Assert-Equal 'Installed' (Get-NativeRuntimeResult 0 $true)
Assert-Equal 'AlreadyInstalled' (Get-NativeRuntimeResult 1638 $true)
Assert-Equal 'RestartRequired' (Get-NativeRuntimeResult 3010 $false)
foreach ($code in @(0, 1638, 1602, 1603, 1223)) {
    $rejected = $false
    try { Get-NativeRuntimeResult $code $false | Out-Null } catch { $rejected = $true }
    Assert-Equal $true $rejected
}
foreach ($signature in @($null, [PSCustomObject]@{ Status = 'Valid'; SignerCertificate = $null })) {
    $rejected = $false
    try { Assert-MicrosoftInstallerSignature $signature } catch { $rejected = $true }
    Assert-Equal $true $rejected
}
foreach ($publisher in @('Microsoft Corporation', 'Untrusted Publisher')) {
    $certificate = [PSCustomObject]@{ Publisher = $publisher }
    $certificate | Add-Member -MemberType ScriptMethod -Name GetNameInfo -Value { param($type, $issuer) return $this.Publisher }
    foreach ($status in @('Valid', 'NotSigned', 'HashMismatch', 'NotTrusted')) {
        $rejected = $false
        try { Assert-MicrosoftInstallerSignature ([PSCustomObject]@{ Status = $status; SignerCertificate = $certificate }) } catch { $rejected = $true }
        Assert-Equal (-not ($publisher -eq 'Microsoft Corporation' -and $status -eq 'Valid')) $rejected
    }
}
Write-Host 'Native runtime setup checks passed without installation or download.'
