// Every asset of the SHIPPED app icon, from its vector: G2 «Ролик · тёмный» (concepts_round9.js),
// ink palette — chosen by the owner 2026-10-23, it replaced U9 «Тонированная инверсия» (round 7).
//
//   node android.js            -> Assets/Images/Icon.png (1024, RGB — Unity's Default Icon: every iOS
//                                 size and the App Store 1024 are generated from it, and the App Store
//                                 refuses an icon with an alpha channel even when it is all opaque)
//                                 Assets/Images/Icon_android_{bg,fg}.png (432, Unity's xxxhdpi adaptive
//                                 slots) + out/android/* (1024 masters, Play listing icon 512, mask
//                                 previews)
//   then python3 Tools/store/play-graphics.py — the Play icon-512 and the feature graphic.
//
// The three PNGs in Assets/Images are overwritten IN PLACE: ProjectSettings references them by the
// guid in their .meta, so new pixels under the same files are the whole publish.
//
// Why the layers are DERIVED from the vector and not cropped out of the PNG: an adaptive icon is
// 108dp of which only the central 72dp are guaranteed visible, and launchers animate the two layers
// against each other. So the background carries the master's ground — the ink gradient STRETCHED to
// the mask's visible window (170.67..853.33 in 1024 space) under the 20 % accent tint — so a circle/
// squircle/rounded mask shows the same colour sweep the App Store icon does; and the foreground
// carries the mark alone, glow included, at ×2/3 around the centre. The visible window IS 2/3 of the
// canvas, so it shows the master's composition at the master's own proportions (the chosen bar at
// 78 % of the width, as on iOS). The mark's farthest point — the chosen bar's round end, 267 from the
// centre at that scale — stays inside the 66dp safe circle (313), which no launcher mask may bite.
// (U9 used ×0.63: its dark window was wider, and 0.65 already grazed the mask edge.)
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { Resvg } = require('@resvg/resvg-js');
const { P, iconSvg } = require('./build');
const { concepts: round9 } = require('./concepts_round9');
const { rrect } = require('./concepts');

const CONCEPT = 'G2';
const PAL = P.ink;
const SCALE = 2 / 3;                   // mark size inside the 108dp canvas = the visible window's share
const WINDOW = [1024 * (1 - 72 / 108) / 2, 1024 - 1024 * (1 - 72 / 108) / 2];

const OUT = path.join(__dirname, 'out', 'android');
const ASSETS = path.resolve(__dirname, '../../../Assets/Images');
fs.mkdirSync(OUT, { recursive: true });

const master = round9.find((c) => c.id === CONCEPT);

const gradient = (id) => `
  <linearGradient id="${id}" gradientUnits="userSpaceOnUse"
      x1="${WINDOW[0]}" y1="${WINDOW[0]}" x2="${WINDOW[1]}" y2="${WINDOW[1]}">
    <stop offset="0" stop-color="${PAL.bg[0]}"/><stop offset="1" stop-color="${PAL.bg[1]}"/>
  </linearGradient>`;

