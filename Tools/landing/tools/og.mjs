// Render tools/og.html → src/static/og.png (1200×630) through headless Chrome.
import { launchChrome } from './chrome.mjs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const browser = await launchChrome();
try {   // rule 0: the browser is closed even when a step throws (финальное ревью, M-5)
  const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
  await page.goto(pathToFileURL(path.join(HERE, 'og.html')).href, { waitUntil: 'load' });
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: path.join(HERE, '../src/static/og.png') });
} finally {
  await browser.close();
}
console.log('og.png written');
