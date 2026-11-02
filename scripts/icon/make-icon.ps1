# Draws the app icon (W-104) into the .ico files and the title bar's PNG: jGlancer.svg from 40 px up, below that
# jGlancer-small.svg, whose wave is drawn thicker, because the fine lines of the full drawing melt together. The SVGs use
# blurs and blend modes, so Edge renders them at full size and this script scales them down. Run after changing the icon;
# the results are committed.
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$smallBelow = 40
$pngFrom = 256
$canvas = 1024
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'

function Render-Svg([string]$name) {
    $work = Join-Path $env:TEMP ('jglancer-icon-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $work | Out-Null
    $page = Join-Path $work 'page.html'
    $source = ([Uri](Join-Path $PSScriptRoot $name)).AbsoluteUri
    Set-Content -LiteralPath $page -Encoding utf8 "<!doctype html><html style='background:transparent'><body style='margin:0;background:transparent'><img src='$source' width=$canvas height=$canvas style='display:block'></body></html>"
    $png = Join-Path $work 'out.png'
    $process = Start-Process $edge -PassThru -ArgumentList '--headless=new', '--disable-gpu', '--allow-file-access-from-files',
        '--hide-scrollbars', '--force-device-scale-factor=1', '--default-background-color=00000000', "--window-size=$canvas,$canvas",
        '--virtual-time-budget=8000', "--user-data-dir=$work\profile", "--screenshot=$png", ([Uri]$page).AbsoluteUri
    $process.WaitForExit()
    $bytes = [System.IO.File]::ReadAllBytes($png)
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    $bmp = [System.Drawing.Bitmap]::FromStream((New-Object System.IO.MemoryStream (, $bytes)))
    if ($bmp.Width -ne $canvas -or $bmp.Height -ne $canvas) { throw "Edge drew $name at $($bmp.Width)x$($bmp.Height)" }
    return $bmp
}

$full = Render-Svg 'jGlancer.svg'
$small = Render-Svg 'jGlancer-small.svg'

function Draw-Icon([int]$size) {
    $source = if ($size -lt $smallBelow) { $small } else { $full }
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
    $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $target = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $g.DrawImage($source, $target, 0, 0, $canvas, $canvas, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
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
