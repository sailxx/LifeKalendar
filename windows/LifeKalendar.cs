// Календарь-обои для Windows: год точками на рабочем столе и экране блокировки.
// Те же темы и настройки, что в приложении для Android. Сборка: build.cmd (C# 5, без зависимостей).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Календарь-обои")]
[assembly: AssemblyProduct("LifeKalendar")]
[assembly: AssemblyVersion("1.1.0.0")]

// Настройки: те же поля, что в Settings на Android, плюс мониторы. Хранятся в settings.ini.
class Settings
{
    public int Theme;          // 0 чёрная, 1 красная, 2 светлая
    public int Schedule;       // 0 одна тема, 1 как в Windows, 2 по времени
    public int DayTheme = 2;
    public int NightTheme;
    public int DayAt = 7 * 60;      // минуты от полуночи
    public int NightAt = 21 * 60;
    public int Size = 1;       // 0 S, 1 M, 2 L
    public int Place;          // 0 по центру, 1 справа, 2 слева
    public int Lang;           // 0 рус, 1 eng
    public int Target;         // 0 рабочий стол, 1 блокировка, 2 оба
    public int Monitors;       // 0 все, 1 только главный
    public int Enabled;

    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LifeKalendar");
    static string File_ { get { return Path.Combine(Dir, "settings.ini"); } }

    static readonly string[] Keys = { "theme", "schedule", "dayTheme", "nightTheme", "dayAt", "nightAt", "size", "place", "lang", "target", "monitors", "enabled" };

    int[] Values()
    {
        return new[] { Theme, Schedule, DayTheme, NightTheme, DayAt, NightAt, Size, Place, Lang, Target, Monitors, Enabled };
    }

    void Set(string key, int v)
    {
        switch (key)
        {
            case "theme": Theme = v; break;
            case "schedule": Schedule = v; break;
            case "dayTheme": DayTheme = v; break;
            case "nightTheme": NightTheme = v; break;
            case "dayAt": DayAt = v; break;
            case "nightAt": NightAt = v; break;
            case "size": Size = v; break;
            case "place": Place = v; break;
            case "lang": Lang = v; break;
            case "target": Target = v; break;
            case "monitors": Monitors = v; break;
            case "enabled": Enabled = v; break;
        }
    }

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            foreach (var line in File.ReadAllLines(File_))
            {
                int eq = line.IndexOf('=');
                int v;
                if (eq > 0 && int.TryParse(line.Substring(eq + 1).Trim(), out v)) s.Set(line.Substring(0, eq).Trim(), v);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder();
        var v = Values();
        for (int i = 0; i < Keys.Length; i++) sb.Append(Keys[i]).Append('=').Append(v[i]).Append("\r\n");
        File.WriteAllText(File_, sb.ToString());
    }

    // Всё, от чего зависит картинка (кроме даты и темы): если не поменялось — обои не переставляются.
    public string Look() { return string.Format("{0}{1}{2}{3}{4}", Size, Place, Lang, Target, Monitors); }
}

static class Kalendar
{
    class Palette
    {
        public Color Bg, Past, Today, Future, Label;
        public Palette(uint bg, uint past, uint today, uint future, uint label)
        {
            Bg = C(bg); Past = C(past); Today = C(today); Future = C(future); Label = C(label);
        }
        static Color C(uint v) { return Color.FromArgb(unchecked((int)v)); }
    }

    // Те же цвета, что в Calendar.kt.
    static readonly Palette[] Pals =
    {
        new Palette(0xFF1C1C1E, 0xFFF2F2F2, 0xFFFF5A1F, 0x38FFFFFF, 0x99FFFFFF),
        new Palette(0xFFF2380F, 0xFFFFD36B, 0xFFFFFFFF, 0x40000000, 0xB3FFE7B0),
        new Palette(0xFFE6E6E6, 0xFF262626, 0xFFFF5A1F, 0x2E000000, 0x99262626),
    };
    static readonly string[] MonthsRu = { "Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек" };
    static readonly string[] MonthsEn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    public static readonly string[] ThemeNames = { "чёрная", "красная", "светлая" };

