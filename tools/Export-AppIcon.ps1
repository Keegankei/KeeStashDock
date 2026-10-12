# Export the approved artwork as a Windows multi-resolution icon without redesigning it.
[CmdletBinding()]
param(
    [string] $Source = (Join-Path $PSScriptRoot '../docs/assets/app-icon.png'),
    [string] $Destination = (Join-Path $PSScriptRoot '../src/KeeStashDock.Desktop/Assets/app.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
if ($sourcePath -eq $destinationPath) { throw 'Source and destination must differ.' }
if (-not (Test-Path -LiteralPath (Split-Path -Parent $destinationPath) -PathType Container)) {
    throw 'The destination directory must already exist.'
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
$sourceImage = [Drawing.Image]::FromFile($sourcePath)
try {
    if ($sourceImage.Width -ne $sourceImage.Height -or $sourceImage.Width -lt 256) {
        throw 'Use square artwork at least 256 pixels wide.'
    }
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = New-Object Drawing.Imaging.ImageAttributes
        $png = New-Object IO.MemoryStream
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $rectangle = New-Object Drawing.Rectangle(0, 0, $size, $size)
            $graphics.DrawImage($sourceImage, $rectangle, 0, 0, $sourceImage.Width, $sourceImage.Height,
                [Drawing.GraphicsUnit]::Pixel, $attributes)
            $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($png.ToArray())
        }
        finally {
            $png.Dispose()
            $attributes.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally { $sourceImage.Dispose() }

# Modern Windows supports PNG frames in ICO containers. Zero denotes 256 pixels.
$buffer = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($buffer)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($destinationPath, $buffer.ToArray())
}
finally {
    $writer.Dispose()
    $buffer.Dispose()
}
Write-Host ('Exported icon sizes: ' + ($sizes -join ', '))
