// Renders icon-lab glyph SVGs to PNG via resvg-js (same engine as
// render_business_icons.js). Glyphs live in icon-lab/glyphs/<name>.svg as
// standalone 24x24-viewBox SVGs with white strokes on transparent bg.
//
// Usage:
//   node render.js                       -> renders every glyph to out/<name>.png @512
//   node render.js chats_outline         -> renders one glyph
//   node render.js chats_outline 64      -> at an explicit pixel size
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');

const GLYPH_DIR = path.join(__dirname, 'glyphs');
const OUT_DIR = path.join(__dirname, 'out');

function render(name, size) {
  const src = path.join(GLYPH_DIR, `${name}.svg`);
  const svg = fs.readFileSync(src, 'utf8');
  const r = new Resvg(svg, {
    background: 'rgba(0,0,0,0)',
    fitTo: { mode: 'width', value: size },
    shapeRendering: 2,
  });
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const out = path.join(OUT_DIR, size === 512 ? `${name}.png` : `${name}@${size}.png`);
  fs.writeFileSync(out, r.render().asPng());
  return out;
}

const [, , which, sizeArg] = process.argv;
const size = sizeArg ? parseInt(sizeArg, 10) : 512;
const names = which
  ? [which.replace(/\.svg$/, '')]
  : fs.readdirSync(GLYPH_DIR).filter((f) => f.endsWith('.svg')).map((f) => f.slice(0, -4)).sort();

for (const n of names) console.log('wrote', render(n, size));