    public static bool WindowsDark()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            object v = k == null ? null : k.GetValue("AppsUseLightTheme");
            return v is int && (int)v == 0;
        }
    }

    static bool IsNight(Settings s, DateTime now)
    {
        if (s.Schedule == 1) return WindowsDark();
        if (s.Schedule != 2) return false;
        int t = now.Hour * 60 + now.Minute;
        if (s.DayAt <= s.NightAt) return t < s.DayAt || t >= s.NightAt;
        return t >= s.NightAt && t < s.DayAt;
    }

    public static int ThemeNow(Settings s, DateTime now)
    {
        int t = s.Schedule == 0 ? s.Theme : s.Schedule == 1 ? (IsNight(s, now) ? 0 : 2) : (IsNight(s, now) ? s.NightTheme : s.DayTheme);
        return Math.Max(0, Math.Min(2, t));
    }

    // Мониторы в настоящих пикселях (манифест PerMonitorV2) и общий прямоугольник рабочего стола.
    public static Rectangle[] Screens(Settings s, out Rectangle bounds)
    {
        if (s.Monitors == 1 || Screen.AllScreens.Length == 1)
        {
            bounds = Screen.PrimaryScreen.Bounds;
            return new[] { bounds };
        }
        var list = new List<Rectangle>();
        foreach (var sc in Screen.AllScreens) list.Add(sc.Bounds);
        bounds = SystemInformation.VirtualScreen;
        return list.ToArray();
    }

    // Картинка на все выбранные мониторы: на каждом — своя сетка под его размер.
    public static Bitmap Compose(Settings s, int theme, DateTime date, float scale)
    {
        Rectangle all;
        var screens = Screens(s, out all);
        var bmp = new Bitmap(Math.Max(1, (int)Math.Round(all.Width * scale)), Math.Max(1, (int)Math.Round(all.Height * scale)));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Pals[theme].Bg);
            foreach (var r in screens)
                Draw(g, new RectangleF((r.X - all.X) * scale, (r.Y - all.Y) * scale, r.Width * scale, r.Height * scale), s, theme, date);
        }
        return bmp;
    }

    public static Bitmap Render(Settings s, int theme, DateTime date, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) Draw(g, new RectangleF(0, 0, w, h), s, theme, date);
        return bmp;
    }

    // Сетка: широкий монитор — 4×3 месяца, ультраширокий — 6×2, вертикальный — 3×4. Размеры в шагах точки p.
    static void Draw(Graphics g, RectangleF a, Settings s, int theme, DateTime date)
    {
        var pal = Pals[theme];
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var bg = new SolidBrush(pal.Bg)) g.FillRectangle(bg, a);

        float w = a.Width, h = a.Height;
        bool portrait = h > w;
        int cols = portrait ? 3 : w / h >= 2.1f ? 6 : 4, rows = 12 / cols;
        float uw = cols * 7 + (cols - 1) * 1.7f, uh = rows * 6.5f + (rows - 1) * 1.1f + 2.6f;
        int size = Math.Max(0, Math.Min(2, s.Size));
        float p = portrait ? w * new[] { 0.58f, 0.68f, 0.80f }[size] / uw
            : Math.Min(w * 0.9f / uw, h * new[] { 0.42f, 0.55f, 0.70f }[size] / uh);
        float gw = uw * p, gh = uh * p, r = p * 0.30f;
        float left = a.X + (s.Place == 1 ? w - gw - w * 0.07f : s.Place == 2 ? w * 0.07f : (w - gw) / 2);
        float top = a.Y + (h - gh) / 2 - h * 0.02f;

        var today = date.Date;
        var names = s.Lang == 1 ? MonthsEn : MonthsRu;
        using (var past = new SolidBrush(pal.Past))
        using (var now = new SolidBrush(pal.Today))
        using (var future = new SolidBrush(pal.Future))
        {
            for (int m = 0; m < 12; m++)
            {
                float x0 = left + (m % cols) * 8.7f * p, y0 = top + (m / cols) * 7.6f * p;
                Text(g, names[m], p * 0.78f, m + 1 == today.Month ? pal.Past : pal.Label, x0 + p / 2 - r, y0 + p * 0.78f);
                int days = DateTime.DaysInMonth(today.Year, m + 1);
                for (int d = 1; d <= days; d++)
                {
                    var day = new DateTime(today.Year, m + 1, d);
                    var br = day == today ? now : day < today ? past : future;
                    int i = d - 1;
                    float cx = x0 + (i % 7) * p + p / 2, cy = y0 + 1.5f * p + (i / 7) * p + p / 2;
                    g.FillEllipse(br, cx - r, cy - r, 2 * r, 2 * r);
                }
            }
        }

        // Подпись: сколько дней осталось и процент года.
        int len = DateTime.IsLeapYear(today.Year) ? 366 : 365;
        string ta = s.Lang == 1 ? (len - today.DayOfYear) + "d left" : "осталось " + (len - today.DayOfYear) + " дн.";
        string tb = "  ·  " + (today.DayOfYear * 100 / len) + "%";
        float fs = p * 0.85f, wa = Measure(g, ta, fs), wb = Measure(g, tb, fs);
        float tx = left + (gw - wa - wb) / 2, ty = top + gh - p * 0.6f;
        Text(g, ta, fs, pal.Today, tx, ty);
        Text(g, tb, fs, pal.Label, tx + wa, ty);
    }

    static readonly FontFamily Family = new FontFamily("Segoe UI Semibold");
    static readonly float Ascent = Family.GetCellAscent(FontStyle.Regular) / (float)Family.GetEmHeight(FontStyle.Regular);

    static float Measure(Graphics g, string s, float size)
    {
        using (var f = new Font(Family, Math.Max(1f, size), FontStyle.Regular, GraphicsUnit.Pixel))
            return g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
    }

    // y — базовая линия, как у drawText на Android.
    static void Text(Graphics g, string s, float size, Color c, float x, float baseline)
    {
        using (var f = new Font(Family, Math.Max(1f, size), FontStyle.Regular, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(c))
            g.DrawString(s, f, b, x, baseline - size * Ascent, StringFormat.GenericTypographic);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(int action, int param, string value, int flags);

    static string AppliedFile { get { return Path.Combine(Settings.Dir, "applied"); } }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var f = Path.Combine(Settings.Dir, "log.txt");
            if (File.Exists(f) && new FileInfo(f).Length > 64 * 1024) File.Delete(f);
            File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + "\r\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Ставит обои. Без force — только если сменились дата, тема, мониторы или настройки.
    public static void Apply(Settings s, bool force)
    {
        var now = DateTime.Now;
        int theme = ThemeNow(s, now);
        Rectangle all;
        var screens = Screens(s, out all);
        var sb = new StringBuilder();
        foreach (var r in screens) sb.Append(r.X).Append(',').Append(r.Y).Append(',').Append(r.Width).Append('x').Append(r.Height).Append(';');
        string key = now.ToString("yyyy-MM-dd") + "|" + theme + "|" + sb + "|" + s.Look();
        if (!force && File.Exists(AppliedFile) && File.ReadAllText(AppliedFile).Trim() == key) return;

        Directory.CreateDirectory(Settings.Dir);
        foreach (var old in Directory.GetFiles(Settings.Dir, "wallpaper-*.png")) TryDelete(old);
        foreach (var old in Directory.GetFiles(Settings.Dir, "lock-*.png")) TryDelete(old);
        string stamp = now.ToString("yyyyMMdd-HHmmss");

        if (s.Target != 1)
        {
            // Новое имя файла каждый раз: Windows кэширует обои по пути.
            string file = Path.Combine(Settings.Dir, "wallpaper-" + stamp + ".png");
            using (var bmp = Compose(s, theme, now, 1f)) bmp.Save(file, ImageFormat.Png);
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            {
                // Несколько мониторов — «Растянуть» (22): одна картинка на весь стол, у каждого монитора своя сетка.
                // Один или только главный — «Заполнение» (10).
                k.SetValue("WallpaperStyle", screens.Length > 1 ? "22" : "10");
                k.SetValue("TileWallpaper", "0");
            }
            SystemParametersInfo(20, 0, file, 3);
        }
        if (s.Target != 0)
        {
            var p = Screen.PrimaryScreen.Bounds;
            string file = Path.Combine(Settings.Dir, "lock-" + stamp + ".png");
            using (var bmp = Render(s, theme, now, p.Width, p.Height)) bmp.Save(file, ImageFormat.Png);
            SetLockScreen(file);
        }
        File.WriteAllText(AppliedFile, key);
    }

    static void TryDelete(string f)
    {
        try { File.Delete(f); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Экран блокировки ставится через WinRT LockScreen.SetImageFileAsync — проще всего из PowerShell.
    static void SetLockScreen(string file)
    {
        string script =
            "$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Runtime.WindowsRuntime;" +
            "$m=[System.WindowsRuntimeSystemExtensions].GetMethods()|?{$_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1};" +
            "$op=($m|?{$_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'})[0];" +
            "$ac=($m|?{$_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'})[0];" +
            "[void][Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime];" +
            "[void][Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime];" +
            "$t=$op.MakeGenericMethod([Windows.Storage.StorageFile]).Invoke($null,@([Windows.Storage.StorageFile]::GetFileFromPathAsync('" + file.Replace("'", "''") + "')));" +
            "$t.Wait();$a=$ac.Invoke($null,@([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($t.Result)));$a.Wait()";
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardError = true;
        using (var pr = Process.Start(psi))
        {
            string err = pr.StandardError.ReadToEnd();
            if (!pr.WaitForExit(60000)) pr.Kill();
            if (pr.ExitCode != 0) throw new Exception("Экран блокировки: " + err.Trim());
        }
    }

    // ---------- Автообновление: фоновый режим, запускается при входе в Windows ----------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "LifeKalendar";
    public const string StopEvent = "LifeKalendar.Stop";
    public static string InstalledExe { get { return Path.Combine(Settings.Dir, "LifeKalendar.exe"); } }

    // Копирует программу в %LOCALAPPDATA%\LifeKalendar, ставит ярлык в «Пуск», автозапуск
    // и сразу запускает фоновый режим (если он ещё не работает).
    public static void Enable()
    {
        Directory.CreateDirectory(Settings.Dir);
        string exe = Application.ExecutablePath;
        if (!string.Equals(Path.GetFullPath(exe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Copy(exe, InstalledExe, true); }
            catch (IOException) { } // фоновая копия уже работает и держит файл — оставляем её
        }
        Shortcut();
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            k.SetValue(RunName, "\"" + InstalledExe + "\" --background");
        if (!Background.Running()) Process.Start(InstalledExe, "--background");
    }

    public static void Disable()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            k.DeleteValue(RunName, false);
        // Просим работающий фоновый режим завершиться.
        EventWaitHandle stop;
        if (EventWaitHandle.TryOpenExisting(StopEvent, out stop))
            using (stop) stop.Set();
        TryDelete(AppliedFile);
    }

    // WScript.Shell не сохраняет ярлык с кириллицей в имени — поэтому латиницей.
    static void Shortcut()
    {
        try
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LifeKalendar.lnk");
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { InstalledExe });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Settings.Dir });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        catch (Exception e) { Log("Ярлык: " + (e.InnerException ?? e).Message); }
    }
}

