// Round 9 renders: the finalists of «как живот Репли» (concepts_round9.js) as exact vectors,
// on U9's ink palette.
//
//   node repli.js   -> out/repli/<id>.svg + <id>_1024.png (full-bleed masters) for every concept
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');
const { iconSvg, P } = require('./build');
const { concepts } = require('./concepts_round9');

const OUT = path.join(__dirname, 'out', 'repli');
fs.mkdirSync(OUT, { recursive: true });

for (const c of concepts) {
  const svg = iconSvg(c, P.ink);
  fs.writeFileSync(path.join(OUT, `${c.id}.svg`), svg);
  fs.writeFileSync(path.join(OUT, `${c.id}_1024.png`),
    new Resvg(svg, { fitTo: { mode: 'width', value: 1024 } }).render().asPng());
  console.log('wrote', path.relative(process.cwd(), path.join(OUT, `${c.id}_1024.png`)));
}
