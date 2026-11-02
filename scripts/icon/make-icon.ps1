# Draws the app icon (jGlancer.svg, W-93) into the .ico files and the title bar's PNG. Below 64 px the wave is drawn
# thicker, at its true width it would vanish; below 32 px there is none, it only blurs the pupil. Run after changing
# the icon; the results are committed.
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngFrom = 256

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 100.0, $size / 100.0)

    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1f3bff'))
    $white = [System.Drawing.Brushes]::White

    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc(0, 0, 44, 44, 180, 90)
    $bg.AddArc(56, 0, 44, 44, 270, 90)
    $bg.AddArc(56, 56, 44, 44, 0, 90)
    $bg.AddArc(0, 56, 44, 44, 90, 90)
    $bg.CloseFigure()
    $g.FillPath($blue, $bg)

    $eye = New-Object System.Drawing.Drawing2D.GraphicsPath
    $eye.AddBezier(6, 50, 26, 18, 74, 18, 94, 50)
    $eye.AddBezier(94, 50, 74, 82, 26, 82, 6, 50)
    $eye.CloseFigure()
    $g.FillPath($white, $eye)
    $g.FillEllipse($blue, 31, 31, 38, 38)
    if ($size -lt 32) {
        $g.Dispose()
        return $bmp
    }

    $g.TranslateTransform(50, 50)
    $g.RotateTransform(-15)
    $g.TranslateTransform(-50, -50)

    $wave = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wave.AddLine(26, 50, 33, 50)
    $wave.AddBezier(33, 50, 35, 50, 35.5, 45, 37.5, 45)
    $wave.AddBezier(37.5, 45, 41, 45, 41.5, 60, 45, 60)
    $wave.AddBezier(45, 60, 48.5, 60, 51.5, 40, 55, 40)
    $wave.AddBezier(55, 40, 58.5, 40, 59, 55, 62.5, 55)
    $wave.AddBezier(62.5, 55, 64.5, 55, 65, 50, 67, 50)
    $wave.AddLine(67, 50, 74, 50)
    $width = if ($size -lt 64) { 3.4 } else { 1.8 }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $wave)

    $left = New-Object System.Drawing.Drawing2D.GraphicsPath
    $left.AddBezier(30, 46, 31, 48, 32, 49.2, 33.5, 50)
    $left.AddBezier(33.5, 50, 32, 50.8, 31, 52, 30, 54)
    $left.CloseFigure()
    $g.FillPath($white, $left)

    $right = New-Object System.Drawing.Drawing2D.GraphicsPath
    $right.AddBezier(70, 46, 69, 48, 68, 49.2, 66.5, 50)
    $right.AddBezier(66.5, 50, 68, 50.8, 69, 52, 70, 54)
    $right.CloseFigure()
    $g.FillPath($white, $right)

    $g.Dispose()
    return $bmp
}

# One .ico image: PNG for the large size, a 32-bit DIB with an empty mask for the others.
function Icon-Image([System.Drawing.Bitmap]$bmp) {
    $stream = New-Object System.IO.MemoryStream
    if ($bmp.Width -ge $pngFrom) {
        $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    }
    $writer = New-Object System.IO.BinaryWriter $stream
    $side = $bmp.Width
    $maskStride = [int]([math]::Ceiling($side / 32.0)) * 4
    $writer.Write([uint32]40); $writer.Write([int32]$side); $writer.Write([int32]($side * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]($side * $side * 4 + $maskStride * $side))
    $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $side - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $side; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
        }
    }
    $writer.Write((New-Object byte[] ($maskStride * $side)))
    $writer.Flush()
    return , $stream.ToArray()
}

$images = @()
foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    $images += , (Icon-Image $bmp)
    if ($size -eq 32) {
        $bmp.Save((Join-Path $root 'src\JGlancer.App\Assets\icon-32.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) {
    $writer.Write([byte[]]$image)
}
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'src\JGlancer.App\Assets\jGlancer.ico'), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $root 'src\JGlancer.Setup\jGlancer.ico'), $ico.ToArray())
