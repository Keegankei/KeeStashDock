[CmdletBinding()]
param([string]$Destination = '')

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Destination)) {
    # Source checkout uses tools/ocr/models; a published app ships this under OcrModels.
    if ((Split-Path -Leaf $PSScriptRoot) -eq 'OcrModels') { $Destination = $PSScriptRoot }
    else { $Destination = Join-Path $PSScriptRoot 'models' }
}
$Destination = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$revision = '87416418657359cb625c412a48b6e1d6d41c29bd'
$files = @{
    'eng.traineddata' = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
    'chi_sim.traineddata' = 'A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730'
    'LICENSE' = 'CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
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
