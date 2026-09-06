// Renders every app-icon concept x colourway to SVG + PNG, and composes the
// review sheets. Judge on the sheets, at the small size — an icon that only
// works at 1024 is not finished (../STYLE.md).
//
//   node build.js              -> everything
//   node build.js sheet        -> sheets only (fast re-render after a tweak)
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');
const { concepts } = require(process.env.SET === 'v1' ? './concepts' : process.env.SET === 'v2' ? './concepts_choice' : process.env.SET === 'v3' ? './concepts_round3' : process.env.SET === 'v4' ? './concepts_round4' : process.env.SET === 'v5' ? './concepts_round5' : process.env.SET === 'v6' ? './concepts_round6' : process.env.SET === 'v7' ? './concepts_round7' : './concepts_round8');

const OUT = path.join(__dirname, 'out');
const dirs = ['svg', 'png'].map((d) => path.join(OUT, d));
dirs.forEach((d) => fs.mkdirSync(d, { recursive: true }));

// mark      = the main shape
// accent    = the single accent element
// onAccent  = ink that sits on top of accent
const palettes = [
  { id: 'night',    ru: 'Ночной',        bg: ['#1E2947', '#0A0E18'], mark: '#FFFFFF', accent: '#22D3EE', accentHi: '#67E8F9', onAccent: '#062B33', deep: '#0D2638' },
  { id: 'ink',      ru: 'Чернильный',    bg: ['#182036', '#0A0D16'], mark: '#FFFFFF', accent: '#5981D6', accentHi: '#8FAEEF', onAccent: '#0B1B3A', deep: '#13214A' },
  { id: 'indigo',   ru: 'Индиго',        bg: ['#5271E4', '#22357A'], mark: '#FFFFFF', accent: '#8FE9F9', accentHi: '#C4F4FD', onAccent: '#0B2D57', deep: '#1A2B66' },
  { id: 'cyan',     ru: 'Циан',          bg: ['#34DDF2', '#0B93B8'], mark: '#FFFFFF', accent: '#06333D', accentHi: '#0B4D5C', onAccent: '#EAFCFF', deep: '#083744' },
  { id: 'light',    ru: 'Светлый',       bg: ['#FFFFFF', '#E4EAF4'], mark: '#16233E', accent: '#2C6BF0', accentHi: '#5B93FF', onAccent: '#FFFFFF', deep: '#1B2A4D' },
  { id: 'graphite', ru: 'Графит',        bg: ['#252C39', '#10141B'], mark: '#FFFFFF', accent: '#22D3EE', accentHi: '#67E8F9', onAccent: '#062B33', deep: '#1D2532' },
  { id: 'green',    ru: 'Мессенджерский',bg: ['#123A24', '#06140D'], mark: '#FFFFFF', accent: '#25D366', accentHi: '#6EE7A0', onAccent: '#05301A', deep: '#0B2A1A' },
];

const P = Object.fromEntries(palettes.map((p) => [p.id, p]));

