# Check a portable win-x64 package before it is archived or distributed.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$publishDirectory = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw 'The publish path must be a directory.'
}

$required = @(
    'KeeStashDock.exe', 'KeeStashDock.dll', 'KeeStashDock.deps.json',
    'KeeStashDock.runtimeconfig.json', 'coreclr.dll', 'System.Private.CoreLib.dll',
    'e_sqlite3.dll', 'LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'Screenshots.md',
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
foreach ($group in @('ocr', 'dotnet')) {
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

# Reject accidental private state without opening any of its contents.
foreach ($entry in Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force) {
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'A release package must not contain symbolic links or directory junctions.'
    }
    if (($entry.PSIsContainer -and $entry.Name -in @('Data', '.git', '.vs', '__pycache__')) -or
        (-not $entry.PSIsContainer -and ($entry.Name -in @('settings.json', 'draft.txt', 'service.json', 'test_runtime.py', 'test_process.ps1') -or
        $entry.Extension -in @('.db', '.sqlite', '.sqlite3', '.log', '.user', '.suo', '.pyc')))) {
        throw ('Private state or development files found in release package: ' + $entry.Name)
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
