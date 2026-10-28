#!/usr/bin/env python3
# Календарь-обои для macOS и Linux: год точками на рабочем столе под разрешение главного монитора.
# Установка: kalendar.py --install [--theme 0..3] [--size 0..2] [--place 0..2] [--lang 0..1]
# Удаление: kalendar.py --uninstall. Картинка без смены обоев: kalendar.py --out file.png [--width 1920 --height 1080]
import argparse
import calendar
import datetime
import json
import os
import plistlib
import re
import shutil
import subprocess
import sys
from pathlib import Path

MAC = sys.platform == 'darwin'
HOME = Path.home()
DIR = (HOME / 'Library/Application Support/LifeKalendar') if MAC else \
    (Path(os.environ.get('XDG_DATA_HOME') or HOME / '.local/share') / 'LifeKalendar')
CONF = DIR / 'settings.json'
SELF = DIR / 'kalendar.py'
APPLIED = DIR / 'applied'
VENV_PY = DIR / 'venv/bin/python'
LABEL = 'com.sailxx.lifekalendar'
PLIST = HOME / 'Library/LaunchAgents' / (LABEL + '.plist')
UNIT_DIR = Path(os.environ.get('XDG_CONFIG_HOME') or HOME / '.config') / 'systemd/user'
CRON_TAG = '# LifeKalendar'

# Те же цвета, что в приложении для Android: фон, прошедшие, сегодня, будущие, подписи (ARGB).
PAL = [
    ('FF1C1C1E', 'FFF2F2F2', 'FFFF5A1F', '38FFFFFF', '99FFFFFF'),
    ('FFF2380F', 'FFFFD36B', 'FFFFFFFF', '40000000', 'B3FFE7B0'),
    ('FFE6E6E6', 'FF262626', 'FFFF5A1F', '2E000000', '99262626'),
]
MONTHS = (
    ('Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн', 'Июл', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек'),
    ('Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'),
)


def run(*cmd, **kw):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, **kw)
        return r.stdout.strip() if r.returncode == 0 else None
    except (OSError, subprocess.SubprocessError):
        return None


def have(name):
    return shutil.which(name) is not None


def screen_size():
    """Настоящие пиксели главного монитора, без масштабирования системы."""
    if MAC:
        js = ('ObjC.import("AppKit");const s=$.NSScreen.screens.objectAtIndex(0);'
              'Math.round(s.frame.size.width*s.backingScaleFactor)+"x"+Math.round(s.frame.size.height*s.backingScaleFactor)')
        m = re.match(r'(\d+)x(\d+)', run('osascript', '-l', 'JavaScript', '-e', js) or '')
        if m:
            return int(m[1]), int(m[2])
    else:
        out = run('xrandr', '--current') or ''
        lines = [l for l in out.splitlines() if ' connected' in l]
        lines.sort(key=lambda l: ' primary ' not in l)
        for l in lines:
            m = re.search(r'(\d+)x(\d+)\+\d+\+\d+', l)
            if m:
                return int(m[1]), int(m[2])
    return 1920, 1080


def system_is_dark():
    if MAC:
        return (run('defaults', 'read', '-g', 'AppleInterfaceStyle') or '').lower() == 'dark'
    if have('gsettings'):
        scheme = run('gsettings', 'get', 'org.gnome.desktop.interface', 'color-scheme') or ''
        if 'dark' in scheme:
            return True
        if 'default' in scheme or 'light' in scheme:
            return False
        if 'dark' in (run('gsettings', 'get', 'org.gnome.desktop.interface', 'gtk-theme') or '').lower():
            return True
    for tool in ('kreadconfig6', 'kreadconfig5'):
        if have(tool):
            return 'dark' in (run(tool, '--group', 'General', '--key', 'ColorScheme') or '').lower()
    return False


