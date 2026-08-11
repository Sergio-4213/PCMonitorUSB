param(
    [string]$Source = (Join-Path $PSScriptRoot '..\Windows\PCMonitorServer\Assets\pc-monitor-usb-brand.png')
)

Add-Type -AssemblyName System.Drawing

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourcePath = (Resolve-Path $Source).Path
$windowsAssets = Join-Path $projectRoot 'Windows\PCMonitorServer\Assets'
$androidResources = Join-Path $projectRoot 'Android\app\src\main\res'
$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)

function New-SquarePngBytes([int]$Size) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($sourceImage, 0, 0, $Size, $Size)
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        if ($stream) { $stream.Dispose() }
    }
}

try {
    $androidSizes = @{
        'mipmap-mdpi' = 48
        'mipmap-hdpi' = 72
        'mipmap-xhdpi' = 96
        'mipmap-xxhdpi' = 144
        'mipmap-xxxhdpi' = 192
    }
    foreach ($entry in $androidSizes.GetEnumerator()) {
        $destination = Join-Path (Join-Path $androidResources $entry.Key) 'ic_launcher.png'
        [System.IO.File]::WriteAllBytes($destination, (New-SquarePngBytes $entry.Value))
    }

    [System.IO.File]::WriteAllBytes(
        (Join-Path $windowsAssets 'pc-monitor-usb-256.png'),
        (New-SquarePngBytes 256))

    $iconSizes = @(16, 24, 32, 48, 64, 128, 256)
    $images = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($size in $iconSizes) { $images.Add([byte[]](New-SquarePngBytes $size)) }
    $iconPath = Join-Path $windowsAssets 'pc-monitor-usb.ico'
    $file = [System.IO.File]::Create($iconPath)
    $writer = New-Object System.IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$iconSizes.Count)
        $offset = 6 + (16 * $iconSizes.Count)
        for ($index = 0; $index -lt $iconSizes.Count; $index++) {
            $size = $iconSizes[$index]
            $dimension = if ($size -eq 256) { 0 } else { $size }
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
        foreach ($image in $images) { $writer.Write($image) }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}
finally {
    $sourceImage.Dispose()
}
