// Android launcher assets for the SHIPPED icon concept (U9 «Тонированная
// инверсия», ink palette — the exact master in Assets/Images/Icon.png, verified
// pixel-identical against the round-7 render on 2026-09-05).
//
//   node android.js            -> Assets/Images/Icon_android_{bg,fg}.png (432, Unity's
//                                 xxxhdpi adaptive slots) + out/android/* (1024 masters,
//                                 Play listing icon 512, mask previews)
//
// Why the layers are DERIVED from the vector and not cropped out of the PNG:
// an adaptive icon is 108dp of which only the central 72dp are guaranteed
// visible, and launchers animate the two layers against each other. So the
// foreground carries the mark at ×0.63 around the centre (0.65 already grazes
// the cut-outs against the mask edge — measured in the round-8 review) and the
// background carries the master's diagonal gradient STRETCHED to the mask's
// visible window (170.67..853.33 in 1024 space), so a circle/squircle/rounded
// mask shows the same colour sweep the App Store icon does. The cut-throughs
// (top/bottom stripes, the window around the chosen one) are drawn in the
// foreground with the SAME userSpaceOnUse gradient, so they read as holes in
// the tinted ground exactly like the master, whatever the launcher parallax.
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');
const { P, iconSvg } = require('./build');
const { concepts: round7 } = require('./concepts_round7');
const { pill } = require('./concepts_choice');
const { rrect } = require('./concepts');

const CONCEPT = 'U9';
const PAL = P.ink;
const SCALE = 0.63;                    // mark size inside the 108dp canvas
const VIS = [1024 / 3, 1024 * 2 / 3];  // the 72dp window: 341.33..682.67 is the SAFE zone; the
                                       // VISIBLE window is 66.67 % → 170.67..853.33
const WINDOW = [1024 * (1 - 72 / 108) / 2, 1024 - 1024 * (1 - 72 / 108) / 2];

const OUT = path.join(__dirname, 'out', 'android');
const ASSETS = path.resolve(__dirname, '../../../Assets/Images');
fs.mkdirSync(OUT, { recursive: true });

// Locked chassis from concepts_round7 (B): x 110, width 804; thin 142 @ 208/674, thick 184 @ 420.
const B = { x: 110, w: 804, top: [208, 142], mid: [420, 184], bot: [674, 142] };
const s = (v) => 512 + (v - 512) * SCALE;   // scale a coordinate about the centre
const d = (v) => v * SCALE;                 // scale a length

// U9's geometry, scaled: the two cut-through stripes (grown ±14 like the master), the
// dark window the chosen stripe sits in (rrect 76,396 872×232 r116), the chosen stripe.
const cutTop = pill(s(B.x - 14), s(B.top[0]), d(B.w + 28), d(B.top[1]));
const cutBot = pill(s(B.x - 14), s(B.bot[0]), d(B.w + 28), d(B.bot[1]));
const cutWin = rrect(s(76), s(396), d(872), d(232), d(116));
const chosen = pill(s(B.x), s(B.mid[0]), d(B.w), d(B.mid[1]));

const gradient = (id) => `
  <linearGradient id="${id}" gradientUnits="userSpaceOnUse"
      x1="${WINDOW[0]}" y1="${WINDOW[0]}" x2="${WINDOW[1]}" y2="${WINDOW[1]}">
    <stop offset="0" stop-color="${PAL.bg[0]}"/><stop offset="1" stop-color="${PAL.bg[1]}"/>
  </linearGradient>`;