// Фоновый режим без окна: обои меняются в полночь, при смене темы Windows, мониторов,
// после сна и разблокировки. Раз в минуту — проверка на всякий случай (расписание «По времени»).
class Background : ApplicationContext
{
    const string MutexName = "LifeKalendar.Background";
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Control sync = new Control();

    public static bool Running()
    {
        bool created;
        using (new Mutex(false, MutexName, out created)) return !created;
    }

    public static void Run()
    {
        bool first;
        using (new Mutex(true, MutexName, out first))
            if (first) Application.Run(new Background());
    }

    Background()
    {
        sync.CreateControl();
        timer.Interval = 60 * 1000;
        timer.Tick += delegate { Update(); };
        timer.Start();
        SystemEvents.UserPreferenceChanged += Soon;
        SystemEvents.DisplaySettingsChanged += Soon;
        SystemEvents.TimeChanged += Soon;
        SystemEvents.PowerModeChanged += Soon;
        SystemEvents.SessionSwitch += Soon;
        var stop = new EventWaitHandle(false, EventResetMode.AutoReset, Kalendar.StopEvent);
        ThreadPool.RegisterWaitForSingleObject(stop, delegate { sync.BeginInvoke((Action)ExitThread); }, null, -1, true);
        Update();
    }

    // События приходят пачкой (смена темы — несколько UserPreferenceChanged): ждём 2 секунды и проверяем один раз.
    void Soon(object o, EventArgs e)
    {
        sync.BeginInvoke((Action)delegate { timer.Stop(); timer.Interval = 2000; timer.Start(); });
    }

