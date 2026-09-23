// Render an SVG to a square PNG of the given size through headless Chrome.
import { launchChrome } from './chrome.mjs';
import fs from 'node:fs';
const [svgPath, outPath, sizeArg] = process.argv.slice(2);
const size = Number(sizeArg);
const svg = fs.readFileSync(svgPath, 'utf8');
const browser = await launchChrome();
try {   // rule 0: the browser is closed even when a step throws (финальное ревью, M-5)
  const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg width="${size}" height="${size}" `)}</body></html>`);
  await page.screenshot({ path: outPath, omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
} finally {
  await browser.close();
}
