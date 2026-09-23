#!/usr/bin/env bash
# Pin and self-host the motion libraries (no third-party CDN at runtime).
set -euo pipefail
cd "$(dirname "$0")/../src/assets/vendor"
GSAP=3.13.0; LENIS=1.3.11
curl -fsSL "https://cdn.jsdelivr.net/npm/gsap@${GSAP}/dist/gsap.min.js" -o gsap.min.js
curl -fsSL "https://cdn.jsdelivr.net/npm/gsap@${GSAP}/dist/ScrollTrigger.min.js" -o ScrollTrigger.min.js
curl -fsSL "https://cdn.jsdelivr.net/npm/lenis@${LENIS}/dist/lenis.min.js" -o lenis.min.js
{
  echo "# Vendored libraries"
  echo "GSAP ${GSAP} (standard no-charge license), Lenis ${LENIS} (MIT). Fetched by tools/fetch-vendor.sh."
  echo; echo '```'; shasum -a 256 *.js; echo '```'
} > VENDOR.md
ls -la
