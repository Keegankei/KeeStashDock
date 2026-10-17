# Download only Microsoft's signed v14 x64 installer when native prerequisites are missing.
[CmdletBinding()]
param([switch] $Force, [string] $ProbeExecutable = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-NativeRuntimeInstalled($Runtime) {
    if ($null -eq $Runtime -or $Runtime.Installed -ne 1) { return $false }
    $runtimeVersion = $null
    if (-not [version]::TryParse(([string]$Runtime.Version).TrimStart('v', 'V'), [ref]$runtimeVersion)) { return $false }
    # This is our installation check, not an upstream guarantee for every native
    # dependency. Use -Force to repair a broken runtime or update after probe failure.
    return $runtimeVersion -ge [version]'14.40'
}

function Get-NativeRuntime {
    $baseKey = $null
    $runtimeKey = $null
    try {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        $runtimeKey = $baseKey.OpenSubKey('SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64')
        if ($null -eq $runtimeKey) { return $null }
        return [PSCustomObject]@{ Installed = $runtimeKey.GetValue('Installed', 0); Version = $runtimeKey.GetValue('Version', '') }
    } finally {
        if ($null -ne $runtimeKey) { $runtimeKey.Dispose() }
        if ($null -ne $baseKey) { $baseKey.Dispose() }
    }
}

function Test-NativeExecutable([string] $Executable) {
    $probe = New-Object Diagnostics.Process
    try {
        $probe.StartInfo = New-Object Diagnostics.ProcessStartInfo
        $probe.StartInfo.FileName = [IO.Path]::GetFullPath($Executable)
        $probe.StartInfo.Arguments = '--version'
        $probe.StartInfo.UseShellExecute = $false
        $probe.StartInfo.CreateNoWindow = $true
        $probe.StartInfo.RedirectStandardOutput = $true
        $probe.StartInfo.RedirectStandardError = $true
        if (-not $probe.Start()) { return $false }
        $stdout = $probe.StandardOutput.ReadToEndAsync()
        $stderr = $probe.StandardError.ReadToEndAsync()
        if (-not $probe.WaitForExit(10000)) {
            $probe.Kill()
            $probe.WaitForExit(1000) | Out-Null
            return $false
        }
        return $probe.ExitCode -eq 0
    } catch { return $false }
    finally { $probe.Dispose() }
}

function Assert-MicrosoftInstallerSignature($Signature) {
    if ($null -eq $Signature -or $Signature.Status -ne 'Valid' -or $null -eq $Signature.SignerCertificate -or
        $Signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Microsoft Corporation') {
        throw 'The native runtime installer does not have a valid Microsoft Corporation signature. Installation was stopped.'
    }
}

function Get-NativeRuntimeResult([int] $ExitCode, [bool] $Installed) {
    if ($ExitCode -eq 3010) { return 'RestartRequired' }
    if ($ExitCode -eq 0 -and $Installed) { return 'Installed' }
    if ($ExitCode -eq 1638 -and $Installed) { return 'AlreadyInstalled' }
    throw "Microsoft Visual C++ runtime setup failed or did not install a usable x64 runtime (exit code $ExitCode)."
}

# Dot-sourcing exposes pure checks to regression fixtures without installing anything.
if ($MyInvocation.InvocationName -eq '.') { return }
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Native OCR and offline translation require Windows x64.' }
if (-not $Force) {
    if (-not [string]::IsNullOrWhiteSpace($ProbeExecutable) -and (Test-Path -LiteralPath $ProbeExecutable -PathType Leaf)) {
        if (Test-NativeExecutable $ProbeExecutable) {
            Write-Host 'Native OCR engine probe passed; no runtime installation is needed.'
            exit 0
        }
        Write-Host 'The native engine probe failed; repairing the shared runtime.'
    } elseif (Test-NativeRuntimeInstalled (Get-NativeRuntime)) {
        Write-Host 'Microsoft Visual C++ x64 runtime is already installed.'
        exit 0
    }
}

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-native-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
$installerPath = Join-Path $temporaryDirectory 'vc_redist.x64.exe'
try {
    Write-Host 'Downloading Microsoft Visual C++ x64 runtime...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://aka.ms/vc14/vc_redist.x64.exe' -OutFile $installerPath -TimeoutSec 180
    Assert-MicrosoftInstallerSignature (Get-AuthenticodeSignature -LiteralPath $installerPath)
    Write-Host 'Installing Microsoft Visual C++ x64 runtime. Windows may ask for administrator permission.'
    $installer = Start-Process -FilePath $installerPath -ArgumentList '/install', '/quiet', '/norestart' -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    $outcome = Get-NativeRuntimeResult $installer.ExitCode (Test-NativeRuntimeInstalled (Get-NativeRuntime))
    if ($outcome -eq 'RestartRequired') {
        Write-Host 'Microsoft Visual C++ runtime installed. Restart Windows before continuing component setup.'
        exit 3010
    }
    if (-not [string]::IsNullOrWhiteSpace($ProbeExecutable) -and -not (Test-NativeExecutable $ProbeExecutable)) {
        throw 'The verified OCR engine still cannot start after native runtime setup.'
    }
    Write-Host "Microsoft Visual C++ x64 runtime is ready ($outcome)."
} finally {
    # Remove only the file and empty directory created by this invocation.
    if (Test-Path -LiteralPath $installerPath) { Remove-Item -LiteralPath $installerPath -Force }
    if (Test-Path -LiteralPath $temporaryDirectory) { Remove-Item -LiteralPath $temporaryDirectory }
}
