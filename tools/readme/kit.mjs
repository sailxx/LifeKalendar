// Плашки README в духе life calendar: белый фон, тонкая рамка, точки вместо цифр, один оранжевый акцент.
// Шрифты Golos Text и JetBrains Mono урезаются под текст и встраиваются в каждый SVG.
import fs from 'node:fs';
import path from 'node:path';
import subsetFont from 'subset-font';
import * as fontkit from 'fontkit';

export const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

export const P = {
  bg: '#FFFFFF', ink: '#0A0A0A', muted: '#7A7A7A', hair: '#EBEBEB', soft: '#F6F6F6',
  dot: '#E4E4E4', accent: '#FF5A1F',
};

// Палитры обоев — как в приложении (Calendar.kt).
export const THEMES = {
  black: { bg: '#1C1C1E', past: '#F2F2F2', today: '#FF5A1F', future: 'rgba(255,255,255,.22)', stroke: '#1C1C1E' },
  red: { bg: '#F2380F', past: '#FFD36B', today: '#FFFFFF', future: 'rgba(0,0,0,.25)', stroke: '#F2380F' },
  light: { bg: '#F4F4F4', past: '#262626', today: '#FF5A1F', future: 'rgba(0,0,0,.14)', stroke: P.hair },
};

export function createKit({ golos, mono }) {
  const fontG = fontkit.openSync(golos), fontM = fontkit.openSync(mono);
  const faces = new Map();
  const face = (m, wght) => {
    const k = (m ? 'm' : 'g') + wght;
    if (!faces.has(k)) faces.set(k, (m ? fontM : fontG).getVariation({ wght }));
    return faces.get(k);
  };
  let used = '';

  const width = (s, size, weight = 400, m = false, ls = 0) => {
    const f = face(m, weight);
    return f.layout(s).advanceWidth / f.unitsPerEm * size + Math.max(0, [...s].length - 1) * ls;
  };

  const wrap = (s, maxW, size, weight = 400) => {
    const out = [];
    let cur = '';
    for (const w of s.split(' ')) {
      const next = cur ? cur + ' ' + w : w;
      if (cur && width(next, size, weight) > maxW) { out.push(cur); cur = w; } else cur = next;
    }
    if (cur) out.push(cur);
    return out;
  };

  /** Текст; mono — моноширинный капс с разрядкой. */
  const text = (x, y, s, { size = 16, weight = 400, fill = P.ink, m = false, ls = 0, anchor } = {}) => {
    used += s;
    return `<text${m ? ' class="m"' : ''} x="${x}" y="${y}" font-size="${size}" font-weight="${weight}"${ls ? ` letter-spacing="${ls}"` : ''}${anchor ? ` text-anchor="${anchor}"` : ''} fill="${fill}">${esc(s)}</text>`;
  };

  const para = (x, y, s, maxW, o = {}) => {
    const size = o.size ?? 16, lh = o.lh ?? size * 1.5;
    const ls = wrap(s, maxW, size, o.weight);
    return { s: ls.map((l, i) => text(x, y + i * lh, l, o)).join('\n'), h: ls.length * lh };
  };

  /** Белая плашка с тонкой рамкой. */
  const card = (x, y, w, h, rx = 22, fill = P.bg) =>
    `<rect x="${x + .5}" y="${y + .5}" width="${w - 1}" height="${h - 1}" rx="${rx}" fill="${fill}" stroke="${P.hair}"/>`;

  /** Год точками: 12 месяцев сеткой 3×4, по 7 точек в ряд. today — [месяц, день]. */
  function year(x, y, w, t, today = [10, 16]) {
    const colGap = w * 0.08, monthW = (w - 2 * colGap) / 3, pitch = monthW / 7, r = pitch * 0.32;
    const rowGap = pitch * 1.2, monthH = 5 * pitch;
    const len = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    let s = '';
    for (let m = 0; m < 12; m++) {
      const x0 = x + (m % 3) * (monthW + colGap), y0 = y + Math.floor(m / 3) * (monthH + rowGap);
      for (let d = 1; d <= len[m]; d++) {
        const before = m + 1 < today[0] || (m + 1 === today[0] && d < today[1]);
        const now = m + 1 === today[0] && d === today[1];
        const fill = now ? t.today : before ? t.past : t.future;
        const i = d - 1;
        s += `<circle cx="${(x0 + (i % 7) * pitch + pitch / 2).toFixed(1)}" cy="${(y0 + Math.floor(i / 7) * pitch + pitch / 2).toFixed(1)}" r="${r.toFixed(2)}" fill="${fill}"/>`;
      }
    }
    return { s, h: 4 * monthH + 3 * rowGap };
  }

  /** Телефон с обоями: без часов и цифр, только год точками. */
  function phone(x, y, w, t) {
    const h = w * 2.08, rx = w * 0.16;
    const g = year(x + w * 0.16, y + h * 0.36, w * 0.68, t);
    return `<rect x="${x + .5}" y="${y + .5}" width="${w - 1}" height="${h - 1}" rx="${rx}" fill="${t.bg}" stroke="${t.stroke}"/>
<rect x="${x + w / 2 - w * 0.13}" y="${y + w * 0.07}" width="${w * 0.26}" height="${w * 0.07}" rx="${w * 0.035}" fill="${t === THEMES.light ? '#E2E2E2' : 'rgba(0,0,0,.35)'}"/>
${g.s}`;
  }

  /** Значок из точек 3×3 — вместо цифр и пиктограмм. on — какие точки закрашены. */
  const dots = (x, y, on, step = 9, r = 2.6) => {
    let s = '';
    for (let i = 0; i < 9; i++) {
      const fill = on[i] === 2 ? P.accent : on[i] ? P.ink : P.dot;
      s += `<circle cx="${x + (i % 3) * step}" cy="${y + Math.floor(i / 3) * step}" r="${r}" fill="${fill}"/>`;
    }
    return s;
  };

  const svg = (w, h, label, body) => (fg, fm) =>
    `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img" aria-label="${esc(label)}">
<style>@font-face{font-family:"G";src:url(data:font/woff2;base64,${fg}) format("woff2");font-weight:100 900}
@font-face{font-family:"M";src:url(data:font/woff2;base64,${fm}) format("woff2");font-weight:100 900}
text{font-family:"G","Segoe UI",system-ui,sans-serif}.m{font-family:"M",ui-monospace,Consolas,monospace}</style>
${body}
</svg>
`;

  /** Сборка: files = { 'name.svg': render }. Сначала прогон вхолостую, чтобы собрать текст для шрифтов. */
  async function build(outDir, make) {
    fs.mkdirSync(outDir, { recursive: true });
    used = '';
    const files = make();
    for (const f of Object.values(files)) f('', '');
    const chars = [...new Set(used + ' ')].join('');
    const sub = async (src) => (await subsetFont(fs.readFileSync(src), chars, { targetFormat: 'woff2' })).toString('base64');
    const fg = await sub(golos), fm = await sub(mono);
    for (const [name, render] of Object.entries(files)) fs.writeFileSync(path.join(outDir, name), render(fg, fm));
    console.log(Object.keys(files).join(', '));
  }

  return { width, wrap, text, para, card, year, phone, dots, svg, build };
}