    void Update()
    {
        timer.Interval = 60 * 1000;
        try
        {
            var s = Settings.Load();
            if (s.Enabled == 1) Kalendar.Apply(s, false);
        }
        catch (Exception e) { Kalendar.Log(e.ToString()); }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= Soon;
        SystemEvents.DisplaySettingsChanged -= Soon;
        SystemEvents.TimeChanged -= Soon;
        SystemEvents.PowerModeChanged -= Soon;
        SystemEvents.SessionSwitch -= Soon;
        timer.Stop();
        base.ExitThreadCore();
    }
}

// ---------- Интерфейс: как экран приложения на телефоне ----------

static class Ui
{
    public static readonly Color Bg = Color.FromArgb(0x0E, 0x0E, 0x10);
    public static readonly Color Chip = Color.FromArgb(0x26, 0x26, 0x2A);
    public static readonly Color Dim = Color.FromArgb(0x9A, 0x9A, 0xA0);
    public static readonly Color Accent = Color.FromArgb(0xFF, 0x5A, 0x1F);

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Логотип как на телефоне: тёмная плитка, 4 ряда по 5 точек, сегодня — оранжевая.
    public static Bitmap Logo(int size)
    {
        var b = new Bitmap(size, size);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = size / 64f;
            using (var path = Round(new RectangleF(0, 0, size, size), 14 * k))
            using (var bg = new SolidBrush(Color.FromArgb(0x1C, 0x1C, 0x1E)))
                g.FillPath(bg, path);
            for (int i = 0; i < 20; i++)
            {
                Color c = i < 8 ? Color.FromArgb(0xF2, 0xF2, 0xF2) : i == 8 ? Accent : Color.FromArgb(0x4D, 255, 255, 255);
                using (var br = new SolidBrush(c))
                    g.FillEllipse(br, (12 + (i % 5) * 10 - 3.6f) * k, (17 + (i / 5) * 10 - 3.6f) * k, 7.2f * k, 7.2f * k);
            }
        }
        return b;
    }
}

