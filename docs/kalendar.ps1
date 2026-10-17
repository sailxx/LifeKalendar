# Календарь-обои для Windows: год точками на рабочем столе под разрешение главного монитора.
# Установка: kalendar.ps1 -Install [-Theme 0..3] [-Size 0..2] [-Place 0..2] [-Lang 0..1]
# Удаление: kalendar.ps1 -Uninstall. Картинка без смены обоев: kalendar.ps1 -Out file.png [-Width 1920 -Height 1080]
param(
    [switch]$Install,
    [switch]$Uninstall,
    [int]$Theme = 0,   # 0 чёрная, 1 красная, 2 светлая, 3 как в Windows
    [int]$Size = 1,    # 0 S, 1 M, 2 L
    [int]$Place = 0,   # 0 по центру, 1 справа, 2 слева
    [int]$Lang = 0,    # 0 рус, 1 eng
    [string]$Out,
    [int]$Width = 0,
    [int]$Height = 0
)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'LifeKalendar'
$conf = Join-Path $dir 'settings.json'
$self = Join-Path $dir 'kalendar.ps1'
$taskName = 'LifeKalendar'

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $dir 'applied') -ErrorAction SilentlyContinue
    Write-Host 'Автообновление выключено. Обои останутся, пока не сменишь их.'
    return
}

