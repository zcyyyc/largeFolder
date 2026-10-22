$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The product mark is a teal rounded square with a silver folder tab and
# four silver tiles in a white folder. Keep all geometry editable here.
function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc(($x + $w - $diameter), $y, $diameter, $diameter, 270, 90)
    $path.AddArc(($x + $w - $diameter), ($y + $h - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($x, ($y + $h - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundedBox($graphics, $x, $y, $w, $h, $radius, $color) {
    $path = New-RoundedPath $x $y $w $h $radius
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($color))
    try { $graphics.FillPath($brush, $path) }
    finally { $brush.Dispose(); $path.Dispose() }
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
        Fill-RoundedBox $graphics 4 4 248 248 54 '#008080'
        Fill-RoundedBox $graphics 50 48 104 58 14 '#C0C0C0'
        Fill-RoundedBox $graphics 28 82 200 136 20 '#FFFFFF'
        Fill-RoundedBox $graphics 50 108 66 40 8 '#C0C0C0'
        Fill-RoundedBox $graphics 140 108 66 40 8 '#C0C0C0'
        Fill-RoundedBox $graphics 50 168 66 28 8 '#C0C0C0'
        Fill-RoundedBox $graphics 140 168 66 28 8 '#C0C0C0'
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

function Get-PngBytes($source, [int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, 0, 0, $size, $size)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$docsDirectory = Join-Path $projectRoot 'docs'
[System.IO.Directory]::CreateDirectory($docsDirectory) | Out-Null
$previewPath = Join-Path $docsDirectory 'app-icon.png'
$iconPath = Join-Path $PSScriptRoot 'LargeFolder.ico'
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$master = New-IconBitmap 1024
try {
    [System.IO.File]::WriteAllBytes($previewPath, (Get-PngBytes $master 512))
    $images = @($sizes | ForEach-Object { ,(Get-PngBytes $master $_) })
    $stream = [System.IO.File]::Create($iconPath)
    $writer = New-Object System.IO.BinaryWriter $stream
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }
        foreach ($png in $images) { $writer.Write([byte[]]$png) }
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}
finally { $master.Dispose() }

Write-Output ('Created {0} icon sizes: {1}' -f $sizes.Count, ($sizes -join ', '))
Write-Output $iconPath
Write-Output $previewPath
