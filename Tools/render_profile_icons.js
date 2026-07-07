// Renders the Profile sub-pages icon set (white glyphs, transparent bg) to
// Assets/Images/ProfileSubPages/PS_*.png at 256px. Same pipeline as
// render_hero.js (resvg-js: no system SVG renderer on this machine preserves
// transparency). Import settings are stamped by ProfileSubPagesBuilder's
// EnsureIconImportSettings — no hand-written .meta files.
//
// Usage: cd Tools && npm install @resvg/resvg-js && node render_profile_icons.js
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');

const W = 24; // viewBox
const wrap = (inner) =>
  `<svg viewBox="0 0 ${W} ${W}" xmlns="http://www.w3.org/2000/svg">${inner}</svg>`;

const S = 'stroke="#FFFFFF" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"';

const ICONS = {
  // Notifications
  PS_Speaker: `<path d="M11 5 6.5 9H3v6h3.5L11 19V5Z" fill="#FFFFFF"/><path d="M15 9a4.2 4.2 0 0 1 0 6M18 6.5a8 8 0 0 1 0 11" ${S}/>`,
  PS_Vibrate: `<rect x="8" y="3" width="8" height="18" rx="2" ${S}/><path d="M4 9v6M20 9v6M1.5 10.5v3M22.5 10.5v3" ${S}/>`,
  PS_Unread: `<path d="M4 5.5A2.5 2.5 0 0 1 6.5 3h11A2.5 2.5 0 0 1 20 5.5v8a2.5 2.5 0 0 1-2.5 2.5H9l-5 4V5.5Z" ${S}/><circle cx="17.5" cy="5.5" r="3.4" fill="#FFFFFF" stroke="none"/>`,
  // Privacy / Account
  PS_Smartphone: `<rect x="6" y="2.5" width="12" height="19" rx="2.5" ${S}/><circle cx="12" cy="18" r="1.2" fill="#FFFFFF" stroke="none"/>`,
  PS_Cloud: `<path d="M7 18a4.5 4.5 0 0 1-.4-9A6 6 0 0 1 18.2 10 4 4 0 0 1 17.5 18H7Z" ${S}/>`,
  PS_Media: `<rect x="3" y="4" width="18" height="16" rx="2.5" ${S}/><circle cx="9" cy="10" r="1.6" fill="#FFFFFF" stroke="none"/><path d="m5 19 5-5 3 3 4-4 3 3" ${S}/>`,
  PS_Bubble: `<path d="M4 5.5A2.5 2.5 0 0 1 6.5 3h11A2.5 2.5 0 0 1 20 5.5v8a2.5 2.5 0 0 1-2.5 2.5H9l-5 4V5.5Z" ${S}/><path d="M8.5 8.5h7M8.5 12h4.5" ${S}/>`,
  PS_Trash: `<path d="M4 6h16M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2m3 0-1 14a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2L6 6" ${S}/>`,
  // About / Support
  PS_Doc: `<path d="M6 2.5h8L20 8.5V21a1.5 1.5 0 0 1-1.5 1.5h-12A1.5 1.5 0 0 1 5 21V4A1.5 1.5 0 0 1 6.5 2.5Z" ${S}/><path d="M14 2.5v6h6" ${S}/>`,
  PS_Send: `<path d="M22 2 11 13M22 2 15 22l-4-9-9-4L22 2Z" ${S}/>`,
  PS_Robot: `<rect x="5" y="7" width="14" height="11" rx="3.5" stroke="#FFFFFF" stroke-width="2" fill="none"/><circle cx="9.75" cy="12.5" r="1.3" fill="#FFFFFF"/><circle cx="14.25" cy="12.5" r="1.3" fill="#FFFFFF"/><path d="M12 7V4" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round"/><circle cx="12" cy="3.2" r="1.2" fill="#FFFFFF"/><path d="M3 11v4M21 11v4" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round"/>`,
};

const outDir = path.join(__dirname, '..', 'Assets', 'Images', 'ProfileSubPages');
fs.mkdirSync(outDir, { recursive: true });

for (const [name, inner] of Object.entries(ICONS)) {
  const r = new Resvg(wrap(inner), {
    background: 'rgba(0,0,0,0)',
    fitTo: { mode: 'width', value: 256 },
    shapeRendering: 2,
  });
  const file = path.join(outDir, `${name}.png`);
  fs.writeFileSync(file, r.render().asPng());
  console.log('wrote', file);
}
