// The ONE way to start headless Chrome for this project's checks.
//
// The hardware GPU is OFF, always: --disable-gpu + --disable-gpu-compositing, WebGL through
// SwiftShader (software, on the CPU). Twice on 2026-09-21 WindowServer's watchdog reported «Display
// not ready» and the whole GUI session restarted on this Mac (MacBookPro16,2, Intel Iris) while
// headless Chrome was rendering the particle field — the second time with only software WebGL but
// GPU compositing still on, and the busiest process in the hang report was headless Chrome's GPU
// helper. So there is no hardware mode any more: FPS is measured on software rendering, paired
// with the v1 baseline in the same run (relative, not absolute).
import { chromium } from 'playwright-core';

export const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';

export const CHROME_ARGS = Object.freeze([
  '--disable-gpu',
  '--disable-gpu-compositing',
  '--use-angle=swiftshader',
  '--enable-unsafe-swiftshader',
]);

export function launchChrome(options = {}) {
  if ('gpu' in options) throw new Error('hardware GPU mode was removed (see tools/chrome.mjs header)');
  return chromium.launch({ executablePath: CHROME, headless: true, args: [...CHROME_ARGS], ...options });
}
