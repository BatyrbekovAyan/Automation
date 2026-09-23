#!/usr/bin/env bash
# Phone screenshots → webp (720 for 1×/phones, 1080 for 2×/desktop), brand mark SVGs,
# favicon/apple-touch from the shipped app icon (U9, ink palette).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../../.." && pwd)"
SRC="$REPO/Tools/store/screenshots"; IMG="$HERE/../src/assets/img"; STATIC="$HERE/../src/static"
mkdir -p "$IMG" "$STATIC"
# macOS ships bash 3.2 (no associative arrays) — use name:src pairs instead.
for pair in chats:04-tab0 thread-auto:02-thread-auto suggestions:03-suggestions svodka:04-tab1 \
            bots:04-tab2 paywall:05-paywall pricelist:06-settings-products prompt:07-settings-prompt; do
  name=${pair%%:*}
  src=${pair#*:}
  for w in 720 1080; do
    ffmpeg -loglevel error -y -i "$SRC/$src.png" -vf "scale=${w}:-1:flags=lanczos" -c:v libwebp -quality 84 "$IMG/shot-$name-$w.webp"
  done
done
# App icon + bare mark (vector)
node -e '
const {P, iconSvg} = require(process.argv[1] + "/Tools/icon-lab/appicon/build");
const {concepts} = require(process.argv[1] + "/Tools/icon-lab/appicon/concepts_round7");
const u9 = concepts.find((c) => c.id === "U9");
require("fs").writeFileSync(process.argv[2] + "/app-icon.svg", iconSvg(u9, P.ink));
' "$REPO" "$IMG"
cat > "$IMG/mark.svg" <<'SVG'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024"><rect x="110" y="208" width="804" height="142" rx="71" fill="#5981D6" fill-opacity=".28"/><rect x="110" y="420" width="804" height="184" rx="92" fill="#5981D6"/><rect x="110" y="674" width="804" height="142" rx="71" fill="#5981D6" fill-opacity=".28"/></svg>
SVG
cp "$IMG/app-icon.svg" "$STATIC/favicon.svg"
# Raster icons via headless Chrome screenshot of the SVG (exact rendering)
node "$HERE/raster-icon.mjs" "$IMG/app-icon.svg" "$STATIC/favicon.png" 64
node "$HERE/raster-icon.mjs" "$IMG/app-icon.svg" "$STATIC/apple-touch-icon.png" 180
ls -la "$IMG" "$STATIC"
