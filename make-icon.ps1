# Draws StartScreen.ico: the Start-screen tile layout (one big tile, two small, one wide strip) on a flat colour.
# Every size is drawn on a 16-unit grid with pixel-snapped rectangles (no anti-aliasing), so it stays crisp.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Set-Location $PSScriptRoot

$bg = [System.Drawing.Color]::FromArgb(96, 96, 96)    # neutral gray accent (was green)
function Mix($a, $t) {   # white over the background at opacity t, as a solid colour
    [System.Drawing.Color]::FromArgb(
        [int]($bg.R + (255 - $bg.R) * $t), [int]($bg.G + (255 - $bg.G) * $t), [int]($bg.B + (255 - $bg.B) * $t))
}
# x, y, w, h on the 16-unit grid (2-unit margin, 1-unit gaps), and tile opacity
$tiles = @(
    @(2, 2, 7, 7, 1.00),
    @(10, 2, 4, 3, 0.72),
    @(10, 6, 4, 3, 0.50),
    @(2, 10, 12, 4, 0.86)
)

function Render($size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.Clear($bg)
    $u = $size / 16.0
    foreach ($t in $tiles) {
        $x0 = [int][math]::Round($t[0] * $u); $y0 = [int][math]::Round($t[1] * $u)
        $x1 = [int][math]::Round(($t[0] + $t[2]) * $u); $y1 = [int][math]::Round(($t[1] + $t[3]) * $u)
        $br = New-Object System.Drawing.SolidBrush (Mix $bg $t[4])
        $g.FillRectangle($br, $x0, $y0, ($x1 - $x0), ($y1 - $y0))
        $br.Dispose()
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -in 32, 256) { $bmp.Save("$PSScriptRoot\icon-preview-$s.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose(); $ms.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes("$PSScriptRoot\StartScreen.ico", $out.ToArray())
Write-Host ("Wrote StartScreen.ico ({0} sizes, {1:N1} KB)" -f $sizes.Count, ($out.Length / 1KB))
