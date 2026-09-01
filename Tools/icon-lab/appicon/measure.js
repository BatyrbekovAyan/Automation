// Measures the real ink width of each message at font-size 100, so the plates
// and rings around the text are sized from a measurement rather than a guess.
// Writes textmetrics.json; re-run it if a message or the face ever changes.
const fs = require('fs');
const { Resvg } = require('@resvg/resvg-js');
const FACE = 'Helvetica, Arial, sans-serif';
const STRINGS = ['Привет!', 'Как дела?', 'Здравствуйте!', 'Привет, как дела?', 'Чем помочь?', 'Добрый день'];
const S = 100, PAD = 40, W = 2400, H = 220;
const out = {};
for (const str of STRINGS) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
    <rect width="${W}" height="${H}" fill="#000"/>
    <text x="${PAD}" y="150" font-family="${FACE}" font-size="${S}" font-weight="700" fill="#fff">${str}</text></svg>`;
  const png = new Resvg(svg, { fitTo: { mode: 'width', value: W } }).render();
  fs.writeFileSync(`out/_m.png`, png.asPng());
  const { execSync } = require('child_process');
  const w = execSync(`python3 -c "
from PIL import Image
im=Image.open('out/_m.png').convert('L')
bb=im.getbbox() if im.getbbox() else (0,0,0,0)
import numpy as np
a=np.array(im)
cols=np.where(a.max(axis=0)>20)[0]
print(int(cols.max()-cols.min()+1) if len(cols) else 0)
"`).toString().trim();
  out[str] = +(parseInt(w, 10) / S).toFixed(4);
}
fs.writeFileSync('textmetrics.json', JSON.stringify(out, null, 2));
fs.unlinkSync('out/_m.png');
console.log(out);
