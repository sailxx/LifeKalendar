#!/usr/bin/env python3
# Календарь-обои для macOS и Linux: год точками на рабочем столе под разрешение главного монитора.
# Установка: kalendar.py --install [--theme 0..3] [--size 0..2] [--place 0..2] [--lang 0..1]
# Удаление: kalendar.py --uninstall. Картинка без смены обоев: kalendar.py --out file.png [--width 1920 --height 1080]
# Расчёт совпадает с kalendar.ps1 (Windows) и приложением для Android.
import argparse
import calendar
import datetime
import json
import os
import platform
import re
import shutil
import subprocess
import sys

MAC = platform.system() == "Darwin"
HOME = os.path.expanduser("~")
DIR = os.path.join(HOME, "Library", "Application Support", "LifeKalendar") if MAC \
    else os.path.join(os.environ.get("XDG_DATA_HOME", os.path.join(HOME, ".local", "share")), "lifekalendar")
CONF = os.path.join(DIR, "settings.json")
SELF = os.path.join(DIR, "kalendar.py")
APPLIED = os.path.join(DIR, "applied")
PLIST = os.path.join(HOME, "Library", "LaunchAgents", "com.sailxx.lifekalendar.plist")
UNIT_DIR = os.path.join(os.environ.get("XDG_CONFIG_HOME", os.path.join(HOME, ".config")), "systemd", "user")
CRON_TAG = "# lifekalendar"

# Фон, прошедшие, сегодня, будущие (с прозрачностью), подписи (с прозрачностью).
PAL = [
    ((0x1C, 0x1C, 0x1E), (0xF2, 0xF2, 0xF2), (0xFF, 0x5A, 0x1F), (255, 255, 255, 0x38), (255, 255, 255, 0x99)),
    ((0xF2, 0x38, 0x0F), (0xFF, 0xD3, 0x6B), (255, 255, 255), (0, 0, 0, 0x40), (0xFF, 0xE7, 0xB0, 0xB3)),
    ((0xE6, 0xE6, 0xE6), (0x26, 0x26, 0x26), (0xFF, 0x5A, 0x1F), (0, 0, 0, 0x2E), (0x26, 0x26, 0x26, 0x99)),
]
MONTHS = [
    ["Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"],
    ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
]


def run(*cmd, **kw):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=30, **kw)
        return r.returncode, r.stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return 1, ""


# ---------- Экран, тема, шрифт ----------

def screen_size():
    if MAC:
        code, out = run("system_profiler", "SPDisplaysDataType", "-json")
        try:
            best = None
            for gpu in json.loads(out)["SPDisplaysDataType"]:
                for d in gpu.get("spdisplays_ndrvs", []):
                    m = re.search(r"(\d+)\s*x\s*(\d+)", d.get("_spdisplays_pixels", ""))
                    if m and (best is None or d.get("spdisplays_main") == "spdisplays_yes"):
                        best = (int(m.group(1)), int(m.group(2)))
            if best:
                return best
        except (ValueError, KeyError, TypeError):
            pass
    else:
        code, out = run("xrandr", "--current")
        lines = [l for l in out.splitlines() if " connected" in l]
        lines.sort(key=lambda l: " primary " not in l)
        for l in lines:
            m = re.search(r"(\d+)x(\d+)\+\d+\+\d+", l)
            if m:
                return int(m.group(1)), int(m.group(2))
        code, out = run("wlr-randr")
        m = re.search(r"(\d+)x(\d+) px.*current", out)
        if m:
            return int(m.group(1)), int(m.group(2))
    return 1920, 1080


def system_is_light():
    if MAC:
        return run("defaults", "read", "-g", "AppleInterfaceStyle")[1] != "Dark"
    code, out = run("gsettings", "get", "org.gnome.desktop.interface", "color-scheme")
    if code == 0 and out:
        return "dark" not in out
    code, out = run("gsettings", "get", "org.gnome.desktop.interface", "gtk-theme")
    if code == 0 and out:
        return "dark" not in out.lower()
    return False


def find_font():
    if MAC:
        names = ["/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/Library/Fonts/Arial Bold.ttf",
                 "/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"]
    else:
        code, out = run("fc-match", "-f", "%{file}", "sans-serif:weight=600:lang=ru")
        names = [out] if out else []
        for d in ("/usr/share/fonts/truetype", "/usr/share/fonts/TTF", "/usr/share/fonts/opentype"):
            for sub in ("noto/NotoSans-SemiBold.ttf", "dejavu/DejaVuSans-Bold.ttf", "DejaVuSans-Bold.ttf",
                        "ubuntu/Ubuntu-M.ttf", "liberation/LiberationSans-Bold.ttf"):
                names.append(os.path.join(d, sub))
    for n in names:
        if n and os.path.exists(n):
            return n
    return None


