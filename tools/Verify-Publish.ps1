# Check a portable win-x64 package before it is archived or distributed.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ArtifactPolicy.ps1')
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
    if ($entry.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $entry.FullName -Force) { $pending.Push($child) }
    }
}

$required = @(
    'KeeStashDock.exe', 'KeeStashDock.dll', 'KeeStashDock.deps.json',
    'KeeStashDock.runtimeconfig.json', 'coreclr.dll', 'System.Private.CoreLib.dll',
    'e_sqlite3.dll', 'LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'StartHere.md', 'Screenshots.md', 'Updating.md',
    'x64/tesseract.exe', 'x64/tesseract55.dll', 'x64/leptonica-1.85.0.dll',
    'NativeRuntime/Setup.ps1',
    'OcrModels/Setup.ps1', 'OcrModels/README.md', 'OcrModels/LICENSE',
    'OcrModels/eng.traineddata', 'OcrModels/chi_sim.traineddata',
    'OfflineTranslation/Common.ps1', 'OfflineTranslation/Setup.cmd',
    'OfflineTranslation/Setup.ps1', 'OfflineTranslation/Start.cmd',
    'OfflineTranslation/Start.ps1', 'OfflineTranslation/Stop.cmd',
    'OfflineTranslation/Stop.ps1', 'OfflineTranslation/server.py',
    'OfflineTranslation/setup_runtime.py', 'OfflineTranslation/README.md'
)
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
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

$modelHashes = @{
    'eng.traineddata' = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
    'chi_sim.traineddata' = 'A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730'
    'LICENSE' = 'CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30'
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
Write-Host 'Portable package verified: runtime, 20 locales, OCR engine/models, offline setup, licenses, and no private state.'
& (Join-Path $PSScriptRoot 'Verify-UpdateManifest.ps1') -Path $publishDirectory