function iconSvg(concept, pal, { size = 1024, bare = false } = {}) {
  const gid = `bg_${concept.id}_${pal.id}`;
  const body = `
  <defs>
    <linearGradient id="${gid}" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="${pal.bg[0]}"/><stop offset="1" stop-color="${pal.bg[1]}"/>
    </linearGradient>
  </defs>
  <rect width="1024" height="1024" fill="url(#${gid})"/>
  ${concept.draw(pal)}`;
  if (bare) return body;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="${size}" height="${size}">${body}\n</svg>`;
}

function render(svg, px) {
  return new Resvg(svg, { fitTo: { mode: 'width', value: px }, background: 'rgba(0,0,0,0)' }).render().asPng();
}

// ---------- per-icon files ----------
if (require.main === module && process.argv[2] !== 'sheet') {
  for (const c of concepts) {
    for (const pal of palettes) {
      const svg = iconSvg(c, pal);
      fs.writeFileSync(path.join(OUT, 'svg', `${c.id}_${pal.id}.svg`), svg);
      if (pal.id === 'night' || pal.id === 'indigo') {
        fs.writeFileSync(path.join(OUT, 'png', `${c.id}_${pal.id}.png`), render(svg, 1024));
      }
    }
  }
  console.log(`wrote ${concepts.length * palettes.length} svg`);
}

// ---------- review sheets ----------
// iOS squircle, superellipse-ish, expressed on a 0..1 box then scaled.
function squircle(x, y, s) {
  const k = s * 0.2225; // Apple's continuous-corner radius is ~22.25% of the side
  const c = k * 0.55;
  return `M ${x + k} ${y}
    L ${x + s - k} ${y} C ${x + s - c} ${y} ${x + s} ${y + c} ${x + s} ${y + k}
    L ${x + s} ${y + s - k} C ${x + s} ${y + s - c} ${x + s - c} ${y + s} ${x + s - k} ${y + s}
    L ${x + k} ${y + s} C ${x + c} ${y + s} ${x} ${y + s - c} ${x} ${y + s - k}
    L ${x} ${y + k} C ${x} ${y + c} ${x + c} ${y} ${x + k} ${y} Z`;
}

function tile(concept, pal, x, y, s, idSuffix) {
  const cid = `clip_${concept.id}_${pal.id}_${idSuffix}`;
  const inner = iconSvg(concept, pal, { bare: true });
  return `
  <clipPath id="${cid}"><path d="${squircle(x, y, s)}"/></clipPath>
  <g clip-path="url(#${cid})"><g transform="translate(${x},${y}) scale(${s / 1024})">${inner}</g></g>`;
}

const FONT = 'Helvetica, Arial, sans-serif';

function conceptSheet(palId) {
  const pal = P[palId];
  const cols = 4, big = 300, gapX = 62, gapY = 214, padX = 70, padY = 118;
  const rows = Math.ceil(concepts.length / cols);
  const W = padX * 2 + cols * big + (cols - 1) * gapX;
  const H = padY + rows * (big + gapY) + 190;
  let s = '';
  concepts.forEach((c, i) => {
    const col = i % cols, row = Math.floor(i / cols);
    const x = padX + col * (big + gapX);
    const y = padY + row * (big + gapY);
    s += tile(c, pal, x, y, big, 'b');
    // true-device row: 180 (iPad), 120 (iPhone @3x home), 60 (Spotlight-ish)
    let sx = x;
    [120, 76, 54].forEach((sz) => {
      s += tile(c, pal, sx, y + big + 30, sz, `s${sz}`);
      sx += sz + 18;
    });
    s += `<text x="${x + 4}" y="${y - 26}" fill="#E8EAF0" font-family="${FONT}" font-size="34" font-weight="700">${c.id}</text>`;
    s += `<text x="${x + 80}" y="${y - 26}" fill="#8A94A6" font-family="${FONT}" font-size="26">dir ${c.dir}</text>`;
  });
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="#0A0D12"/>
  <text x="${padX}" y="64" fill="#E8EAF0" font-family="${FONT}" font-size="40" font-weight="700">Choose Reply — app icon concepts (${pal.id})</text>
  ${s}
</svg>`;
}

function colorwaySheet(conceptId) {
  const c = concepts.find((k) => k.id === conceptId);
  const big = 300, gapX = 56, padX = 70, padY = 118;
  const W = padX * 2 + palettes.length * big + (palettes.length - 1) * gapX;
  const H = padY + big + 250;
  let s = '';
  palettes.forEach((pal, i) => {
    const x = padX + i * (big + gapX);
    s += tile(c, pal, x, padY, big, 'c');
    let sx = x;
    [120, 76, 54].forEach((sz) => { s += tile(c, pal, sx, padY + big + 30, sz, `cs${sz}`); sx += sz + 18; });
    s += `<text x="${x + 4}" y="${padY - 26}" fill="#E8EAF0" font-family="${FONT}" font-size="30" font-weight="700">${pal.id}</text>`;
  });
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="#0A0D12"/>
  <text x="${padX}" y="64" fill="#E8EAF0" font-family="${FONT}" font-size="40" font-weight="700">Colourways — ${c.id}</text>
  ${s}
</svg>`;
}

// Sheets are a CLI concern — page.js requires this module for iconSvg/palettes
// only, and re-running the sheet loop there crashed on a stale default CW.
if (require.main === module) {
  const sheets = { concepts_night: conceptSheet('night'), concepts_ink: conceptSheet('ink') };
  const want = (process.env.CW || concepts.slice(0, 3).map((c) => c.id).join(','))
    .split(',').filter((id) => concepts.some((c) => c.id === id));
  for (const id of want) sheets[`colorways_${id}`] = colorwaySheet(id);
  for (const [name, svg] of Object.entries(sheets)) {
    fs.writeFileSync(path.join(OUT, `sheet_${name}.svg`), svg);
    const w = parseInt(svg.match(/width="(\d+)"/)[1], 10);
    fs.writeFileSync(path.join(OUT, `sheet_${name}.png`), render(svg, w));
    console.log('sheet', name, w);
  }
}
module.exports = { iconSvg, palettes, P };
