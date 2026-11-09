# Generates Assets\QuickOrbit.ico from vector primitives.
# The build target only calls this when the icon is missing, so the repository
# can ship the generated binary while remaining reproducible from source.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'
$target = Join-Path $assets 'QuickOrbit.ico'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$canvasSize = 256
$canvas = New-Object System.Drawing.Bitmap($canvasSize, $canvasSize)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$tile = New-RoundedPath 12 12 232 232 52
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(20, 20)),
    (New-Object System.Drawing.Point(236, 236)),
    [System.Drawing.Color]::FromArgb(255, 18, 28, 42),
    [System.Drawing.Color]::FromArgb(255, 5, 10, 18))
$g.FillPath($brush, $tile)
$brush.Dispose()

$border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 100, 227, 255), 2)
$g.DrawPath($border, $tile)
$border.Dispose()
$tile.Dispose()

$g.TranslateTransform(128, 128)
$g.RotateTransform(-26)

$rings = @(
    @{ W = 188; H = 78; A = 255; T = 6.0 },
    @{ W = 148; H = 62; A = 180; T = 3.6 },
    @{ W = 108; H = 46; A = 120; T = 2.4 }
)
foreach ($ring in $rings) {
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($ring.A, 100, 227, 255), $ring.T)
    $x = -$ring.W / 2.0
    $y = -$ring.H / 2.0
    $g.DrawEllipse($pen, [float]$x, [float]$y, [float]$ring.W, [float]$ring.H)
    $pen.Dispose()
}

$g.ResetTransform()

$dots = @(
    @{ X = 153; Y = 94; R = 10; A = 255 },
    @{ X = 97; Y = 165; R = 7; A = 220 },
    @{ X = 74; Y = 121; R = 5; A = 170 }
)
foreach ($dot in $dots) {
    $glow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(55, 100, 227, 255))
    $g.FillEllipse($glow, $dot.X - ($dot.R * 2.2), $dot.Y - ($dot.R * 2.2), $dot.R * 4.4, $dot.R * 4.4)
    $glow.Dispose()
    $core = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($dot.A, 160, 242, 255))
    $g.FillEllipse($core, $dot.X - $dot.R, $dot.Y - $dot.R, $dot.R * 2, $dot.R * 2)
    $core.Dispose()
}

$g.Dispose()

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]
foreach ($size in $sizes) {
    $scaled = New-Object System.Drawing.Bitmap($size, $size)
    $sg = [System.Drawing.Graphics]::FromImage($scaled)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.DrawImage($canvas, 0, 0, $size, $size)
    $sg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $scaled.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($ms.ToArray())
    $ms.Dispose()
    $scaled.Dispose()
}
$canvas.Dispose()

$stream = [System.IO.File]::Create($target)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) {
    $writer.Write($image)
}
$writer.Flush()
$writer.Dispose()
$stream.Dispose()

Write-Host "Generated $target"
