[CmdletBinding()]
param([string]$Destination = '', [string]$EngineDirectory = '')

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KeeStashDock/OcrModels'
}
if ([string]::IsNullOrWhiteSpace($EngineDirectory)) {
    $EngineDirectory = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Destination))) 'OcrEngine'
}
$EngineDirectory = [IO.Path]::GetFullPath($EngineDirectory)
$Destination = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($Destination) | Out-Null
[IO.Directory]::CreateDirectory($EngineDirectory) | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Pin both the original NuGet archive and the only three executable entries we
# extract. No package scripts, managed wrapper, x86 files or arbitrary ZIP paths run.
$engineHashes = @{
    'tesseract.exe' = '856D2CDDF1B7CD5F014AA92445FC26789116B1B733606D31201F5B32035E36B8'
    'tesseract55.dll' = 'E9903623E901F2D754EB49EEA4DBFF229F599A8817055A3E3F1C292216670CA9'
    'leptonica-1.85.0.dll' = '64BB1D584BCE83DB5E5C3625F1857E4BC12993625699CABD0A9B8B76E393BD01'
}
function Test-VerifiedFile([string]$Path, [string]$Hash) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Hash)
}
$engineReady = $true
foreach ($name in $engineHashes.Keys) {
    if (-not (Test-VerifiedFile (Join-Path $EngineDirectory $name) $engineHashes[$name])) { $engineReady = $false }
}
if (-not $engineReady) {
    $stage = Join-Path $EngineDirectory ('.install-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    try {
        $archive = Join-Path $stage 'engine.nupkg'
        Write-Host 'Downloading verified OCR engine...'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/tesseractocr/5.5.2/tesseractocr.5.5.2.nupkg' -OutFile $archive -TimeoutSec 180
        if (-not (Test-VerifiedFile $archive '2D11E0A45F8F5887D91277DD61E4B2B57423BE0E06AEFBA432484EBC33A8A794')) {
            throw 'The downloaded OCR engine archive did not match its expected SHA256.'
        }
        Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($name in $engineHashes.Keys) {
                $entry = $zip.GetEntry('x64/' + $name)
                if ($null -eq $entry -or $entry.Length -gt 16MB) { throw "Missing or oversized OCR engine file: $name" }
                $staged = Join-Path $stage $name
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $staged, $false)
                if (-not (Test-VerifiedFile $staged $engineHashes[$name])) { throw "The OCR engine file has an unexpected SHA256: $name" }
            }
        }
        finally { $zip.Dispose() }
        foreach ($name in $engineHashes.Keys) {
            Move-Item -LiteralPath (Join-Path $stage $name) -Destination (Join-Path $EngineDirectory $name) -Force
        }
        Write-Host 'OCR engine installed and verified.'
    }
    finally {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        if ([IO.Path]::GetDirectoryName($resolvedStage) -ne $EngineDirectory -or
            [IO.Path]::GetFileName($resolvedStage) -notmatch '^\.install-[a-f0-9]{32}$') {
            throw 'Refusing unexpected OCR staging cleanup path.'
        }
        if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
    }
}
else { Write-Host 'OCR engine is already verified.' }

$revision = '87416418657359cb625c412a48b6e1d6d41c29bd'
$files = @{
    'eng.traineddata' = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
    'chi_sim.traineddata' = 'A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730'
    'LICENSE' = 'CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30'
}
foreach ($name in $files.Keys) {
    $target = Join-Path $Destination $name
    if ((Test-Path -LiteralPath $target) -and ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $files[$name])) {
        Write-Host "$name is already verified."
        continue
    }
    $temporary = Join-Path $Destination ('.download-' + [Guid]::NewGuid().ToString('N'))
    try {
        $uri = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$revision/$name"
        Write-Host "Downloading $name..."
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $temporary -TimeoutSec 90
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $files[$name]) {
            throw "The downloaded $name did not match its expected SHA256."
        }
        Move-Item -LiteralPath $temporary -Destination $target -Force
        Write-Host "$name installed and verified."
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
Write-Host "Offline English and Simplified Chinese OCR models are ready in $Destination."