// Ряд кнопок-чипов: выбранная — белая.
class Chips : Control
{
    readonly string[] items;
    int sel;
    public event Action<int> Picked;

    public Chips(string[] items, int sel)
    {
        this.items = items;
        this.sel = sel;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    public int Selected { get { return sel; } set { sel = value; Invalidate(); } }

    RectangleF Cell(int i)
    {
        float gap = Height * 0.22f, w = (Width - gap * (items.Length - 1)) / items.Length;
        return new RectangleF(i * (w + gap), 0, w, Height - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        for (int i = 0; i < items.Length; i++)
        {
            var r = Cell(i);
            bool on = i == sel;
            using (var p = Ui.Round(r, r.Height / 2))
            using (var b = new SolidBrush(on ? Color.White : Ui.Chip))
                g.FillPath(b, p);
            using (var t = new SolidBrush(on ? Color.Black : Color.White))
                g.DrawString(items[i], Font, t, r, fmt);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (!Cell(i).Contains(e.Location)) continue;
            sel = i;
            Invalidate();
            if (Picked != null) Picked(i);
        }
    }
}

// Большая круглая кнопка: оранжевая — главное действие, серая — остальные.
class Btn : Control
{
    readonly bool primary;
    public event Action Clicked;

    public Btn(string text, bool primary)
    {
        Text = text;
        this.primary = primary;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var p = Ui.Round(r, r.Height / 2))
        using (var b = new SolidBrush(primary ? Ui.Accent : Ui.Chip))
            g.FillPath(b, p);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var t = new SolidBrush(primary ? Color.White : Ui.Dim))
            g.DrawString(Text, Font, t, r, fmt);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (Clicked != null) Clicked();
    }
}

// Поле времени «День с 07:00»: часы и минуты вводятся с клавиатуры.
class TimeBox : Control
{
    readonly TextBox box = new TextBox();
    readonly string label;
    readonly Func<int> get;
    readonly Action<int> set;
    public event Action Changed;

