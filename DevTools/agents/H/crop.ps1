param($src, $dst, [int]$x, [int]$y, [int]$w, [int]$h, [int]$s = 3)
Add-Type -AssemblyName System.Drawing
$b = [System.Drawing.Bitmap]::FromFile($src)
$o = New-Object System.Drawing.Bitmap ($w * $s), ($h * $s)
$g = [System.Drawing.Graphics]::FromImage($o)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($b, (New-Object System.Drawing.Rectangle 0, 0, ($w * $s), ($h * $s)), (New-Object System.Drawing.Rectangle $x, $y, $w, $h), [System.Drawing.GraphicsUnit]::Pixel)
$o.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $o.Dispose(); $b.Dispose()
