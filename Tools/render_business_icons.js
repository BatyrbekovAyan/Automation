// Renders the business-type tile icon set (white glyphs, transparent bg) to
// Assets/Images/BusinessIcons/BT_*.png at 256px. Same pipeline as
// render_profile_icons.js (resvg-js). Import settings are stamped by
// BusinessTileIconBuilder's EnsureIconImportSettings — .meta guids are the
// source of truth for BusinessTypes.asset references.
//
// Usage: cd Tools && node render_business_icons.js [--preview <dir>]
//   --preview also writes QA composites (glyph on its tile color) to <dir>.
const fs = require('fs');
const path = require('path');
const { Resvg } = require('@resvg/resvg-js');

const W = 24; // viewBox
const S = 'stroke="#FFFFFF" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"';

// Glyphs match the 6 BusinessTypes.asset entries (ids in comments).
const ICONS = {
  // auto_parts — gear (запчасти/механика)
  BT_AutoParts: `<circle cx="12" cy="12" r="3.1" ${S}/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" ${S}/>`,
  // wholesale — stacked boxes
  BT_Wholesale: `<rect x="2.5" y="13" width="8.6" height="8.5" rx="1" ${S}/><rect x="12.9" y="13" width="8.6" height="8.5" rx="1" ${S}/><rect x="7.7" y="2.5" width="8.6" height="8.5" rx="1" ${S}/><path d="M6.8 13v3M17.2 13v3M12 2.5v3" ${S}/>`,
  // flowers — flower head + stem + leaves
  BT_Flowers: `<path d="M12 3.6c1 1.15 1 3 0 4.15-1-1.15-1-3 0-4.15Z" ${S}/><path d="M12 3.6c1 1.15 1 3 0 4.15-1-1.15-1-3 0-4.15Z" ${S} transform="rotate(60 12 9.4)"/><path d="M12 3.6c1 1.15 1 3 0 4.15-1-1.15-1-3 0-4.15Z" ${S} transform="rotate(120 12 9.4)"/><path d="M12 3.6c1 1.15 1 3 0 4.15-1-1.15-1-3 0-4.15Z" ${S} transform="rotate(180 12 9.4)"/><path d="M12 3.6c1 1.15 1 3 0 4.15-1-1.15-1-3 0-4.15Z" ${S} transform="rotate(240 12 9.4)"/><path d="M12 3.6c1 1.15 1 3 0 4.15-1-1.15-1-3 0-4.15Z" ${S} transform="rotate(300 12 9.4)"/><circle cx="12" cy="9.4" r="1.4" fill="#FFFFFF" stroke="none"/><path d="M12 15.2V21.5" ${S}/><path d="M12 19c-2.4 0-4.1-1.2-4.6-3 2.4 0 4.1 1.2 4.6 3ZM12 19c2.4 0 4.1-1.2 4.6-3-2.4 0-4.1 1.2-4.6 3Z" ${S}/>`,
  // kaspi_seller — shopping bag
  BT_KaspiSeller: `<path d="M6.2 2.5 3.5 6.1v13.4a2 2 0 0 0 2 2h13a2 2 0 0 0 2-2V6.1L17.8 2.5H6.2Z" ${S}/><path d="M3.5 6.5h17" ${S}/><path d="M15.4 9.8a3.4 3.4 0 0 1-6.8 0" ${S}/>`,
  // education — graduation cap
  BT_Education: `<path d="M1.8 9.3 12 4.4l10.2 4.9L12 14.2 1.8 9.3Z" ${S}/><path d="M6.2 11.5v4.6c0 1.5 2.6 2.8 5.8 2.8s5.8-1.3 5.8-2.8v-4.6" ${S}/><path d="M22.2 9.3v5.2" ${S}/>`,
  // phone_repair — phone + wrench
  BT_PhoneRepair: `<path d="M15.5 5V4.5a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v15a2 2 0 0 0 2 2h4" ${S}/><circle cx="9.75" cy="17.8" r="1.1" fill="#FFFFFF" stroke="none"/><g transform="translate(10.6 7.0) scale(0.58)"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" stroke="#FFFFFF" stroke-width="3.1" fill="none" stroke-linecap="round" stroke-linejoin="round"/></g>`,
};

// tileColor per entry in BusinessTypes.asset — preview-only, NOT baked into PNGs.
const PREVIEW_TILE_COLORS = {
  BT_AutoParts: '#8E8E93',
  BT_Wholesale: '#5856D6',
  BT_Flowers: '#FF2D55',
  BT_KaspiSeller: '#FF9500',
  BT_Education: '#30B0C7',
  BT_PhoneRepair: '#32ADE6',
};

const wrap = (inner) =>
  `<svg viewBox="0 0 ${W} ${W}" xmlns="http://www.w3.org/2000/svg">${inner}</svg>`;
const wrapPreview = (inner, color) =>
  `<svg viewBox="0 0 ${W} ${W}" xmlns="http://www.w3.org/2000/svg">` +
  `<rect x="0" y="0" width="${W}" height="${W}" rx="6" fill="${color}"/>` +
  `<g transform="translate(2.9 2.9) scale(0.76)">${inner}</g></svg>`;

const previewIdx = process.argv.indexOf('--preview');
const previewDir = previewIdx >= 0 ? process.argv[previewIdx + 1] : null;

const outDir = path.join(__dirname, '..', 'Assets', 'Images', 'BusinessIcons');
fs.mkdirSync(outDir, { recursive: true });
if (previewDir) fs.mkdirSync(previewDir, { recursive: true });

for (const [name, inner] of Object.entries(ICONS)) {
  const r = new Resvg(wrap(inner), {
    background: 'rgba(0,0,0,0)',
    fitTo: { mode: 'width', value: 256 },
    shapeRendering: 2,
  });
  const file = path.join(outDir, `${name}.png`);
  fs.writeFileSync(file, r.render().asPng());
  console.log('wrote', file);

  if (previewDir) {
    const p = new Resvg(wrapPreview(inner, PREVIEW_TILE_COLORS[name] || '#666'), {
      fitTo: { mode: 'width', value: 200 },
      shapeRendering: 2,
    });
    fs.writeFileSync(path.join(previewDir, `${name}_preview.png`), p.render().asPng());
  }
}
