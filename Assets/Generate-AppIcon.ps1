<#!
.SYNOPSIS
Builds the island mark at Windows icon sizes, with transparent corners and a high-contrast waveform.
#>
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $capsule = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $capsule.AddArc(12, 56, 144, 144, 90, 180)
    $capsule.AddArc(100, 56, 144, 144, 270, 180)
    $capsule.CloseFigure()
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 15, 17, 21))
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 226, 232, 240), 10)
    $graphics.FillPath($fill, $capsule)
    $graphics.DrawPath($outline, $capsule)
    $wave = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 82, 225, 210), 16)
    $wave.StartCap = $wave.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $heights = @(28, 60, 88, 52, 24)
    for ($i = 0; $i -lt $heights.Count; $i++) {
        $x = 72 + $i * 28
        $graphics.DrawLine($wave, $x, 128 - $heights[$i] / 2, $x, 128 + $heights[$i] / 2)
    }
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'app-icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose()
    $wave.Dispose()
    $outline.Dispose()
    $fill.Dispose()
    $capsule.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
# ICO directory entries point at PNG frames; Windows selects the closest DPI size.
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'app.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
