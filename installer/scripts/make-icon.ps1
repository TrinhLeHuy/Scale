# Regenerate src\TTC.Weighing.WinForms\Assets\logo.ico from Assets\logo.png.
#   powershell -ExecutionPolicy Bypass -File installer\scripts\make-icon.ps1
# The logo is padded to a square (transparent) and stored as PNG frames 16..256 px.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\TTC.Weighing.WinForms\Assets'
$src = New-Object Drawing.Bitmap (Join-Path $assets 'logo.png')

# Crop the white/transparent margin (same rule as LogoHelper.GetContentBounds), sampled every 2 px.
$data = $src.LockBits((New-Object Drawing.Rectangle 0, 0, $src.Width, $src.Height),
    [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $src.Height)
[Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$stride = $data.Stride
$src.UnlockBits($data)
$top = $src.Height; $bottom = 0; $left = $src.Width; $right = 0
for ($y = 0; $y -lt $src.Height; $y += 2) {
    $row = $y * $stride
    for ($x = 0; $x -lt $src.Width; $x += 2) {
        $i = $row + $x * 4
        if ($bytes[$i + 3] -gt 20 -and ($bytes[$i + 2] -lt 240 -or $bytes[$i + 1] -lt 240 -or $bytes[$i] -lt 240)) {
            if ($y -lt $top) { $top = $y }; if ($y -gt $bottom) { $bottom = $y }
            if ($x -lt $left) { $left = $x }; if ($x -gt $right) { $right = $x }
        }
    }
}
$crop = if ($top -le $bottom) {
    New-Object Drawing.Rectangle $left, $top, ([Math]::Min($src.Width - $left, $right - $left + 2)), ([Math]::Min($src.Height - $top, $bottom - $top + 2))
} else { New-Object Drawing.Rectangle 0, 0, $src.Width, $src.Height }
Write-Host "Logo content: $crop"

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
try {
    foreach ($size in $sizes) {
        $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([Drawing.Color]::Transparent)
        $scale = [Math]::Min($size / $crop.Width, $size / $crop.Height)
        $w = [int][Math]::Round($crop.Width * $scale); $h = [int][Math]::Round($crop.Height * $scale)
        $dest = New-Object Drawing.Rectangle ([int](($size - $w) / 2)), ([int](($size - $h) / 2)), $w, $h
        $g.DrawImage($src, $dest, $crop, [Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $frames += , @($size, $ms.ToArray())
    }
}
finally { $src.Dispose() }

$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $bw.Write([byte]$s); $bw.Write([byte]$s); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$f[1].Length); $bw.Write([UInt32]$offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f[1]) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'logo.ico'), $out.ToArray())
Write-Host "Wrote $(Join-Path $assets 'logo.ico')"