    public TimeBox(string label, Func<int> get, Action<int> set)
    {
        this.label = label;
        this.get = get;
        this.set = set;
        DoubleBuffered = true;
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Ui.Chip;
        box.ForeColor = Color.White;
        box.TextAlign = HorizontalAlignment.Center;
        box.MaxLength = 5;
        box.Leave += delegate { Commit(); };
        box.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; } };
        Controls.Add(box);
        Show();
    }

    new void Show() { box.Text = string.Format("{0:00}:{1:00}", get() / 60, get() % 60); }

    void Commit()
    {
        var parts = box.Text.Replace('.', ':').Split(':');
        int h, m = 0;
        if (int.TryParse(parts[0], out h) && (parts.Length < 2 || int.TryParse(parts[1], out m)) && h >= 0 && h < 24 && m >= 0 && m < 60)
        {
            if (h * 60 + m != get())
            {
                set(h * 60 + m);
                if (Changed != null) Changed();
            }
        }
        Show();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        box.Font = Font;
        int bw = Width * 45 / 100;
        box.SetBounds(Width - bw - Height / 2, (Height - box.PreferredHeight) / 2, bw, box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var p = Ui.Round(r, r.Height / 2))
        using (var b = new SolidBrush(Ui.Chip))
            g.FillPath(b, p);
        var fmt = new StringFormat { LineAlignment = StringAlignment.Center };
        using (var t = new SolidBrush(Ui.Dim))
            g.DrawString(label, Font, t, new RectangleF(Height / 2f, 0, Width, Height), fmt);
    }

    protected override void OnMouseClick(MouseEventArgs e) { box.Focus(); box.SelectAll(); }
}

class Preview : Control
{
    public Bitmap Image;
    public Preview() { DoubleBuffered = true; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Bg);
        if (Image == null) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float k = Math.Min(Width / (float)Image.Width, Height / (float)Image.Height);
        var r = new RectangleF((Width - Image.Width * k) / 2, 0, Image.Width * k, Image.Height * k);
        using (var path = Ui.Round(r, 12 * DeviceDpi / 96f))
        {
            g.SetClip(path);
            g.DrawImage(Image, r);
        }
    }
}

class MainForm : Form
{
    readonly Settings s = Settings.Load();
    readonly float k;
    readonly Preview preview = new Preview();
    readonly Label status = new Label();
    readonly Btn apply, stop, save, folder;
    readonly List<Control[]> rows = new List<Control[]>();   // подпись + элементы группы, сверху вниз
    readonly Dictionary<Control[], Func<bool>> shown = new Dictionary<Control[], Func<bool>>();
    readonly System.Windows.Forms.Timer midnight = new System.Windows.Forms.Timer();
    int busy;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();

    static readonly string[] Themes = { "Чёрная", "Красная", "Светлая" };

    public MainForm()
    {
        k = GetDpiForSystem() / 96f;
        Text = "Календарь-обои";
        BackColor = Ui.Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        using (var b = Ui.Logo(64)) Icon = Icon.FromHandle(b.GetHicon());
        ClientSize = new Size(P(1000), P(720));

        Add(new Label { Text = "Календарь-обои", Font = new Font("Segoe UI Semibold", 20f), AutoSize = true, Location = new Point(P(28), P(22)) });
        Add(new Label { Text = "Год точками на рабочем столе. Обновляется сам каждый день,\nтему можно менять вместе с Windows или по расписанию.", ForeColor = Ui.Dim, AutoSize = true, Location = new Point(P(30), P(66)) });
        preview.SetBounds(P(28), P(120), P(560), P(340));
        Add(preview);

        apply = new Btn("Поставить обои", true) { Font = new Font("Segoe UI Semibold", 11f) };
        apply.SetBounds(P(28), P(484), P(560), P(50));
        apply.Clicked += delegate { ApplyNow(true); };
        Add(apply);
        stop = new Btn("Выключить автообновление", false);
        stop.SetBounds(P(28), P(544), P(560), P(44));
        stop.Clicked += delegate { Run(delegate { Kalendar.Disable(); s.Enabled = 0; s.Save(); }, null); };
        Add(stop);
        save = new Btn("Сохранить PNG…", false);
        save.SetBounds(P(28), P(598), P(275), P(40));
        save.Clicked += SavePng;
        Add(save);
        folder = new Btn("Открыть папку", false);
        folder.SetBounds(P(313), P(598), P(275), P(40));
        folder.Clicked += delegate { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); };
        Add(folder);
        status.SetBounds(P(28), P(650), P(560), P(56));
        status.ForeColor = Ui.Dim;
        status.TextAlign = ContentAlignment.TopCenter;
        Add(status);

