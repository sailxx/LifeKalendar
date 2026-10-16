// Генерирует плашки README в assets/: node build.mjs (шрифты golos.ttf и jbmono.ttf в .font/).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createKit, P, THEMES } from './kit.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const S = JSON.parse(fs.readFileSync(path.join(here, 'strings.json'), 'utf8'));
const k = createKit({ golos: path.join(here, '.font/golos.ttf'), mono: path.join(here, '.font/jbmono.ttf') });
const W = 880;

function hero() {
  const h = 380;
  const body = () => {
    const lead = k.para(48, 252, S.lead, 440, { size: 17, fill: P.muted, lh: 27 });
    return `${k.card(0, 0, W, h, 28)}
${k.dots(48, 58, [1, 1, 1, 1, 2, 0, 0, 0, 0], 8, 2.4)}
${k.text(82, 66, S.label, { size: 12, weight: 600, m: true, ls: 2.4, fill: P.muted })}
${k.text(46, 140, S.title[0], { size: 52, weight: 700, ls: -1.6 })}
${k.text(46, 194, S.title[1], { size: 52, weight: 700, ls: -1.6, fill: '#B5B5B5' })}
${lead.s}
${k.phone(640, 34, 150, THEMES.light)}`;
  };
  return k.svg(W, h, S.title.join(' '), body());
}

function features() {
  const gap = 16, cw = (W - gap) / 2, ch = 168;
  const body = () => S.features.map((f, i) => {
    const x = (i % 2) * (cw + gap), y = Math.floor(i / 2) * (ch + gap);
    const d = k.para(x + 32, y + 120, f.d, cw - 64, { size: 15, fill: P.muted, lh: 23 });
    return `${k.card(x, y, cw, ch)}
${k.dots(x + 36, y + 38, f.dots)}
${k.text(x + 32, y + 98, f.t, { size: 21, weight: 650, ls: -0.4 })}
${d.s}`;
  }).join('\n');
  return k.svg(W, 2 * ch + gap, 'Возможности', body());
}

function themes() {
  const h = 430, pw = 160;
  const body = () => {
    const xs = [W / 2 - pw * 1.5 - 56, W / 2 - pw / 2, W / 2 + pw / 2 + 56];
    const list = [THEMES.black, THEMES.red, THEMES.light];
    return `${k.card(0, 0, W, h, 28)}
${list.map((t, i) => `${k.phone(xs[i], 36, pw, t)}
${k.text(xs[i] + pw / 2, 36 + pw * 2.08 + 38, S.themes[i], { size: 11, weight: 600, m: true, ls: 2.2, fill: P.muted, anchor: 'middle' })}`).join('\n')}`;
  };
  return k.svg(W, h, 'Темы', body());
}

function steps() {
  const gap = 16, cw = (W - 2 * gap) / 3, ch = 168;
  const body = () => S.steps.map((st, i) => {
    const x = i * (cw + gap);
    const d = k.para(x + 28, y0 + 112, st.d, cw - 56, { size: 15, fill: P.muted, lh: 23 });
    // шаг отмечен точками: одна, две, три
    const marks = [0, 1, 2].map((j) => `<circle cx="${x + 34 + j * 12}" cy="${y0 + 40}" r="3.4" fill="${j < i ? P.ink : j === i ? P.accent : P.dot}"/>`).join('');
    return `${k.card(x, y0, cw, ch)}
${marks}
${k.text(x + 28, y0 + 90, st.t, { size: 21, weight: 650, ls: -0.4 })}
${d.s}`;
  }).join('\n');
  const y0 = 0;
  return k.svg(W, ch, 'Как поставить', body());
}

function button() {
  const body = () => {
    const w = Math.ceil(62 + k.width(S.download, 16, 600) * 1.08 + 48);
    // Оранжевая: выделяется и на светлой, и на тёмной теме GitHub.
    return { w, s: `<rect x=".5" y=".5" width="${w - 1}" height="55" rx="28" fill="${P.accent}"/>
<g transform="translate(26 16)" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4v11m-5-5 5 5 5-5M5 20h14"/></g>
<circle cx="${w - 28}" cy="28" r="3.4" fill="#fff"/>
${k.text(62, 34, S.download, { size: 16, weight: 600, fill: '#fff' })}` };
  };
  const b = body();
  return k.svg(Math.ceil(b.w), 56, S.download, b.s);
}

await k.build(path.join(here, '../../assets'), () => ({
  'hero.svg': hero(),
  'features.svg': features(),
  'themes.svg': themes(),
  'steps.svg': steps(),
  'download.svg': button(),
}));