def find_font():
    if MAC:
        paths = ['/System/Library/Fonts/Supplemental/Arial Bold.ttf', '/System/Library/Fonts/Helvetica.ttc']
    else:
        paths = []
        if have('fc-match'):
            paths.append(run('fc-match', '-f', '%{file}', 'sans-serif:weight=600:lang=ru'))
        paths += [
            '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
            '/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf',
            '/usr/share/fonts/TTF/DejaVuSans-Bold.ttf',
            '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf',
            '/usr/share/fonts/liberation/LiberationSans-Bold.ttf',
            '/usr/share/fonts/noto/NotoSans-Bold.ttf',
            'C:/Windows/Fonts/segoeuib.ttf',
        ]
    for p in paths:
        if p and os.path.exists(p):
            return p
    return None


def color(h, bg=None):
    v = int(h, 16)
    a, r, g, b = (v >> 24) & 255, (v >> 16) & 255, (v >> 8) & 255, v & 255
    if bg is None or a == 255:
        return (r, g, b)
    k = a / 255
    return tuple(round(c * k + o * (1 - k)) for c, o in zip((r, g, b), bg))


def render(theme, size, place, lang, w, h, today):
    from PIL import Image, ImageDraw, ImageFont

    # Рисуем в 2x и сжимаем: сглаженные края точек и текста. На очень больших экранах хватает 1x.
    ss = 2 if w * h <= 8_300_000 else 1
    W, H = w * ss, h * ss
    bg = color(PAL[theme][0])
    pal = [bg] + [color(c, bg) for c in PAL[theme][1:]]
    img = Image.new('RGB', (W, H), bg)
    dr = ImageDraw.Draw(img)

    # Сетка: широкий монитор 4x3 месяца, ультраширокий 6x2, вертикальный 3x4. Размеры в шагах точки.
    cols = 3 if H > W else 6 if W / H >= 2.1 else 4
    rows = 12 // cols
    uw = cols * 7 + (cols - 1) * 1.7
    uh = rows * 6.5 + (rows - 1) * 1.1 + 2.6
    if H > W:
        p = W * (0.58, 0.68, 0.80)[size] / uw
    else:
        p = min(W * 0.9 / uw, H * (0.42, 0.55, 0.70)[size] / uh)
    gw, gh = uw * p, uh * p
    left = (W - gw) / 2 if place == 0 else W - gw - W * 0.07 if place == 1 else W * 0.07
    top = (H - gh) / 2 - H * 0.02
    r = p * 0.30

    path = find_font()

    def font(px):
        px = max(1, round(px))
        return ImageFont.truetype(path, px) if path else ImageFont.load_default(px)

    def text(s, px, col, x, baseline):
        dr.text((x, baseline), s, font=font(px), fill=col, anchor='ls')

    for m in range(12):
        x0 = left + (m % cols) * (7 * p + 1.7 * p)
        y0 = top + (m // cols) * (6.5 * p + 1.1 * p)
        lc = pal[1] if m + 1 == today.month else pal[4]
        text(MONTHS[lang][m], p * 0.78, lc, x0 + p / 2 - r, y0 + p * 0.78)
        for d in range(1, calendar.monthrange(today.year, m + 1)[1] + 1):
            day = datetime.date(today.year, m + 1, d)
            col = pal[2] if day == today else pal[1] if day < today else pal[3]
            i = d - 1
            cx = x0 + (i % 7) * p + p / 2
            cy = y0 + 1.5 * p + (i // 7) * p + p / 2
            dr.ellipse((cx - r, cy - r, cx + r, cy + r), fill=col)

    # Подпись под сеткой: сколько дней осталось и процент года.
    n = 366 if calendar.isleap(today.year) else 365
    doy = today.timetuple().tm_yday
    a = f'{n - doy}d left' if lang else f'осталось {n - doy} дн.'
    b = f'  ·  {doy * 100 // n}%'
    f = font(p * 0.85)
    wa, wb = f.getlength(a), f.getlength(b)
    ty = top + gh - p * 0.6
    tx = left + (gw - wa - wb) / 2
    dr.text((tx, ty), a, font=f, fill=pal[2], anchor='ls')
    dr.text((tx + wa, ty), b, font=f, fill=pal[4], anchor='ls')

    if ss > 1:
        img = img.resize((w, h), Image.LANCZOS)
    return img


def set_wallpaper(file):
    uri = Path(file).as_uri()
    if MAC:
        script = f'tell application "System Events" to tell every desktop to set picture to POSIX file "{file}"'
        return run('osascript', '-e', script) is not None
    if not os.environ.get('DBUS_SESSION_BUS_ADDRESS'):
        os.environ['DBUS_SESSION_BUS_ADDRESS'] = f'unix:path=/run/user/{os.getuid()}/bus'
    de = os.environ.get('XDG_CURRENT_DESKTOP', '').lower()
    ok = False
    if 'kde' in de and have('plasma-apply-wallpaperimage'):
        ok = run('plasma-apply-wallpaperimage', str(file)) is not None
    elif 'xfce' in de and have('xfconf-query'):
        for prop in (run('xfconf-query', '-c', 'xfce4-desktop', '-l') or '').splitlines():
            if prop.endswith('/last-image'):
                run('xfconf-query', '-c', 'xfce4-desktop', '-p', prop, '-s', str(file))
                run('xfconf-query', '-c', 'xfce4-desktop', '-p', prop.replace('last-image', 'image-style'), '-s', '5')
                ok = True
    elif 'mate' in de and have('gsettings'):
        run('gsettings', 'set', 'org.mate.background', 'picture-options', 'zoom')
        ok = run('gsettings', 'set', 'org.mate.background', 'picture-filename', str(file)) is not None
    elif have('gsettings'):
        schema = 'org.cinnamon.desktop.background' if 'cinnamon' in de else 'org.gnome.desktop.background'
        run('gsettings', 'set', schema, 'picture-options', 'zoom')
        run('gsettings', 'set', schema, 'picture-uri-dark', uri)
        ok = run('gsettings', 'set', schema, 'picture-uri', uri) is not None
    if not ok and have('feh'):
        ok = run('feh', '--bg-fill', str(file)) is not None
    return ok


def apply(args):
    if not args.out and CONF.exists():
        c = json.loads(CONF.read_text(encoding='utf-8'))
        args.theme, args.size, args.place, args.lang = c['theme'], c['size'], c['place'], c['lang']
    w, h = args.width, args.height
    if w <= 0 or h <= 0:
        w, h = screen_size()
    theme = args.theme
    if theme == 3:
        theme = 0 if system_is_dark() else 2
    theme = max(0, min(2, theme))
    size = max(0, min(2, args.size))
    place = max(0, min(2, args.place))
    lang = max(0, min(1, args.lang))
    today = datetime.date.today()
    key = f'{today}|{theme}|{w}x{h}|{size}{place}{lang}'
    if not args.out and APPLIED.exists() and APPLIED.read_text().strip() == key:
        return
    img = render(theme, size, place, lang, w, h, today)
    if args.out:
        img.save(args.out)
        return
    # Новое имя файла каждый день: система кэширует обои по пути.
    DIR.mkdir(parents=True, exist_ok=True)
    for old in DIR.glob('wallpaper-*.png'):
        old.unlink(missing_ok=True)
    file = DIR / f'wallpaper-{datetime.datetime.now():%Y%m%d-%H%M%S}.png'
    img.save(file)
    if not set_wallpaper(file):
        print(f'Не удалось поставить обои, картинка лежит здесь: {file}', file=sys.stderr)
        return
    APPLIED.write_text(key)


def schedule():
    """Раз в 10 минут: скрипт сам проверит дату, тему системы и разрешение и меняет обои, только если что-то изменилось."""
    cmd = [str(VENV_PY), str(SELF)]
    if MAC:
        PLIST.parent.mkdir(parents=True, exist_ok=True)
        PLIST.write_bytes(plistlib.dumps({
            'Label': LABEL, 'ProgramArguments': cmd, 'RunAtLoad': True, 'StartInterval': 600,
            'StandardErrorPath': str(DIR / 'error.log'),
        }))
        target = f'gui/{os.getuid()}'
        run('launchctl', 'bootout', f'{target}/{LABEL}')
        if run('launchctl', 'bootstrap', target, str(PLIST)) is None:
            print('launchctl не принял задачу, автообновление не включено.', file=sys.stderr)
        return
    q = ' '.join(f'"{c}"' for c in cmd)
    if have('systemctl') and run('systemctl', '--user', 'show-environment') is not None:
        UNIT_DIR.mkdir(parents=True, exist_ok=True)
        (UNIT_DIR / 'lifekalendar.service').write_text(
            f'[Unit]\nDescription=LifeKalendar wallpaper\n\n[Service]\nType=oneshot\nExecStart={q}\n')
        (UNIT_DIR / 'lifekalendar.timer').write_text(
            '[Unit]\nDescription=LifeKalendar wallpaper every 10 minutes\n\n'
            '[Timer]\nOnStartupSec=30\nOnUnitActiveSec=10min\n\n[Install]\nWantedBy=timers.target\n')
        run('systemctl', '--user', 'daemon-reload')
        run('systemctl', '--user', 'enable', '--now', 'lifekalendar.timer')
        return
    if have('crontab'):
        old = run('crontab', '-l') or ''
        keep = [l for l in old.splitlines() if CRON_TAG not in l]
        keep.append(f'*/10 * * * * {q} {CRON_TAG}')
        subprocess.run(['crontab', '-'], input='\n'.join(keep) + '\n', text=True)
        return
    print('Нет ни systemd, ни cron: обои поставятся один раз.', file=sys.stderr)


def install(args):
    DIR.mkdir(parents=True, exist_ok=True)
    CONF.write_text(json.dumps({'theme': args.theme, 'size': args.size, 'place': args.place, 'lang': args.lang}),
                    encoding='utf-8')
    if Path(__file__).resolve() != SELF.resolve():
        shutil.copyfile(__file__, SELF)
    if not VENV_PY.exists():
        print('Ставлю Pillow в отдельное окружение...')
        subprocess.run([sys.executable, '-m', 'venv', str(DIR / 'venv')], check=True)
        subprocess.run([str(VENV_PY), '-m', 'pip', 'install', '--quiet', 'pillow'], check=True)
    APPLIED.unlink(missing_ok=True)
    schedule()
    subprocess.run([str(VENV_PY), str(SELF)], check=True)
    print('Готово: обои стоят и будут обновляться каждый день.')
    if MAC:
        print('Если macOS спросит доступ к System Events, разреши: иначе обои не сменятся.')


def uninstall():
    if MAC:
        run('launchctl', 'bootout', f'gui/{os.getuid()}/{LABEL}')
        PLIST.unlink(missing_ok=True)
    else:
        if have('systemctl'):
            run('systemctl', '--user', 'disable', '--now', 'lifekalendar.timer')
        for n in ('lifekalendar.service', 'lifekalendar.timer'):
            (UNIT_DIR / n).unlink(missing_ok=True)
        if have('crontab'):
            old = run('crontab', '-l') or ''
            if CRON_TAG in old:
                keep = [l for l in old.splitlines() if CRON_TAG not in l]
                subprocess.run(['crontab', '-'], input='\n'.join(keep) + '\n', text=True)
    APPLIED.unlink(missing_ok=True)
    print('Автообновление выключено. Обои останутся, пока не сменишь их.')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--install', action='store_true')
    ap.add_argument('--uninstall', action='store_true')
    ap.add_argument('--theme', type=int, default=0, help='0 чёрная, 1 красная, 2 светлая, 3 как в системе')
    ap.add_argument('--size', type=int, default=1, help='0 S, 1 M, 2 L')
    ap.add_argument('--place', type=int, default=0, help='0 по центру, 1 справа, 2 слева')
    ap.add_argument('--lang', type=int, default=0, help='0 рус, 1 eng')
    ap.add_argument('--out')
    ap.add_argument('--width', type=int, default=0)
    ap.add_argument('--height', type=int, default=0)
    args = ap.parse_args()
    if args.uninstall:
        uninstall()
    elif args.install:
        install(args)
    else:
        apply(args)


if __name__ == '__main__':
    main()
