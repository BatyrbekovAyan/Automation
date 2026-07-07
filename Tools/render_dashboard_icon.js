// Renders the "Сводка" (Dashboard) bottom-tab icon pair -> Assets/Images/Nav/.
//
// Two states of a simple line-chart/pulse glyph, both on a 132x132 canvas,
// centered with comfortable padding, rounded line caps/joins:
//   dashboard_inactive.png — muted #8C8C8C outline stroke (~10px)
//   dashboard_active.png   — #1B7CEB, visually bolder (~14px stroke + filled dot accents)
//
// Setup + run:
//   cd Tools && node render_dashboard_icon.js
//
// Why resvg-js: no system SVG renderer is installed (cairosvg/rsvg/inkscape
// absent), and macOS `qlmanage` bakes an opaque white background. resvg-js is
// self-contained (prebuilt native binary, no system cairo) and preserves
// transparency. Mirrors Tools/render_hero.js.

const { Resvg } = require('@resvg/resvg-js');
const fs = require('fs');
const path = require('path');

const CANVAS = 132;

// Line-chart polyline path, drawn inside a padded inner box so the glyph
// reads clearly at small tab-bar sizes. Inner box: 26..106 (80x80), with the
// trend line rising left-to-right and a peak accent point on the last vertex.
const POLYLINE_POINTS = '28,96 50,74 66,86 86,50 104,32';
const PEAK_CX = 104;
const PEAK_CY = 32;

function inactiveSvg() {
  return `
<svg width="${CANVAS}" height="${CANVAS}" viewBox="0 0 ${CANVAS} ${CANVAS}" xmlns="http://www.w3.org/2000/svg">
  <polyline points="${POLYLINE_POINTS}"
            fill="none" stroke="#8C8C8C" stroke-width="10"
            stroke-linecap="round" stroke-linejoin="round" />
  <circle cx="${PEAK_CX}" cy="${PEAK_CY}" r="7" fill="none" stroke="#8C8C8C" stroke-width="10" />
</svg>`;
}

function activeSvg() {
  return `
<svg width="${CANVAS}" height="${CANVAS}" viewBox="0 0 ${CANVAS} ${CANVAS}" xmlns="http://www.w3.org/2000/svg">
  <polyline points="${POLYLINE_POINTS}"
            fill="none" stroke="#1B7CEB" stroke-width="14"
            stroke-linecap="round" stroke-linejoin="round" />
  <circle cx="${PEAK_CX}" cy="${PEAK_CY}" r="10" fill="#1B7CEB" />
</svg>`;
}

function render(svg, filename) {
  const r = new Resvg(svg, {
    background: 'rgba(0,0,0,0)',
    fitTo: { mode: 'width', value: CANVAS },
    shapeRendering: 2,
    textRendering: 2,
  });
  const out = r.render();
  const dest = path.join(__dirname, '..', 'Assets', 'Images', 'Nav', filename);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, out.asPng());
  console.log('wrote', dest, out.width + 'x' + out.height);
}

render(inactiveSvg(), 'dashboard_inactive.png');
render(activeSvg(), 'dashboard_active.png');
