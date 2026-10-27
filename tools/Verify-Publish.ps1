# Check a portable win-x64 package before it is archived or distributed.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $Path, [switch]$AllowInstalledComponents)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ArtifactPolicy.ps1')
. (Join-Path $PSScriptRoot 'UpdateManifest.Common.ps1')
. (Join-Path $PSScriptRoot 'SingleFileBundle.Common.ps1')
$publishDirectory = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw 'The publish path must be a directory.'
}

# Inspect names before required files, and do not traverse symbolic links or junctions.
$pending = New-Object 'System.Collections.Generic.Stack[IO.FileSystemInfo]'
$pending.Push((Get-Item -LiteralPath $publishDirectory -Force))
while ($pending.Count -gt 0) {
    $entry = $pending.Pop()
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'A release package must not contain symbolic links or directory junctions.'
    }
    $personalReason = Get-PersonalArtifactReason $entry
    if ($personalReason -or
        ($entry.PSIsContainer -and $entry.Name -in @('.git', '.vs', '__pycache__')) -or
        (-not $entry.PSIsContainer -and ($entry.Name -in @('test_runtime.py', 'test_process.ps1') -or
        $entry.Extension -in @('.user', '.suo', '.pyc')))) {
        throw ('Private state or development files found in release package: ' + $entry.Name)
    }
    if (-not $AllowInstalledComponents -and -not $entry.PSIsContainer -and
        ($entry.Name -match '^(?i)(?:tesseract.*\.(?:exe|dll)|leptonica.*\.dll)$' -or $entry.Extension -ieq '.traineddata')) {
        throw ('A fresh release must not contain an installed OCR engine or model: ' + $entry.Name)
    }
    if ($entry.PSIsContainer) {
        if ($entry.FullName -ne $publishDirectory) {
            $relativeDirectory = $entry.FullName.Substring($publishDirectory.TrimEnd('\', '/').Length + 1).Replace('\', '/')
            Assert-UpdateRelativePath $relativeDirectory
        }
        foreach ($child in Get-ChildItem -LiteralPath $entry.FullName -Force) { $pending.Push($child) }
    }
}

$required = @(
    'KeeStashDock.exe', 'StartHere.md',
    'NativeRuntime/Setup.ps1',
    'OcrModels/Setup.ps1', 'OcrModels/README.md',
    'OfflineTranslation/Common.ps1', 'OfflineTranslation/Setup.cmd',
    'OfflineTranslation/Setup.ps1', 'OfflineTranslation/Start.cmd',
    'OfflineTranslation/Start.ps1', 'OfflineTranslation/Stop.cmd',
    'OfflineTranslation/Stop.ps1', 'OfflineTranslation/server.py',
    'OfflineTranslation/setup_runtime.py', 'OfflineTranslation/README.md'
)
if ($AllowInstalledComponents) {
    $required += @('x64/tesseract.exe', 'x64/tesseract55.dll', 'x64/leptonica-1.85.0.dll',
        'OcrModels/LICENSE', 'OcrModels/eng.traineddata', 'OcrModels/chi_sim.traineddata')
} else {
    if ((Test-Path -LiteralPath (Join-Path $publishDirectory 'x64')) -or
        (Test-Path -LiteralPath (Join-Path $publishDirectory 'x86')) -or
        @(Get-ChildItem -LiteralPath $publishDirectory -Filter '*.traineddata' -Recurse -File).Count -gt 0 -or
        (Test-Path -LiteralPath (Join-Path $publishDirectory 'OfflineTranslation/runtime')) -or
        (Test-Path -LiteralPath (Join-Path $publishDirectory 'OfflineTranslation/models'))) {
        throw 'A fresh release must not contain preinstalled OCR engines/models or offline translation runtimes/models.'
    }
}
$flat = Test-Path -LiteralPath (Join-Path $publishDirectory 'KeeStashDock.dll') -PathType Leaf
if ($flat) {
    $required += @('KeeStashDock.dll', 'KeeStashDock.deps.json', 'KeeStashDock.runtimeconfig.json',
        'coreclr.dll', 'System.Private.CoreLib.dll', 'e_sqlite3.dll')
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'Licenses/LICENSE') -PathType Leaf) {
    $required += @('Licenses/LICENSE', 'Licenses/NOTICE', 'Licenses/THIRD_PARTY_NOTICES.md',
        'Docs/Screenshots.md', 'Docs/Updating.md', 'Docs/Updating.zh-CN.md')
} else {
    # Continue validating previously issued flat packages without weakening
    # their complete runtime, documentation, or ownership requirements.
    $required += @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'Screenshots.md', 'Updating.md')
}
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
if ((Test-Path -LiteralPath (Join-Path $publishDirectory 'KeeStashDock.exe') -PathType Leaf) -and
    [Version](Get-UpdatePackageVersion $publishDirectory) -ge [Version]'1.3.0') {
    $builtInRoot = Join-Path $repositoryDirectory 'theme/builtin'
    foreach ($themeFile in Get-ChildItem -LiteralPath $builtInRoot -Recurse -File) {
        $relativeThemeFile = $themeFile.FullName.Substring($builtInRoot.Length + 1).Replace('\', '/')
        $required += 'theme/builtin/' + $relativeThemeFile
    }
}
foreach ($locale in Get-ChildItem -LiteralPath (Join-Path $repositoryDirectory 'src/KeeStashDock.Desktop/Locales') -Filter '*.json' -File) {
    $required += 'Locales/' + $locale.Name
}
foreach ($group in @('ocr', 'dotnet', 'qr')) {
    foreach ($license in Get-ChildItem -LiteralPath (Join-Path $repositoryDirectory ('third_party/' + $group)) -File) {
        $required += 'Licenses/' + $group + '/' + $license.Name
    }
}
foreach ($relative in $required) {
    $file = Join-Path $publishDirectory $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Missing or empty release file: $relative"
    }
}
if (-not $flat) {
    $embedded = @(Get-SingleFileBundleEntries (Join-Path $publishDirectory 'KeeStashDock.exe'))
    # The single-file host can statically link CoreCLR instead of listing a coreclr.dll entry.
    # Test-PackagedHost verifies actual startup in addition to this manifest inspection.
    foreach ($runtime in @('KeeStashDock.dll', 'KeeStashDock.deps.json', 'KeeStashDock.runtimeconfig.json',
        'KeeStashDock.Core.dll', 'KeeStashDock.Domain.dll', 'KeeStashDock.Infrastructure.dll',
        'System.Private.CoreLib.dll', 'e_sqlite3.dll',
        'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'wpfgfx_cor3.dll')) {
        if (-not ($embedded | Where-Object Path -CEQ $runtime)) { throw "Missing bundled runtime file: $runtime" }
    }
    if ($embedded | Where-Object { $_.Path -match '^(?:x64|x86|Locales|OcrModels|OfflineTranslation|NativeRuntime|Docs|Licenses|theme)/' -or
            (-not $AllowInstalledComponents -and $_.Path -match '(?i)(?:^|/)(?:tesseract[^/]*|leptonica[^/]*|[^/]+\.traineddata)$') }) {
        throw 'Editable assets and the separate OCR engine must remain outside the application bundle.'
    }
    foreach ($entry in Get-ChildItem -LiteralPath $publishDirectory -File) {
        if ($entry.Name -cnotin @('KeeStashDock.exe', 'StartHere.md', 'update-manifest.json')) {
            throw ('Unexpected loose file in the organized package root: ' + $entry.Name)
        }
    }
}

if ($AllowInstalledComponents) {
    $modelHashes = @{
        'eng.traineddata' = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
        'chi_sim.traineddata' = 'A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730'
        'LICENSE' = 'CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30'
    }

    $engineHashes = @{
        'tesseract.exe' = '856D2CDDF1B7CD5F014AA92445FC26789116B1B733606D31201F5B32035E36B8'
        'tesseract55.dll' = 'E9903623E901F2D754EB49EEA4DBFF229F599A8817055A3E3F1C292216670CA9'
        'leptonica-1.85.0.dll' = '64BB1D584BCE83DB5E5C3625F1857E4BC12993625699CABD0A9B8B76E393BD01'
    }
    foreach ($name in $engineHashes.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $publishDirectory ('x64/' + $name)) -Algorithm SHA256).Hash -ne $engineHashes[$name]) {
            throw "The installed OCR engine has an unexpected SHA-256: $name"
        }
    }
    foreach ($name in $modelHashes.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $publishDirectory ('OcrModels/' + $name)) -Algorithm SHA256).Hash -ne $modelHashes[$name]) {
            throw "The packaged OCR asset has an unexpected SHA-256: $name"
        }
    }

    $languages = & (Join-Path $publishDirectory 'x64/tesseract.exe') --tessdata-dir (Join-Path $publishDirectory 'OcrModels') --list-langs
    if ($LASTEXITCODE -ne 0 -or $languages -notcontains 'eng' -or $languages -notcontains 'chi_sim') {
        throw 'The packaged OCR engine could not start or load both language models.'
    }
}
Write-Host 'Portable package verified: runtime, 20 locales, optional component policy, setup tools, licenses, and no private state.'
& (Join-Path $PSScriptRoot 'Verify-UpdateManifest.ps1') -Path $publishDirectory