        // Правая колонка — те же группы, что на телефоне.
        Group("Смена темы", new[] { "Нет", "Система", "По времени" }, () => s.Schedule, v => { s.Schedule = v; Relayout(); }, null);
        Group("Тема", Themes, () => s.Theme, v => s.Theme = v, () => s.Schedule == 0);
        Group("Днём", Themes, () => s.DayTheme, v => s.DayTheme = v, () => s.Schedule == 2);
        Group("Ночью", Themes, () => s.NightTheme, v => s.NightTheme = v, () => s.Schedule == 2);
        TimeRow();
        Group("Размер", new[] { "S", "M", "L" }, () => s.Size, v => s.Size = v, null);
        Group("Где на экране", new[] { "По центру", "Справа", "Слева" }, () => s.Place, v => s.Place = v, null);
        Group("Подписи", new[] { "Русские", "English" }, () => s.Lang, v => s.Lang = v, null);
        Group("Ставить на", new[] { "Рабочий стол", "Блокировку", "Оба" }, () => s.Target, v => s.Target = v, null);
        Group("Мониторы", new[] { "Все", "Только главный" }, () => s.Monitors, v => s.Monitors = v, () => Screen.AllScreens.Length > 1);
        Relayout();

        // «Система»: тема Windows поменялась — сразу обновляем превью и обои. Мониторы — тоже.
        SystemEvents.UserPreferenceChanged += OnSystemChanged;
        SystemEvents.DisplaySettingsChanged += OnSystemChanged;
        midnight.Interval = 60 * 1000;
        midnight.Tick += delegate { if (s.Enabled == 1) Run(delegate { Kalendar.Apply(s, false); }, null); else Refresh_(); };
        midnight.Start();
        Refresh_();
    }

    int P(int v) { return (int)Math.Round(v * k); }

    void Add(Control c) { Controls.Add(c); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int one = 1;
        DwmSetWindowAttribute(Handle, 20, ref one, 4);   // тёмный заголовок окна
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnSystemChanged;
        SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
        base.OnFormClosed(e);
    }

    void OnSystemChanged(object o, EventArgs e)
    {
        BeginInvoke((Action)delegate { Relayout(); Changed(); });
    }

    Label Caption(string t)
    {
        var l = new Label { Text = t, ForeColor = Ui.Dim, AutoSize = true };
        Add(l);
        return l;
    }

    void Group(string title, string[] items, Func<int> get, Action<int> set, Func<bool> visible)
    {
        var c = new Chips(items, get());
        c.Picked += v => { set(v); Changed(); };
        Add(c);
        var row = new Control[] { Caption(title), c };
        rows.Add(row);
        if (visible != null) shown[row] = visible;
    }

    void TimeRow()
    {
        var day = new TimeBox("День с", () => s.DayAt, v => s.DayAt = v);
        var night = new TimeBox("Ночь с", () => s.NightAt, v => s.NightAt = v);
        day.Changed += Changed;
        night.Changed += Changed;
        Add(day);
        Add(night);
        var row = new Control[] { Caption("Расписание"), day, night };
        rows.Add(row);
        shown[row] = () => s.Schedule == 2;
    }

    // Раскладка правой колонки: скрытые группы не оставляют пустого места.
    void Relayout()
    {
        int x = P(620), w = P(352), y = P(24);
        foreach (var row in rows)
        {
            Func<bool> f;
            bool on = !shown.TryGetValue(row, out f) || f();
            foreach (var c in row) c.Visible = on;
            if (!on) continue;
            row[0].Location = new Point(x + P(2), y);
            if (row.Length == 3)
            {
                row[1].SetBounds(x, y + P(24), (w - P(8)) / 2, P(38));
                row[2].SetBounds(x + (w + P(8)) / 2, y + P(24), (w - P(8)) / 2, P(38));
            }
            else row[1].SetBounds(x, y + P(24), w, P(38));
            y += P(76);
        }
    }

    // Любая настройка сохраняется и, если автообновление включено, сразу ставит обои.
    void Changed()
    {
        s.Save();
        if (s.Enabled == 1) Run(delegate { Kalendar.Apply(s, true); }, null);
        else Refresh_();
    }

    void ApplyNow(bool toast)
    {
        Run(delegate
        {
            Kalendar.Apply(s, true);
            Kalendar.Enable();
            s.Enabled = 1;
            s.Save();
        }, toast ? "Готово: обои стоят и будут обновляться сами." : null);
    }

    // Работа с обоями — в фоне, чтобы окно не подвисало.
    void Run(Action work, string done)
    {
        Interlocked.Increment(ref busy);
        Refresh_();
        ThreadPool.QueueUserWorkItem(delegate
        {
            string err = null;
            try { work(); }
            catch (Exception e) { err = e.Message; Kalendar.Log(e.ToString()); }
            BeginInvoke((Action)delegate
            {
                Interlocked.Decrement(ref busy);
                Refresh_();
                if (err != null) MessageBox.Show(this, "Не удалось поставить обои.\n\n" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (done != null) status.Text = done + "\n" + status.Text;
            });
        });
    }

    void Refresh_()
    {
        var now = DateTime.Now;
        int theme = Kalendar.ThemeNow(s, now);
        Rectangle all;
        Kalendar.Screens(s, out all);
        float scale = Math.Min(preview.Width / (float)all.Width, preview.Height / (float)all.Height) * 1.5f;
        var old = preview.Image;
        preview.Image = Kalendar.Compose(s, theme, now, Math.Min(1f, scale));
        if (old != null) old.Dispose();
        preview.Invalidate();

        bool on = s.Enabled == 1;
        string line = busy > 0 ? "Ставлю обои…" : on ? "Автообновление включено" : "Автообновление выключено — нажми «Поставить обои»";
        status.Text = line + "\nСейчас на обоях: " + Kalendar.ThemeNames[theme] +
            (s.Schedule == 1 ? " · тема Windows: " + (Kalendar.WindowsDark() ? "тёмная" : "светлая") : "") +
            " · " + all.Width + "×" + all.Height;
        stop.Visible = on;
        // Без кнопки «Выключить» нижний ряд поднимается на её место.
        int y = on ? P(598) : P(544);
        save.Top = y;
        folder.Top = y;
        status.Top = y + P(52);
    }

    void SavePng()
    {
        using (var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "life-kalendar.png" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var now = DateTime.Now;
            using (var b = Kalendar.Compose(s, Kalendar.ThemeNow(s, now), now, 1f)) b.Save(d.FileName, ImageFormat.Png);
        }
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // --background: фоновый режим (автозагрузка); --update: один раз проверить и обновить; --render файл.png [ширина высота]: только картинка.
        if (args.Length > 0 && args[0] == "--update")
        {
            try
            {
                var s = Settings.Load();
                if (s.Enabled == 1) Kalendar.Apply(s, false);
                return 0;
            }
            catch (Exception e)
            {
                Kalendar.Log(e.ToString());
                return 1;
            }
        }
        if (args.Length > 0 && args[0] == "--background")
        {
            Background.Run();
            return 0;
        }
        // --install / --uninstall: включить или выключить автообновление без окна (для своих скриптов).
        if (args.Length > 0 && (args[0] == "--install" || args[0] == "--uninstall"))
        {
            try
            {
                var s = Settings.Load();
                if (args[0] == "--install")
                {
                    Kalendar.Apply(s, true);
                    Kalendar.Enable();
                    s.Enabled = 1;
                }
                else
                {
                    Kalendar.Disable();
                    s.Enabled = 0;
                }
                s.Save();
                return 0;
            }
            catch (Exception e)
            {
                Kalendar.Log(e.ToString());
                return 1;
            }
        }
        if (args.Length > 1 && args[0] == "--render")
        {
            var s = Settings.Load();
            var now = DateTime.Now;
            int theme = Kalendar.ThemeNow(s, now);
            using (var b = args.Length > 3 ? Kalendar.Render(s, theme, now, int.Parse(args[2]), int.Parse(args[3])) : Kalendar.Compose(s, theme, now, 1f))
                b.Save(args[1], ImageFormat.Png);
            return 0;
        }

        bool first;
        using (new Mutex(true, "LifeKalendar.Window", out first))
        {
            if (!first) return 0;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
        return 0;
    }
}
