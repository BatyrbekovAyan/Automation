// Business-type tile icons are no longer authored here.
//
// The glyph geometry moved to Tools/icon-lab/glyphs/bt_*.svg, which is rendered
// and published by Tools/icon-lab/publish.js. This file stays as the documented
// entry point and simply delegates, because the copy of the geometry it used to
// hold inline would otherwise sit here going stale and silently overwrite the
// published icons the next time someone ran it.
//
// Usage: cd Tools && node render_business_icons.js
const { execFileSync } = require('child_process');
const path = require('path');

execFileSync('node', [path.join(__dirname, 'icon-lab', 'publish.js'), ...process.argv.slice(2)], {
  stdio: 'inherit',
});
