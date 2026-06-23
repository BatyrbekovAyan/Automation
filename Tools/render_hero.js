// Renders Tools/hero_robot.svg -> Assets/Images/Chat/bot_hero.png
// (transparent, 1080xH, faithful to the SVG source).
//
// This is the add-bot (Screen_New) hero: a sleek white+cyan robot "call assistant"
// wearing a headset. Edit hero_robot.svg, then re-run this to re-export the PNG.
//
// Setup + run:
//   cd Tools && npm install @resvg/resvg-js && node render_hero.js
//
// Why resvg-js: no system SVG renderer is installed (cairosvg/rsvg/inkscape absent),
// and macOS `qlmanage` bakes an opaque white background. resvg-js is self-contained
// (prebuilt native binary, no system cairo) and preserves transparency.

const { Resvg } = require('@resvg/resvg-js');
const fs = require('fs');
const path = require('path');

const svg = fs.readFileSync(path.join(__dirname, 'hero_robot.svg'), 'utf8');
const r = new Resvg(svg, {
  background: 'rgba(0,0,0,0)',
  fitTo: { mode: 'width', value: 1080 },
  shapeRendering: 2,
  textRendering: 2,
});
const out = r.render();
const dest = path.join(__dirname, '..', 'Assets', 'Images', 'Chat', 'bot_hero.png');
fs.writeFileSync(dest, out.asPng());
console.log('wrote', dest, out.width + 'x' + out.height);
