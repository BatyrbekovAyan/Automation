#!/bin/bash
# probe-message.sh — READ-ONLY single-message probe for Wappi tapi (Telegram).
# Companion to capture-shapes.sh: fetches ONE message by id via
#   GET /tapi/sync/messages/id/get?profile_id=<id>&message_id=<mid>
# and writes it to Tools/tapi/samples/probe_<mid>.json (gitignored).
#
# Same guarantees as capture-shapes.sh:
#   * ONLY this one GET endpoint — never sends/deletes/auths/webhooks, never mark_all
#   * token read locally from secrets.json, passed via curl --config (never argv,
#     never printed, never written to samples)
# Usage: probe-message.sh <message_id> [<message_id> ...]
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SECRETS="${REPO_ROOT}/Assets/StreamingAssets/secrets.json"
OUT_DIR="${SCRIPT_DIR}/samples"
BASE="https://wappi.pro"

[ $# -ge 1 ] || { echo "usage: $0 <message_id> [...]"; exit 2; }
command -v jq >/dev/null 2>&1 || { echo "ERROR: jq required (brew install jq)"; exit 2; }
[ -f "${SECRETS}" ] || { echo "ERROR: secrets.json not found"; exit 2; }

TOKEN="$(jq -r '.wappiAuthToken // empty' "${SECRETS}")"
[ -n "${TOKEN}" ] || { echo "ERROR: wappiAuthToken missing in secrets.json"; exit 2; }
[[ "${TOKEN}" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "ERROR: token charset unexpected"; exit 2; }

auth_curl() { curl -sS --config <(printf 'header = "Authorization: %s"\n' "${TOKEN}") "$@"; }

# Resolve the first authorized tg profile (same discovery as capture-shapes.sh)
PROFILES="$(auth_curl "${BASE}/tapi/profile/all/get")" || { echo "ERROR: profile list fetch failed"; exit 4; }
PROFILE_ID="$(printf '%s' "${PROFILES}" | jq -r '[.profiles[]? | select(.platform=="tg" and .authorized==true)][0].profile_id // empty')"
[ -n "${PROFILE_ID}" ] || { echo "ERROR: no authorized tg profile"; exit 3; }

mkdir -p "${OUT_DIR}"
for MID in "$@"; do
  [[ "${MID}" =~ ^[A-Za-z0-9@._-]+$ ]] || { echo "SKIP unsafe id: ${MID}"; continue; }
  OUT="${OUT_DIR}/probe_${MID}.json"
  auth_curl "${BASE}/tapi/sync/messages/id/get?profile_id=${PROFILE_ID}&message_id=${MID}" -o "${OUT}" \
    && echo "wrote ${OUT}" || echo "FAILED ${MID}"
done
