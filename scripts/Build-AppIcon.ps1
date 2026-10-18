param(
    [string]$Source = "$PSScriptRoot\..\DellSmmFanControl\Assets\fan-master.png",
    [string]$Destination = "$PSScriptRoot\..\DellSmmFanControl\Assets\fan.ico"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source))
$frames = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
function ConvertTo-IconDib([System.Drawing.Bitmap]$Image) {
    # Classic 32-bit BGRA + AND mask for small shell/tray icons. Some GDI
    # consumers cannot correctly convert PNG-compressed small ICO frames.
    $size = $Image.Width
    $maskStride = [int][Math]::Ceiling($size / 32.0) * 4
    $mask = [byte[]]::new($maskStride * $size)
    $dibStream = [System.IO.MemoryStream]::new()
    $dib = [System.IO.BinaryWriter]::new($dibStream)
    try {
        $dib.Write([uint32]40)
        $dib.Write([int32]$size)
        $dib.Write([int32]($size * 2))
        $dib.Write([uint16]1)
        $dib.Write([uint16]32)
        $dib.Write([uint32]0)
        $dib.Write([uint32]($size * $size * 4))
        $dib.Write([int32]0)
        $dib.Write([int32]0)
        $dib.Write([uint32]0)
        $dib.Write([uint32]0)
        for ($row = 0; $row -lt $size; $row++) {
            for ($column = 0; $column -lt $size; $column++) {
                $pixel = $Image.GetPixel($column, $size - 1 - $row)
                $dib.Write([byte]$pixel.B)
                $dib.Write([byte]$pixel.G)
                $dib.Write([byte]$pixel.R)
                $dib.Write([byte]$pixel.A)
                if ($pixel.A -eq 0) {
                    $maskIndex = $row * $maskStride + [int][Math]::Floor($column / 8.0)
                    $mask[$maskIndex] = $mask[$maskIndex] -bor (0x80 -shr ($column % 8))
                }
            }
        }
        $dib.Write($mask)
        return ,$dibStream.ToArray()
    }
    finally { $dib.Dispose() }
}
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $png = [System.IO.MemoryStream]::new()
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($sourceImage, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            if ($size -eq 256) {
                $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($png.ToArray())
            }
            else { $frames.Add((ConvertTo-IconDib $bitmap)) }
        }
        finally { $png.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $sourceImage.Dispose() }

$output = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Destination))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally { $writer.Dispose() }
