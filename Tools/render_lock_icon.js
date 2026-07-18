// Renders Tools/lock_icon.svg -> Assets/Images/Icons/Lock.png
// (transparent, 512x512, faithful to the SVG source).
//
// The onboarding «Это безопасно» trust blocks (OnboardingAuthBlocksBuilder) show
// this padlock as an Image+sprite tinted green on the trust card. White-on-
// transparent source so the Unity Image.color tint controls the final colour.
//
// Setup + run:
//   cd Tools && npm install @resvg/resvg-js && node render_lock_icon.js
//
// Why resvg-js: no system SVG renderer is installed (cairosvg/rsvg/inkscape
// absent), and macOS `qlmanage` bakes an opaque white background. resvg-js is
// self-contained (prebuilt native binary, no system cairo) and preserves
// transparency (same pipeline as render_hero.js).

const { Resvg } = require('@resvg/resvg-js');
const fs = require('fs');
const path = require('path');

const svg = fs.readFileSync(path.join(__dirname, 'lock_icon.svg'), 'utf8');
const r = new Resvg(svg, {
  background: 'rgba(0,0,0,0)',
  fitTo: { mode: 'width', value: 512 },
  shapeRendering: 2,
  textRendering: 2,
});
const out = r.render();
const dest = path.join(__dirname, '..', 'Assets', 'Images', 'Icons', 'Lock.png');
fs.writeFileSync(dest, out.asPng());
console.log('wrote', dest, out.width + 'x' + out.height);
