// Publishes the icon-lab glyphs into the Unity project.
//
// Business glyphs are written OVER the existing BT_*.png filenames on purpose:
// the .meta guid beside each one is what BusinessTypes.asset references, so
// overwriting keeps every reference intact, while a renamed file would silently
// unwire all six business types.
//
// Nav glyphs are new files; Unity mints their guids on import, and
// Tools/Nav Icons/Apply Icon Set wires them into BottomTabManager.
//
// Usage: cd Tools/icon-lab && node publish.js [--check]
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');

const HERE = __dirname;
const GLYPHS = path.join(HERE, 'glyphs');
const ROOT = path.resolve(HERE, '..', '..');
const SIZE = 256;

// glyph name -> published path. Business filenames are load-bearing (see above).
const BUSINESS = {
  bt_auto_parts: 'Assets/Images/BusinessIcons/BT_AutoParts.png',
  bt_wholesale: 'Assets/Images/BusinessIcons/BT_Wholesale.png',
  bt_flowers: 'Assets/Images/BusinessIcons/BT_Flowers.png',
  bt_kaspi_seller: 'Assets/Images/BusinessIcons/BT_KaspiSeller.png',
  bt_education: 'Assets/Images/BusinessIcons/BT_Education.png',
  bt_phone_repair: 'Assets/Images/BusinessIcons/BT_PhoneRepair.png',
};

const NAV = {};
for (const tab of ['chats', 'dashboard', 'bots', 'profile']) {
  for (const state of ['outline', 'filled']) {
    NAV[`nav_${tab}_${state}`] = `Assets/Images/Nav/nav_${tab}_${state}.png`;
  }
}

const TARGETS = { ...BUSINESS, ...NAV };
const check = process.argv.includes('--check');

const missing = Object.keys(TARGETS).filter(
  (n) => !fs.existsSync(path.join(GLYPHS, `${n}.svg`)),
);
if (missing.length) {
  console.error(`Missing glyph(s): ${missing.join(', ')}`);
  process.exit(1);
}
if (check) {
  console.log(`All ${Object.keys(TARGETS).length} glyphs present.`);
  process.exit(0);
}

for (const [name, rel] of Object.entries(TARGETS)) {
  const svg = fs.readFileSync(path.join(GLYPHS, `${name}.svg`), 'utf8');
  const png = new Resvg(svg, {
    background: 'rgba(0,0,0,0)',
    fitTo: { mode: 'width', value: SIZE },
    shapeRendering: 2,
  }).render().asPng();

  const dest = path.join(ROOT, rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  const existed = fs.existsSync(dest);
  fs.writeFileSync(dest, png);
  console.log(`${existed ? 'overwrote' : 'created  '} ${rel}`);
}
console.log(`\nDone. New files need an Assets/Refresh in Unity, then run` +
            ` Tools/Nav Icons/Apply Icon Set to wire the nav bar.`);
