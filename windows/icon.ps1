# Иконка программы: тот же логотип, что у приложения на телефоне. powershell -File icon.ps1 → LifeKalendar.ico
Add-Type -AssemblyName System.Drawing
function Round($r, $rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $rad * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90); $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90); $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}
$pngs = foreach ($n in 16, 24, 32, 48, 64, 256) {
    $b = New-Object System.Drawing.Bitmap($n, $n); $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $k = $n / 64
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 28, 30))), (Round (New-Object System.Drawing.RectangleF(0, 0, $n, $n)) (14 * $k)))
    for ($i = 0; $i -lt 20; $i++) {
        $c = if ($i -lt 8) { [System.Drawing.Color]::FromArgb(242, 242, 242) } elseif ($i -eq 8) { [System.Drawing.Color]::FromArgb(255, 90, 31) } else { [System.Drawing.Color]::FromArgb(77, 255, 255, 255) }
        $g.FillEllipse((New-Object System.Drawing.SolidBrush($c)), [single]((12 + ($i % 5) * 10 - 3.6) * $k), [single]((17 + [Math]::Floor($i / 5) * 10 - 3.6) * $k), [single](7.2 * $k), [single](7.2 * $k))
    }
    $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , @($n, $ms.ToArray())
}
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$off = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$p[1].Length); $w.Write([uint32]$off); $off += $p[1].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'LifeKalendar.ico'), $out.ToArray())