# ---------- Рисование ----------

def blend(c, bg):
    if len(c) == 3:
        return c
    a = c[3] / 255
    return tuple(round(c[i] * a + bg[i] * (1 - a)) for i in range(3))


def render(w, h, theme, size, place, lang, date=None):
    from PIL import Image, ImageDraw, ImageFont
    date = date or datetime.date.today()
    pal = PAL[max(0, min(2, theme))]
    bg = pal[0]
    past, today_c = pal[1], pal[2]
    future, label = blend(pal[3], bg), blend(pal[4], bg)

    # Широкий монитор: 4×3 месяца, ультраширокий: 6×2, вертикальный: 3×4. Размеры в шагах точки p.
    portrait = h > w
    cols = 3 if portrait else (6 if w / h >= 2.1 else 4)
    rows = 12 // cols
    uw = cols * 7 + (cols - 1) * 1.7
    uh = rows * 6.5 + (rows - 1) * 1.1 + 2.6
    if portrait:
        p = w * (0.58, 0.68, 0.80)[size] / uw
    else:
        p = min(w * 0.9 / uw, h * (0.42, 0.55, 0.70)[size] / uh)
    gw, gh = uw * p, uh * p
    left = (w - gw - w * 0.07, w * 0.07, (w - gw) / 2)[{1: 0, 2: 1}.get(place, 2)]
    top = (h - gh) / 2 - h * 0.02
    r = p * 0.30

    # Рисуем только область сетки, в большом масштабе, и сжимаем: край точек получается чётким.
    mp = w * h / 1e6
    ss = 4 if mp <= 4.5 else 3 if mp <= 10 else 2
    pad = int(p * 1.5)
    bx, by = max(0, int(left) - pad), max(0, int(top) - pad)
    bw = min(w, int(left + gw) + pad) - bx
    bh = min(h, int(top + gh) + pad) - by
    layer = Image.new("RGB", (bw * ss, bh * ss), bg)
    d = ImageDraw.Draw(layer)
    ox, oy = left - bx, top - by

    path = find_font()
    def font(px):
        if path:
            return ImageFont.truetype(path, max(1, round(px * ss)))
        return ImageFont.load_default()
    f_month, f_foot = font(p * 0.78), font(p * 0.85)

    names = MONTHS[1 if lang == 1 else 0]
    year = date.year
    for m in range(12):
        x0 = ox + (m % cols) * 8.7 * p
        y0 = oy + (m // cols) * 7.6 * p
        d.text(((x0 + p / 2 - r) * ss, (y0 + p * 0.78) * ss), names[m], font=f_month,
               fill=past if m + 1 == date.month else label, anchor="ls")
        for day in range(1, calendar.monthrange(year, m + 1)[1] + 1):
            dt = datetime.date(year, m + 1, day)
            col = today_c if dt == date else past if dt < date else future
            i = day - 1
            cx = x0 + (i % 7) * p + p / 2
            cy = y0 + 1.5 * p + (i // 7) * p + p / 2
            d.ellipse(((cx - r) * ss, (cy - r) * ss, (cx + r) * ss, (cy + r) * ss), fill=col)

    length = 366 if calendar.isleap(year) else 365
    doy = date.timetuple().tm_yday
    a = f"{length - doy}d left" if lang == 1 else f"осталось {length - doy} дн."
    b = f"  ·  {doy * 100 // length}%"
    wa, wb = f_foot.getlength(a) / ss, f_foot.getlength(b) / ss
    ty = oy + gh - p * 0.6
    tx = ox + (gw - wa - wb) / 2
    d.text((tx * ss, ty * ss), a, font=f_foot, fill=today_c, anchor="ls")
    d.text(((tx + wa) * ss, ty * ss), b, font=f_foot, fill=label, anchor="ls")

    img = Image.new("RGB", (w, h), bg)
    img.paste(layer.reduce(ss), (bx, by))
    return img


# ---------- Установка обоев ----------

def set_wallpaper(path):
    if MAC:
        script = 'tell application "System Events" to tell every desktop to set picture to POSIX file "%s"' % path
        if run("osascript", "-e", script)[0] != 0:
            print("Не удалось сменить обои: разреши доступ к System Events в «Системные настройки → Конфиденциальность → Автоматизация».",
                  file=sys.stderr)
        return
    uri = "file://" + path
    # Из cron адрес сессионной шины не задан, без него gsettings молчит.
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", "unix:path=/run/user/%d/bus" % os.getuid())
    desk = (os.environ.get("XDG_CURRENT_DESKTOP", "") + os.environ.get("DESKTOP_SESSION", "")).lower()
    if "kde" in desk or "plasma" in desk:
        run("plasma-apply-wallpaperimage", path)
    elif "xfce" in desk:
        code, out = run("xfconf-query", "-c", "xfce4-desktop", "-l")
        for prop in [l for l in out.splitlines() if l.endswith("/last-image")]:
            run("xfconf-query", "-c", "xfce4-desktop", "-p", prop, "-s", path)
            run("xfconf-query", "-c", "xfce4-desktop", "-p", prop.replace("last-image", "image-style"), "-s", "5")
    elif "mate" in desk:
        run("gsettings", "set", "org.mate.background", "picture-filename", path)
    elif "cinnamon" in desk:
        run("gsettings", "set", "org.cinnamon.desktop.background", "picture-uri", uri)
        run("gsettings", "set", "org.cinnamon.desktop.background", "picture-options", "zoom")
    elif "sway" in desk or "wlroots" in desk:
        run("swaymsg", "output", "*", "bg", path, "fill")
    elif shutil.which("gsettings") and ("gnome" in desk or "unity" in desk or "budgie" in desk or "pop" in desk or "ubuntu" in desk):
        for key in ("picture-uri", "picture-uri-dark"):
            run("gsettings", "set", "org.gnome.desktop.background", key, uri)
        run("gsettings", "set", "org.gnome.desktop.background", "picture-options", "zoom")
    elif shutil.which("feh"):
        run("feh", "--bg-fill", path)
    else:
        run("gsettings", "set", "org.gnome.desktop.background", "picture-uri", uri)


def load_conf():
    try:
        with open(CONF, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def apply(args):
    conf = {} if args.out else load_conf()
    theme = conf.get("theme", args.theme)
    size, place, lang = conf.get("size", args.size), conf.get("place", args.place), conf.get("lang", args.lang)
    w, h = (args.width, args.height) if args.width > 0 and args.height > 0 else screen_size()
    if theme == 3:
        theme = 2 if system_is_light() else 0
    date = datetime.date.today()
    key = f"{date}|{theme}|{w}x{h}|{size}{place}{lang}"
    if not args.out:
        try:
            if open(APPLIED).read().strip() == key:
                return
        except OSError:
            pass
    img = render(w, h, theme, size, place, lang, date)
    if args.out:
        img.save(args.out)
        return
    os.makedirs(DIR, exist_ok=True)
    for f in os.listdir(DIR):
        if f.startswith("wallpaper-") and f.endswith(".png"):
            os.remove(os.path.join(DIR, f))
    # Новое имя файла каждый раз: рабочие столы кэшируют обои по пути.
    path = os.path.join(DIR, "wallpaper-%s.png" % datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
    img.save(path)
    set_wallpaper(path)
    with open(APPLIED, "w") as f:
        f.write(key)


# ---------- Автообновление ----------

def have_pillow():
    try:
        import PIL  # noqa: F401
        return True
    except ImportError:
        return False


def ensure_pillow(argv):
    """Pillow нет — ставим в свою папку (venv), чтобы не трогать системный Python, и перезапускаемся из неё."""
    if have_pillow():
        return
    venv = os.path.join(DIR, "venv")
    os.makedirs(DIR, exist_ok=True)
    print("Ставлю Pillow (рисование картинки)...")
    if subprocess.call([sys.executable, "-m", "venv", venv]) == 0:
        py = os.path.join(venv, "bin", "python")
        if subprocess.call([py, "-m", "pip", "install", "-q", "pillow"]) == 0:
            os.execv(py, [py] + argv)
    for extra in ([], ["--break-system-packages"]):
        if subprocess.call([sys.executable, "-m", "pip", "install", "-q", "--user", "pillow"] + extra) == 0:
            os.execv(sys.executable, [sys.executable] + argv)
    sys.exit("Не получилось поставить Pillow. Поставь вручную: python3 -m pip install --user pillow")


def install(args, argv):
    ensure_pillow(argv)
    os.makedirs(DIR, exist_ok=True)
    with open(CONF, "w", encoding="utf-8") as f:
        json.dump({"theme": args.theme, "size": args.size, "place": args.place, "lang": args.lang}, f)
    if os.path.abspath(__file__) != SELF:
        shutil.copyfile(os.path.abspath(__file__), SELF)
    if os.path.exists(APPLIED):
        os.remove(APPLIED)
    cmd = [sys.executable, SELF]
    if MAC:
        os.makedirs(os.path.dirname(PLIST), exist_ok=True)
        items = "".join("<string>%s</string>" % c for c in cmd)
        # При входе и каждые 10 минут: обои меняются, только если сменилась дата, тема или разрешение.
        with open(PLIST, "w") as f:
            f.write('<?xml version="1.0" encoding="UTF-8"?>\n<plist version="1.0"><dict>'
                    '<key>Label</key><string>com.sailxx.lifekalendar</string>'
                    '<key>ProgramArguments</key><array>%s</array>'
                    '<key>RunAtLoad</key><true/><key>StartInterval</key><integer>600</integer>'
                    '</dict></plist>\n' % items)
        run("launchctl", "unload", PLIST)
        run("launchctl", "load", PLIST)
    elif shutil.which("systemctl") and run("systemctl", "--user", "show-environment")[0] == 0:
        os.makedirs(UNIT_DIR, exist_ok=True)
        with open(os.path.join(UNIT_DIR, "lifekalendar.service"), "w") as f:
            f.write("[Unit]\nDescription=LifeKalendar wallpaper\n\n[Service]\nType=oneshot\n"
                    "ExecStart=%s\n" % " ".join('"%s"' % c for c in cmd))
        with open(os.path.join(UNIT_DIR, "lifekalendar.timer"), "w") as f:
            f.write("[Unit]\nDescription=LifeKalendar wallpaper\n\n[Timer]\nOnStartupSec=30s\n"
                    "OnUnitActiveSec=10min\n\n[Install]\nWantedBy=timers.target\n")
        run("systemctl", "--user", "daemon-reload")
        run("systemctl", "--user", "enable", "--now", "lifekalendar.timer")
    else:
        code, cur = run("crontab", "-l")
        keep = [l for l in cur.splitlines() if CRON_TAG not in l] if code == 0 else []
        keep.append("*/10 * * * * %s %s" % (" ".join(cmd), CRON_TAG))
        subprocess.run(["crontab", "-"], input="\n".join(keep) + "\n", text=True)
    apply(args)
    print("Готово: обои стоят и будут обновляться каждый день.")


def uninstall():
    if MAC:
        run("launchctl", "unload", PLIST)
        if os.path.exists(PLIST):
            os.remove(PLIST)
    else:
        run("systemctl", "--user", "disable", "--now", "lifekalendar.timer")
        for n in ("lifekalendar.service", "lifekalendar.timer"):
            p = os.path.join(UNIT_DIR, n)
            if os.path.exists(p):
                os.remove(p)
        code, cur = run("crontab", "-l")
        if code == 0 and CRON_TAG in cur:
            keep = [l for l in cur.splitlines() if CRON_TAG not in l]
            subprocess.run(["crontab", "-"], input="\n".join(keep) + "\n", text=True)
    if os.path.exists(APPLIED):
        os.remove(APPLIED)
    print("Автообновление выключено. Обои останутся, пока не сменишь их.")


def main():
    ap = argparse.ArgumentParser(description="LifeKalendar: год точками на рабочем столе")
    ap.add_argument("--install", action="store_true")
    ap.add_argument("--uninstall", action="store_true")
    ap.add_argument("--theme", type=int, default=0, help="0 чёрная, 1 красная, 2 светлая, 3 как в системе")
    ap.add_argument("--size", type=int, default=1, help="0 S, 1 M, 2 L")
    ap.add_argument("--place", type=int, default=0, help="0 по центру, 1 справа, 2 слева")
    ap.add_argument("--lang", type=int, default=0, help="0 рус, 1 eng")
    ap.add_argument("--out", help="сохранить PNG, не меняя обои")
    ap.add_argument("--width", type=int, default=0)
    ap.add_argument("--height", type=int, default=0)
    args = ap.parse_args()
    args.size = max(0, min(2, args.size))
    if args.uninstall:
        return uninstall()
    if args.install:
        return install(args, [os.path.abspath(__file__)] + sys.argv[1:])
    if not have_pillow():
        ensure_pillow([os.path.abspath(__file__)] + sys.argv[1:])
    apply(args)


if __name__ == "__main__":
    main()