if ($Install) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    [ordered]@{ theme = $Theme; size = $Size; place = $Place; lang = $Lang } | ConvertTo-Json | Set-Content $conf -Encoding UTF8
    if ($PSCommandPath -ne $self) { Copy-Item $PSCommandPath $self -Force }
    Remove-Item (Join-Path $dir 'applied') -ErrorAction SilentlyContinue
    # Без окна: conhost --headless. Раз в 10 минут проверяет дату, тему Windows и разрешение — обои меняются, только если что-то изменилось.
    $act = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$self`""
    $logon = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $every = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 10)
    $set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $act -Trigger $logon, $every -Settings $set -Force | Out-Null
    & $self
    Write-Host 'Готово: обои стоят и будут обновляться каждый день.'
    return
}

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class KalendarNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(int a, int b, string c, int d);
}
'@

if (-not $Out -and (Test-Path $conf)) {
    $c = Get-Content $conf -Raw -Encoding UTF8 | ConvertFrom-Json
    $Theme = $c.theme; $Size = $c.size; $Place = $c.place; $Lang = $c.lang
}
if ($Width -le 0 -or $Height -le 0) {
    # Настоящие пиксели монитора, без масштабирования Windows.
    [KalendarNative]::SetProcessDPIAware() | Out-Null
    $Width = [KalendarNative]::GetSystemMetrics(0)
    $Height = [KalendarNative]::GetSystemMetrics(1)
}
if ($Theme -eq 3) {
    $light = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue).AppsUseLightTheme
    $Theme = if ($light -eq 0) { 0 } else { 2 }
}
$Theme = [Math]::Max(0, [Math]::Min(2, $Theme))
$Size = [Math]::Max(0, [Math]::Min(2, $Size))

$date = Get-Date
$key = '{0:yyyy-MM-dd}|{1}|{2}x{3}|{4}{5}{6}' -f $date, $Theme, $Width, $Height, $Size, $Place, $Lang
$appliedFile = Join-Path $dir 'applied'
if (-not $Out -and (Test-Path $appliedFile) -and (Get-Content $appliedFile -Raw).Trim() -eq $key) { return }

function Col([string]$h) {
    $v = [Convert]::ToUInt32($h, 16)
    [System.Drawing.Color]::FromArgb([int](($v -shr 24) -band 255), [int](($v -shr 16) -band 255), [int](($v -shr 8) -band 255), [int]($v -band 255))
}
# Те же цвета, что в приложении для Android: фон, прошедшие, сегодня, будущие, подписи.
$pal = @(
    @('FF1C1C1E', 'FFF2F2F2', 'FFFF5A1F', '38FFFFFF', '99FFFFFF'),
    @('FFF2380F', 'FFFFD36B', 'FFFFFFFF', '40000000', 'B3FFE7B0'),
    @('FFE6E6E6', 'FF262626', 'FFFF5A1F', '2E000000', '99262626')
)[$Theme] | ForEach-Object { Col $_ }
$months = if ($Lang -eq 1) { 'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec' }
          else { 'Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн', 'Июл', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек' }

# Сетка: широкий монитор — 4×3 месяца, ультраширокий — 6×2, вертикальный — 3×4. Размеры в шагах точки.
$w = $Width; $h = $Height
$cols = if ($h -gt $w) { 3 } elseif ($w / $h -ge 2.1) { 6 } else { 4 }
$rows = 12 / $cols
$unitsW = $cols * 7 + ($cols - 1) * 1.7
$unitsH = $rows * 6.5 + ($rows - 1) * 1.1 + 2.6
$p = if ($h -gt $w) { $w * (0.58, 0.68, 0.80)[$Size] / $unitsW }
     else { [Math]::Min($w * 0.9 / $unitsW, $h * (0.42, 0.55, 0.70)[$Size] / $unitsH) }
$gw = $unitsW * $p; $gh = $unitsH * $p
$left = switch ($Place) { 1 { $w - $gw - $w * 0.07 } 2 { $w * 0.07 } default { ($w - $gw) / 2 } }
$top = ($h - $gh) / 2 - $h * 0.02
$r = $p * 0.30

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear($pal[0])
$fmt = [System.Drawing.StringFormat]::GenericTypographic
$family = New-Object System.Drawing.FontFamily('Segoe UI Semibold')
$ascent = $family.GetCellAscent('Regular') / $family.GetEmHeight('Regular')
function Text([string]$s, [double]$size, $color, [double]$x, [double]$baseline) {
    $f = New-Object System.Drawing.Font($family, [single]$size, 'Regular', 'Pixel')
    $b = New-Object System.Drawing.SolidBrush($color)
    $g.DrawString($s, $f, $b, [single]$x, [single]($baseline - $size * $ascent), $fmt)
    $width = $g.MeasureString($s, $f, (New-Object System.Drawing.PointF(0, 0)), $fmt).Width
    $f.Dispose(); $b.Dispose()
    $width
}

$brushes = $pal | ForEach-Object { New-Object System.Drawing.SolidBrush($_) }
$today = $date.Date
$year = $today.Year
for ($m = 0; $m -lt 12; $m++) {
    $x0 = $left + ($m % $cols) * (7 * $p + 1.7 * $p)
    $y0 = $top + [Math]::Floor($m / $cols) * (6.5 * $p + 1.1 * $p)
    $lc = if ($m + 1 -eq $today.Month) { $pal[1] } else { $pal[4] }
    Text $months[$m] ($p * 0.78) $lc ($x0 + $p / 2 - $r) ($y0 + $p * 0.78) | Out-Null
    $days = [DateTime]::DaysInMonth($year, $m + 1)
    for ($d = 1; $d -le $days; $d++) {
        $day = New-Object DateTime($year, ($m + 1), $d)
        $br = if ($day -eq $today) { $brushes[2] } elseif ($day -lt $today) { $brushes[1] } else { $brushes[3] }
        $i = $d - 1
        $cx = $x0 + ($i % 7) * $p + $p / 2
        $cy = $y0 + 1.5 * $p + [Math]::Floor($i / 7) * $p + $p / 2
        $g.FillEllipse($br, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
    }
}

# Подпись под сеткой: сколько дней осталось и процент года.
$len = if ([DateTime]::IsLeapYear($year)) { 366 } else { 365 }
$rest = $len - $today.DayOfYear
$pct = [Math]::Floor($today.DayOfYear * 100 / $len)
$a = if ($Lang -eq 1) { "${rest}d left" } else { "осталось $rest дн." }
$b = "  ·  $pct%"
$fs = $p * 0.85
$probe = New-Object System.Drawing.Font($family, [single]$fs, 'Regular', 'Pixel')
$wa = $g.MeasureString($a, $probe, (New-Object System.Drawing.PointF(0, 0)), $fmt).Width
$wb = $g.MeasureString($b, $probe, (New-Object System.Drawing.PointF(0, 0)), $fmt).Width
$probe.Dispose()
$ty = $top + $gh - $p * 0.6
$tx = $left + ($gw - $wa - $wb) / 2
Text $a $fs $pal[2] $tx $ty | Out-Null
Text $b $fs $pal[4] ($tx + $wa) $ty | Out-Null
$g.Dispose()

if ($Out) {
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return
}

# Новое имя файла каждый день: Windows кэширует обои по пути.
New-Item -ItemType Directory -Force $dir | Out-Null
Get-ChildItem $dir -Filter 'wallpaper-*.png' | Remove-Item -ErrorAction SilentlyContinue
$file = Join-Path $dir ('wallpaper-{0:yyyyMMdd-HHmmss}.png' -f $date)
$bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$desk = 'HKCU:\Control Panel\Desktop'
Set-ItemProperty $desk WallpaperStyle '10'   # «Заполнение»: на втором мониторе другого размера края — просто фон
Set-ItemProperty $desk TileWallpaper '0'
[KalendarNative]::SystemParametersInfo(20, 0, $file, 3) | Out-Null
Set-Content $appliedFile $key
