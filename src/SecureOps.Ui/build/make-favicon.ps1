# Regenerates wwwroot/brand/favicon.ico. Run from anywhere:
#   powershell -File src/SecureOps.Ui/build/make-favicon.ps1
#
# Lives outside wwwroot so it is never served as a static file. Not part of the build:
# the .ico is committed, and this exists so the next person can reproduce it rather
# than redraw it. Keep it in step with wwwroot/brand/favicon.svg by hand.
#
# At 16 px the shield outline closes into a blob, so that size drops the shield and
# keeps a bolder check — the red plate already carries the identity.
Add-Type -AssemblyName System.Drawing

$brand = [System.Drawing.Color]::FromArgb(255, 0xB8, 0x1D, 0x2B)
$white = [System.Drawing.Color]::White

function New-Plate([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [Math]::Max(2, [int]($s * 7 / 32))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush($brand)
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()

    $k = $s / 32.0
    $penW = [Math]::Max(1.6, 2.1 * $k)
    $pen = New-Object System.Drawing.Pen($white, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    if ($s -ge 24) {
        # Shield outline, matching the SVG path proportions.
        $shield = @(
            (New-Object System.Drawing.PointF((16 * $k), (6.4 * $k))),
            (New-Object System.Drawing.PointF((8.6 * $k), (9.3 * $k))),
            (New-Object System.Drawing.PointF((8.6 * $k), (15.4 * $k)))
        )
        $g.DrawLines($pen, [System.Drawing.PointF[]]$shield)
        $g.DrawArc($pen, (8.6 * $k), (8.0 * $k), (14.8 * $k), (16.8 * $k), 20, 140)
        $right = @(
            (New-Object System.Drawing.PointF((16 * $k), (6.4 * $k))),
            (New-Object System.Drawing.PointF((23.4 * $k), (9.3 * $k))),
            (New-Object System.Drawing.PointF((23.4 * $k), (15.4 * $k)))
        )
        $g.DrawLines($pen, [System.Drawing.PointF[]]$right)

        $check = @(
            (New-Object System.Drawing.PointF((12.9 * $k), (15.7 * $k))),
            (New-Object System.Drawing.PointF((15.3 * $k), (18.1 * $k))),
            (New-Object System.Drawing.PointF((19.7 * $k), (13.4 * $k)))
        )
        $g.DrawLines($pen, [System.Drawing.PointF[]]$check)
    }
    else {
        # 16px: check only, drawn heavier so it survives downsampling.
        $pen.Width = [Math]::Max(2.0, 3.0 * $k)
        $check = @(
            (New-Object System.Drawing.PointF((10.0 * $k), (16.4 * $k))),
            (New-Object System.Drawing.PointF((14.2 * $k), (20.6 * $k))),
            (New-Object System.Drawing.PointF((22.2 * $k), (11.6 * $k)))
        )
        $g.DrawLines($pen, [System.Drawing.PointF[]]$check)
    }

    $pen.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = @(16, 32, 48)
$payloads = @()
foreach ($s in $sizes) {
    $bmp = New-Plate $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$payloads[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $payloads[$i].Length
}
foreach ($p in $payloads) { $bw.Write($p) }
$bw.Flush()

$target = Join-Path $PSScriptRoot '..\wwwroot\brand\favicon.ico'
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
$bw.Dispose(); $out.Dispose()
Write-Output "wrote $target ($((Get-Item $target).Length) bytes, sizes: $($sizes -join ', '))"
