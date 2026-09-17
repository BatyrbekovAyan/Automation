#!/usr/bin/env bash
# Inspect a REAL release artifact (.apk / .aab / .ipa / .app) for client-side secrets (audit A1, 2026-09-17).
#
#   Tools/store/inspect-artifact.sh Builds/Android/app-release.aab
#   Tools/store/inspect-artifact.sh path/to/App.ipa
#
# Exit 0 = the bundled StreamingAssets/secrets.json carries ONLY the allowed keys (n8nBaseUrl,
# revenueCat.iosKey/androidKey — RevenueCat public SDK keys are designed to ship) and no Wappi
# token / n8n API key / Telegram bot token string appears anywhere in the archive's text assets.
# Exit 1 = a forbidden key or value was found (names + locations printed, never the values).
# Exit 2 = usage / unreadable artifact.
#
# Removing the field from the SOURCE does not prove the shipped binary is clean (an Append build
# or a stale StreamingAssets folder can carry a key forward) — run this on the artifact you upload.
set -euo pipefail
ART="${1:-}"
[ -f "${ART}" ] || { echo "usage: $0 <apk|aab|ipa|app.zip>" >&2; exit 2; }
TMP="$(mktemp -d)"; trap 'rm -rf "${TMP}"' EXIT
unzip -q -o "${ART}" -d "${TMP}" 2>/dev/null || { echo "cannot unzip ${ART}" >&2; exit 2; }
FOUND="$(find "${TMP}" -type f -name 'secrets.json' | head -20)"
[ -n "${FOUND}" ] || { echo "no secrets.json inside ${ART} (StreamingAssets missing?)" >&2; exit 2; }
FAIL=0
while IFS= read -r f; do
  echo "== $(echo "$f" | sed "s#${TMP}/##")"
  python3 - "$f" <<'PY' || FAIL=1
import json, sys
allowed_top = {"n8nBaseUrl", "revenueCat"}
allowed_rc = {"iosKey", "androidKey"}
d = json.load(open(sys.argv[1]))
bad = sorted(set(d) - allowed_top) + [f"revenueCat.{k}" for k in (set(d.get("revenueCat") or {}) - allowed_rc)]
print("   keys:", sorted(d), " revenueCat:", sorted((d.get("revenueCat") or {}).keys()))
if bad:
    print("   FORBIDDEN keys present:", bad)
    sys.exit(1)
print("   OK: only allowed keys")
PY
done <<< "${FOUND}"
# Value-level sweep across text assets: names of known secret shapes only, never printing values.
if grep -rIl --exclude='*.png' --exclude='*.jpg' -E 'wappiAuthToken|n8nAPIKey|telegramBotToken|supportChatId' "${TMP}" >/dev/null 2>&1; then
  echo "FORBIDDEN identifiers found in:"; grep -rIl -E 'wappiAuthToken|n8nAPIKey|telegramBotToken|supportChatId' "${TMP}" | sed "s#${TMP}/#   #"
  FAIL=1
fi
[ "${FAIL}" -eq 0 ] && echo "ARTIFACT CLEAN: ${ART}" || echo "ARTIFACT NOT CLEAN: ${ART}"
exit "${FAIL}"