const svg = (body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">${body}\n</svg>`;

// Background layer: the master's ground — gradient (stretched to the visible window) under the 20 %
// accent tint. Nothing of the mark here; it lives in the foreground so it moves with the parallax.
const bgSvg = svg(`
  <defs>${gradient('g')}</defs>
  <rect width="1024" height="1024" fill="url(#g)"/>
  <rect width="1024" height="1024" fill="${PAL.accent}" opacity="0.20"/>`);

// Foreground layer: transparent, the three bars with their glow, scaled about the centre.
const fgSvg = svg(`
  <g transform="translate(512 512) scale(${SCALE}) translate(-512 -512)">
  ${master.mark(PAL)}
  </g>`);

function render(svgText, px) {
  return new Resvg(svgText, { fitTo: { mode: 'width', value: px }, background: 'rgba(0,0,0,0)' }).render().asPng();
}

// The master as a PNG WITHOUT an alpha channel. resvg always hands out RGBA, so the opaque render is
// re-encoded as 8-bit truecolour: one filter-0 row per line, one zlib stream, a CRC per chunk.
function rgbPng(svgText, px) {
  const img = new Resvg(svgText, { fitTo: { mode: 'width', value: px }, background: '#000000' }).render();
  const { width: w, height: h, pixels } = img;
  const row = w * 3 + 1;
  const raw = Buffer.alloc(row * h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const s = (y * w + x) * 4;
      if (pixels[s + 3] !== 255) throw new Error(`master is not opaque at ${x},${y}`);
      pixels.copy(raw, y * row + 1 + x * 3, s, s + 3);
    }
  }
  const chunk = (type, data) => {
    const len = Buffer.alloc(4);
    len.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(zlib.crc32(body));
    return Buffer.concat([len, body, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8;   // bit depth
  ihdr[9] = 2;   // colour type 2 = RGB, no alpha
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

// Preview: the composite under the three launcher masks, at true 48dp×4 = 192 px, plus a
// full-bleed 512 so the parallax margin is visible.
function preview() {
  const bgBody = bgSvg.replace(/^<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');
  const fgBody = fgSvg.replace(/^<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');
  const r = (WINDOW[1] - WINDOW[0]) / 2;
  const masks = {
    circle: `<circle cx="512" cy="512" r="${r}"/>`,
    squircle: `<path d="${rrect(WINDOW[0], WINDOW[0], 2 * r, 2 * r, r * 0.42)}"/>`,
    rounded: `<path d="${rrect(WINDOW[0], WINDOW[0], 2 * r, 2 * r, r * 0.22)}"/>`,
  };
  let tiles = '';
  let x = 40;
  for (const [name, shape] of Object.entries(masks)) {
    const id = `clip_${name}`;
    tiles += `
  <clipPath id="${id}">${shape}</clipPath>
  <g transform="translate(${x},40) scale(${192 / 1024})">
    <g clip-path="url(#${id})">${bgBody.replace(/id="g"/g, `id="g_${name}_bg"`).replace(/url\(#g\)/g, `url(#g_${name}_bg)`)}${fgBody}</g>
  </g>
  <text x="${x + 96}" y="262" fill="#E8EAF0" font-family="Helvetica, Arial, sans-serif" font-size="18" text-anchor="middle">${name}</text>`;
    x += 232;
  }
  // full bleed, both layers, 512 — the ring outside the visible window is the parallax reserve
  tiles += `
  <g transform="translate(40,300) scale(${512 / 1024})">${bgBody.replace(/id="g"/g, 'id="g_full_bg"').replace(/url\(#g\)/g, 'url(#g_full_bg)')}${fgBody}
    <circle cx="512" cy="512" r="${r}" fill="none" stroke="#FFFFFF" stroke-opacity="0.35" stroke-width="4" stroke-dasharray="16 12"/>
  </g>
  <text x="296" y="840" fill="#8A94A6" font-family="Helvetica, Arial, sans-serif" font-size="18" text-anchor="middle">full 108dp canvas · dashed = 72dp visible window</text>`;
  const W = 40 + 232 * 3, H = 870;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}"><rect width="${W}" height="${H}" fill="#0A0D12"/>${tiles}\n</svg>`;
}

if (require.main === module) {
  const masterSvg = iconSvg(master, PAL);
  fs.writeFileSync(path.join(ASSETS, 'Icon.png'), rgbPng(masterSvg, 1024));

  fs.writeFileSync(path.join(OUT, 'adaptive_bg_1024.svg'), bgSvg);
  fs.writeFileSync(path.join(OUT, 'adaptive_fg_1024.svg'), fgSvg);
  fs.writeFileSync(path.join(OUT, 'adaptive_bg_1024.png'), render(bgSvg, 1024));
  fs.writeFileSync(path.join(OUT, 'adaptive_fg_1024.png'), render(fgSvg, 1024));
  fs.writeFileSync(path.join(ASSETS, 'Icon_android_bg.png'), render(bgSvg, 432));
  fs.writeFileSync(path.join(ASSETS, 'Icon_android_fg.png'), render(fgSvg, 432));

  // Play listing icon: the master, full-bleed square (Play applies its own mask).
  fs.writeFileSync(path.join(OUT, 'master_1024.svg'), masterSvg);
  fs.writeFileSync(path.join(OUT, 'play_icon_512.png'), render(masterSvg, 512));
  fs.writeFileSync(path.join(OUT, 'master_1024.png'), render(masterSvg, 1024));

  const pv = preview();
  fs.writeFileSync(path.join(OUT, 'adaptive_preview.svg'), pv);
  fs.writeFileSync(path.join(OUT, 'adaptive_preview.png'), render(pv, parseInt(pv.match(/width="(\d+)"/)[1], 10)));
  console.log(`wrote ${path.relative(process.cwd(), ASSETS)}/Icon.png (1024, RGB), Icon_android_{bg,fg}.png (432) and ${path.relative(process.cwd(), OUT)}/*`);
}

module.exports = { bgSvg, fgSvg };