const svg = (body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">${body}\n</svg>`;

// Background layer: the master's ground — gradient (stretched to the visible window) under
// the 20 % accent tint. No cuts here; they live in the foreground so they move with the mark.
const bgSvg = svg(`
  <defs>${gradient('g')}</defs>
  <rect width="1024" height="1024" fill="url(#g)"/>
  <rect width="1024" height="1024" fill="${PAL.accent}" opacity="0.20"/>`);

// Foreground layer: transparent, the three stripes + their window. Cuts are filled with the
// SAME gradient as the background, so they read as holes in the tint.
const fgSvg = svg(`
  <defs>${gradient('g')}</defs>
  <path d="${cutTop}" fill="url(#g)"/>
  <path d="${cutWin}" fill="url(#g)"/>
  <path d="${cutBot}" fill="url(#g)"/>
  <path d="${chosen}" fill="${PAL.accent}"/>`);

function render(svgText, px) {
  return new Resvg(svgText, { fitTo: { mode: 'width', value: px }, background: 'rgba(0,0,0,0)' }).render().asPng();
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
    <g clip-path="url(#${id})">${bgBody.replace(/id="g"/g, `id="g_${name}_bg"`).replace(/url\(#g\)/g, `url(#g_${name}_bg)`)}${fgBody.replace(/id="g"/g, `id="g_${name}_fg"`).replace(/url\(#g\)/g, `url(#g_${name}_fg)`)}</g>
  </g>
  <text x="${x + 96}" y="262" fill="#E8EAF0" font-family="Helvetica, Arial, sans-serif" font-size="18" text-anchor="middle">${name}</text>`;
    x += 232;
  }
  // full bleed, both layers, 512 — the ring outside the visible window is the parallax reserve
  tiles += `
  <g transform="translate(40,300) scale(${512 / 1024})">${bgBody.replace(/id="g"/g, 'id="g_full_bg"').replace(/url\(#g\)/g, 'url(#g_full_bg)')}${fgBody.replace(/id="g"/g, 'id="g_full_fg"').replace(/url\(#g\)/g, 'url(#g_full_fg)')}
    <circle cx="512" cy="512" r="${r}" fill="none" stroke="#FFFFFF" stroke-opacity="0.35" stroke-width="4" stroke-dasharray="16 12"/>
  </g>
  <text x="296" y="840" fill="#8A94A6" font-family="Helvetica, Arial, sans-serif" font-size="18" text-anchor="middle">full 108dp canvas · dashed = 72dp visible window</text>`;
  const W = 40 + 232 * 3, H = 870;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}"><rect width="${W}" height="${H}" fill="#0A0D12"/>${tiles}\n</svg>`;
}

if (require.main === module) {
  fs.writeFileSync(path.join(OUT, 'adaptive_bg_1024.svg'), bgSvg);
  fs.writeFileSync(path.join(OUT, 'adaptive_fg_1024.svg'), fgSvg);
  fs.writeFileSync(path.join(OUT, 'adaptive_bg_1024.png'), render(bgSvg, 1024));
  fs.writeFileSync(path.join(OUT, 'adaptive_fg_1024.png'), render(fgSvg, 1024));
  fs.writeFileSync(path.join(ASSETS, 'Icon_android_bg.png'), render(bgSvg, 432));
  fs.writeFileSync(path.join(ASSETS, 'Icon_android_fg.png'), render(fgSvg, 432));

  // Play listing icon: the master concept, full-bleed square (Play applies its own mask).
  const master = round7.find((c) => c.id === CONCEPT);
  fs.writeFileSync(path.join(OUT, 'play_icon_512.png'), render(iconSvg(master, PAL), 512));
  fs.writeFileSync(path.join(OUT, 'master_1024.png'), render(iconSvg(master, PAL), 1024));

  const pv = preview();
  fs.writeFileSync(path.join(OUT, 'adaptive_preview.svg'), pv);
  fs.writeFileSync(path.join(OUT, 'adaptive_preview.png'), render(pv, parseInt(pv.match(/width="(\d+)"/)[1], 10)));
  console.log(`wrote ${path.relative(process.cwd(), ASSETS)}/Icon_android_{bg,fg}.png (432) and ${path.relative(process.cwd(), OUT)}/*`);
}

module.exports = { bgSvg, fgSvg };
