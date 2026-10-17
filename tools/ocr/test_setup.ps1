#Requires -Version 5.1
# Exercise the production installer with synthetic, inert ZIP files and a local
# download substitute. Nothing is fetched, installed system-wide, or executed.
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('KeeStashDock-ocr-setup-test-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($root)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fixtureState = @{ Downloads = 0; Damage = '' }

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}
function Invoke-WebRequest {
    param([switch]$UseBasicParsing, [string]$Uri, [string]$OutFile, [int]$TimeoutSec)
    $fixtureState.Downloads++
    $name = if ($Uri.EndsWith('.nupkg')) { 'engine.nupkg' } else { ($Uri -split '/')[-1] }
    if ($fixtureState.Damage -eq 'archive' -and $name -eq 'engine.nupkg') {
        [IO.File]::WriteAllText($OutFile, 'untrusted archive')
    } elseif ($fixtureState.Damage -eq 'model' -and $name -eq 'eng.traineddata') {
        [IO.File]::WriteAllText($OutFile, 'untrusted model')
    } else { Copy-Item -LiteralPath (Join-Path $root ('source/' + $name)) -Destination $OutFile }
}
try {
    [IO.Directory]::CreateDirectory((Join-Path $root 'source')) | Out-Null
    $pins = @{
        'tesseract.exe' = '856D2CDDF1B7CD5F014AA92445FC26789116B1B733606D31201F5B32035E36B8'
        'tesseract55.dll' = 'E9903623E901F2D754EB49EEA4DBFF229F599A8817055A3E3F1C292216670CA9'
        'leptonica-1.85.0.dll' = '64BB1D584BCE83DB5E5C3625F1857E4BC12993625699CABD0A9B8B76E393BD01'
        'engine.nupkg' = '2D11E0A45F8F5887D91277DD61E4B2B57423BE0E06AEFBA432484EBC33A8A794'
        'eng.traineddata' = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
        'chi_sim.traineddata' = 'A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730'
        'LICENSE' = 'CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30'
    }
    foreach ($name in $pins.Keys | Where-Object { $_ -ne 'engine.nupkg' }) {
        [IO.File]::WriteAllText((Join-Path $root ('source/' + $name)), ('inert fixture ' + $name))
    }
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $root 'source/engine.nupkg'), [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in 'tesseract.exe', 'tesseract55.dll', 'leptonica-1.85.0.dll') {
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root ('source/' + $name)), ('x64/' + $name))
        }
        foreach ($name in '../escape.txt', 'x86/tesseract.exe', 'tools/install.ps1') {
            $entry = $zip.CreateEntry($name)
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write('must never be extracted or executed') } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose() }
    $scriptText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Setup.ps1'))
    foreach ($name in $pins.Keys) {
        Assert-True ($scriptText.Contains($pins[$name])) ('Production artifact pin missing: ' + $name)
        $scriptText = $scriptText.Replace($pins[$name], (Get-FileHash -LiteralPath (Join-Path $root ('source/' + $name))).Hash)
    }
    $fixtureScript = Join-Path $root 'fixture-setup.ps1'
    [IO.File]::WriteAllText($fixtureScript, $scriptText)
    $fixtureState.Damage = ''
    $fixtureState.Downloads = 0
    $models = Join-Path $root "success space ' literal/OcrModels"
    $engine = Join-Path $root "success space ' literal/OcrEngine"
    & $fixtureScript -Destination $models -EngineDirectory $engine
    Assert-True ($fixtureState.Downloads -eq 4) 'The engine archive and three model/license assets must be fetched once.'
    Assert-True (@(Get-ChildItem -LiteralPath $engine -File).Count -eq 3) 'Only allowlisted engine files may be extracted.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $engine 'escape.txt'))) 'ZIP traversal escaped the allowlist.'
    & $fixtureScript -Destination $models -EngineDirectory $engine
    Assert-True ($fixtureState.Downloads -eq 4) 'Verified installations must not download again.'
    [IO.File]::WriteAllText((Join-Path $engine 'tesseract55.dll'), 'damaged engine')
    & $fixtureScript -Destination $models -EngineDirectory $engine
    Assert-True ($fixtureState.Downloads -eq 5) 'A damaged engine must fetch and verify its archive again.'

    foreach ($scenario in 'archive', 'entry', 'model') {
        $fixtureState.Damage = $scenario
        $models = Join-Path $root ($scenario + '/OcrModels')
        $engine = Join-Path $root ($scenario + '/OcrEngine')
        $text = $scriptText
        if ($scenario -eq 'entry') {
            $hash = (Get-FileHash -LiteralPath (Join-Path $root 'source/tesseract.exe')).Hash
            $text = $text.Replace($hash, ('0' * 64))
        }
        [IO.File]::WriteAllText($fixtureScript, $text)
        $failure = ''
        try { & $fixtureScript -Destination $models -EngineDirectory $engine }
        catch { $failure = $_.Exception.Message }
        Assert-True ($failure -like '*SHA256*') ('Corrupt ' + $scenario + ' must fail checksum validation: ' + $failure)
        if ($scenario -ne 'model') {
            Assert-True (@(Get-ChildItem -LiteralPath $engine -File).Count -eq 0) 'An unverified engine must never be installed.'
        } else {
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $models 'eng.traineddata'))) 'An unverified model must never be installed.'
        }
        Assert-True (@(Get-ChildItem -LiteralPath $engine -Filter '.install-*').Count -eq 0) 'Failed staging files must be removed.'
        Assert-True (@(Get-ChildItem -LiteralPath $models -Filter '.download-*').Count -eq 0) 'Failed downloads must be removed.'
    }
    Write-Output 'OCR setup checks passed: fresh install, idempotence, repair, allowlist, archive/file/model tampering, and cleanup.'
}
finally {
    if ([IO.Path]::GetFullPath($root) -ne $expectedRoot -or
        [IO.Path]::GetDirectoryName($expectedRoot) -ne [IO.Path]::GetTempPath().TrimEnd('\', '/')) {
        throw 'Refusing an unexpected OCR test cleanup path.'
    }
    if (Test-Path -LiteralPath $expectedRoot) { Remove-Item -LiteralPath $expectedRoot -Recurse -Force }
}
