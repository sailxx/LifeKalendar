// Установщик Календаря-обоев для Windows: кладёт программу в %LOCALAPPDATA%\Programs\LifeKalendar,
// делает ярлыки, запись в «Приложения» и запускает её. Без прав администратора.
// Сборка: build.cmd (LifeKalendar.exe вшивается внутрь как ресурс app.exe). Удаление: Uninstall.exe рядом с программой.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Установщик Календаря-обоев")]
[assembly: AssemblyProduct("LifeKalendar")]
[assembly: AssemblyVersion("1.7.0.0")]

static class Setup
{
    const string Name = "LifeKalendar";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LifeKalendar";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);
    static string Exe { get { return Path.Combine(Dir, "LifeKalendar.exe"); } }
    static string Uninstaller { get { return Path.Combine(Dir, "Uninstall.exe"); } }
    static string StartLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LifeKalendar.lnk"); } }
    static string DeskLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LifeKalendar.lnk"); } }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool un = Path.GetFileName(Application.ExecutablePath).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase)
                  || (args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        bool quiet = args.Length > 0 && args[args.Length - 1].Equals("/quiet", StringComparison.OrdinalIgnoreCase);
        Application.Run(new SetupForm(un, quiet));
        return 0;
    }

    static void StopApp()
    {
        foreach (var p in Process.GetProcessesByName(Name))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
    }

    static void Link(string path, string target)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
        var lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Dir });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Календарь-обои" });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    public static string Install()
    {
        StopApp();
        Directory.CreateDirectory(Dir);
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.exe"))
        using (var f = new FileStream(Exe, FileMode.Create, FileAccess.Write))
            res.CopyTo(f);
        File.Copy(Application.ExecutablePath, Uninstaller, true);
        try { File.Delete(Exe + ".old"); } catch { }

        Link(StartLink, Exe);
        Link(DeskLink, Exe);

        string ver = AssemblyName.GetAssemblyName(Exe).Version.ToString(2);
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "Календарь-обои (LifeKalendar)");
            k.SetValue("DisplayVersion", ver);
            k.SetValue("Publisher", "sailxx");
            k.SetValue("InstallLocation", Dir);
            k.SetValue("DisplayIcon", Exe);
            k.SetValue("UninstallString", "\"" + Uninstaller + "\"");
            k.SetValue("URLInfoAbout", "https://github.com/sailxx/LifeKalendar");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // Автозапуск уже был включён у старой копии — переводим на новое место.
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            if (k.GetValue(Name) != null) k.SetValue(Name, "\"" + Exe + "\" --tray");

        // Старая копия из версии 1.1–1.x, лежавшая рядом с настройками, больше не нужна.
        string oldCopy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "LifeKalendar.exe");
        try { File.Delete(oldCopy); } catch { }
        return ver;
    }

    public static void Launch()
    {
        Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = Dir, UseShellExecute = true });
    }

    public static void Uninstall()
    {
        StopApp();
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(Name, false);
        try { File.Delete(StartLink); } catch { }
        try { File.Delete(DeskLink); } catch { }
        Registry.CurrentUser.DeleteSubKey(UninstallKey, false);
        // Папку с программой удаляем снаружи: Uninstall.exe лежит внутри неё и пока запущен.
        // Настройки и картинки в %LOCALAPPDATA%\LifeKalendar остаются.
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false });
    }
}

class SetupForm : Form
{
    static readonly Color Bg = Color.FromArgb(0x0E, 0x0E, 0x10), Dim = Color.FromArgb(0x9A, 0x9A, 0xA0), Accent = Color.FromArgb(0xFF, 0x5A, 0x1F);
    readonly Label status = new Label();
    readonly Button go = new Button();
    readonly bool un;

    public SetupForm(bool un, bool quiet)
    {
        this.un = un;
        Text = un ? "Удаление Календаря-обоев" : "Установка Календаря-обоев";
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 230);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Controls.Add(new Label { Text = "Календарь-обои", Font = new Font("Segoe UI Semibold", 20f), AutoSize = true, Location = new Point(28, 24) });
        Controls.Add(new Label
        {
            Text = un ? "Программа будет удалена. Настройки и картинки останутся в папке LifeKalendar."
                      : "Год точками на рабочем столе. Ставится для текущего пользователя, права администратора не нужны.",
            ForeColor = Dim, Location = new Point(30, 74), Size = new Size(380, 50)
        });
        status.ForeColor = Dim;
        status.SetBounds(30, 130, 380, 24);
        Controls.Add(status);

        go.Text = un ? "Удалить" : "Установить";
        go.Font = new Font("Segoe UI Semibold", 11f);
        go.FlatStyle = FlatStyle.Flat;
        go.FlatAppearance.BorderSize = 0;
        go.BackColor = Accent;
        go.ForeColor = Color.White;
        go.Cursor = Cursors.Hand;
        go.SetBounds(30, 164, 380, 44);
        go.Click += delegate { Start(); };
        Controls.Add(go);
        AcceptButton = go;

        HandleCreated += delegate
        {
            int one = 1;
            try { DwmSetWindowAttribute(Handle, 20, ref one, 4); } catch { }
        };
        if (quiet) Shown += delegate { Start(); };
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);

    void Start()
    {
        go.Enabled = false;
        status.Text = un ? "Удаляю..." : "Устанавливаю...";
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                if (un) Setup.Uninstall();
                else Setup.Install();
                BeginInvoke((Action)delegate
                {
                    if (un) { Close(); return; }
                    status.Text = "Готово. Ярлыки на рабочем столе и в меню «Пуск».";
                    Setup.Launch();
                    Close();
                });
            }
            catch (Exception e)
            {
                BeginInvoke((Action)delegate
                {
                    status.Text = "Не вышло: " + (e.InnerException ?? e).Message;
                    go.Enabled = true;
                });
            }
        });
    }
}
