#!/usr/bin/env bash
#
# capture-shapes.sh — READ-ONLY Wappi tapi (Telegram) live-shape capture.
#
# WHAT THIS IS
#   A transparent, owner-run probe that captures REAL Wappi tapi (Telegram)
#   response shapes so the Phase-5 Telegram parser/media work is grounded in
#   observed JSON instead of undocumented guesses. It feeds the 13 open shape
#   questions recorded in Tools/tapi/SHAPES.md.
#
# READ-ONLY GUARANTEE (owner trust matters — this is a hard invariant)
#   This script calls ONLY GET / list endpoints. It will NOT:
#     - send, reply to, or react to any message
#     - add, delete, log out, or otherwise mutate any profile
#     - make any auth call (QR / phone / code / 2FA)
#     - change any webhook configuration
#     - pass the mark_all parameter (which would silently mark real chats read
#       via a GET-shaped endpoint on the owner's own account)
#   The 8 read endpoints it MAY call:
#     tapi/profile/all/get, tapi/sync/get/status, tapi/sync/chats/get,
#     tapi/sync/chats/filter, tapi/sync/chats/days/get, tapi/sync/messages/get,
#     tapi/sync/messages/id/get, tapi/sync/contact/get
#
# TOKEN SAFETY
#   The Wappi token is read LOCALLY at runtime from
#   Assets/StreamingAssets/secrets.json (never hardcoded, never printed, never
#   written to a sample file, never passed as an argument). It is used only in
#   the Authorization request header. Response BODIES are written to disk;
#   request headers are not.
#
# OUTPUT
#   Sanitized JSON samples + an INDEX.json map land under Tools/tapi/samples/
#   which is gitignored (raw payloads may contain phone numbers / names).
#   Fill the verdicts in Tools/tapi/SHAPES.md afterwards (see README.md).
#
# USAGE
#   Tools/tapi/capture-shapes.sh                 # auto-detect first authorized TG profile
#   Tools/tapi/capture-shapes.sh --profile <id>  # override profile selection
#   Tools/tapi/capture-shapes.sh --chats 8       # sample more chats (default 5)
#   Tools/tapi/capture-shapes.sh --dry-run       # print the endpoint plan; no network, no token read
#   Tools/tapi/capture-shapes.sh --help
#
# EXIT CODES
#   0  success (or --dry-run / --help)
#   2  guard failure (missing jq/curl, missing secrets, bad argument)
#   3  no authorized Telegram profile found (authorize one in-app first)
#
# Conventions mirror Tools/run-tests-headless.sh.

set -u

# --- Resolve project root (this script lives in <project>/Tools/tapi/) ------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

BASE="https://wappi.pro"
SECRETS="${PROJECT}/Assets/StreamingAssets/secrets.json"
# Samples land under Tools/tapi/samples/ (gitignored — may contain PII).
SAMPLES_DIR="${SCRIPT_DIR}/samples"

# --- Defaults --------------------------------------------------------------
PROFILE_ID=""
CHATS_N=5
DRY_RUN=0

# --- Banner (printed first, ALWAYS) ----------------------------------------
print_banner() {
  cat >&2 <<'BANNER'
================================================================
  capture-shapes.sh — READ-ONLY Wappi tapi (Telegram) probe
----------------------------------------------------------------
  This tool ONLY reads. It will NOT:
    * send, reply to, or react to any message
    * add, delete, or log out any profile
    * run any auth step (QR / phone / code / 2FA)
    * change any webhook configuration
    * pass the mark_all parameter (would mark your chats read)
  Your Wappi token is read locally from secrets.json and used
  only in the Authorization header — never printed, never saved.
  Samples are written to Tools/tapi/samples/ (gitignored).
================================================================
BANNER
}

print_help() {
  cat >&2 <<'HELP'
Usage: Tools/tapi/capture-shapes.sh [--profile <id>] [--chats <N>] [--dry-run] [--help]

  --profile <id>   Override auto-detection; use this Telegram profile id.
  --chats <N>      Number of chats to sample messages from (default 5).
  --dry-run        Print the exact read-only endpoint plan and exit.
                   Makes NO network call and does NOT read the token.
  --help           Show this help.

Output: sanitized JSON samples + INDEX.json under Tools/tapi/samples/ (gitignored).
Next:   open Tools/tapi/SHAPES.md and fill each of the 13 verdicts (see README.md).
HELP
}

# --- Parse args ------------------------------------------------------------
while [ $# -gt 0 ]; do
  case "$1" in
    --profile)   [ $# -ge 2 ] || { print_banner; echo "ERROR: --profile requires a value." >&2; exit 2; }
                 PROFILE_ID="$2"; shift 2 ;;
    --profile=*) PROFILE_ID="${1#*=}"; shift ;;
    --chats)     [ $# -ge 2 ] || { print_banner; echo "ERROR: --chats requires a value." >&2; exit 2; }
                 CHATS_N="$2"; shift 2 ;;
    --chats=*)   CHATS_N="${1#*=}"; shift ;;
    --dry-run)   DRY_RUN=1; shift ;;
    -h|--help)   print_banner; print_help; exit 0 ;;
    *)           print_banner; echo "ERROR: unknown argument: $1" >&2; print_help; exit 2 ;;
  esac
done

# Validate user-supplied args are safe URL-query values (no shell/URL injection).
if [[ ! "${PROFILE_ID}" =~ ^[A-Za-z0-9_-]*$ ]]; then
  print_banner
  echo "ERROR: --profile must contain only letters, digits, '_' or '-'." >&2
  exit 2
fi
if [[ ! "${CHATS_N}" =~ ^[0-9]+$ ]]; then
  print_banner
  echo "ERROR: --chats must be a positive integer." >&2
  exit 2
fi

print_banner

# --- Dry run: print the plan, touch nothing --------------------------------
if [ "${DRY_RUN}" -eq 1 ]; then
  cat >&2 <<PLAN

DRY RUN — the following READ-ONLY GET requests WOULD be made (none sent now,
token NOT read). Base = ${BASE}

  1. GET  /tapi/profile/all/get                       -> pick first platform=tg, authorized profile
  2. GET  /tapi/sync/get/status?profile_id=<id>       -> connection status
  3. GET  /tapi/sync/chats/get?profile_id=<id>        -> plain chat list
  4. GET  /tapi/sync/chats/filter?profile_id=<id>     -> filtered chat list
  5. GET  /tapi/sync/chats/days/get?profile_id=<id>   -> chats-by-days (thumbnail evidence)
  6. GET  /tapi/sync/messages/get?...&limit=100&offset=0&order=   (${CHATS_N} chats; NO mark_all)
  7. GET  /tapi/sync/messages/id/get?...&message_id=<mid>         -> reply + reactions target
  8. GET  /tapi/sync/contact/get?...&recipient=<cid>             -> native avatar

Coverage preview (§11 questions this run can feed):
  Q1/Q2  media JSON per type + sticker/GIF type strings  <- messages/get + message_type_*.json
  Q3     reactions transport                              <- messages/id/get target
  Q4     group/channel dialog types                       <- chats/*/get
  Q5/Q7  name/thumbnail/id-empty + isDeleted              <- chats/filter + chats/days/get
  Q6     last_timestamp runtime type                      <- chats/get
  Q8     reply snapshot echo                              <- messages/id/get (isReply)
  Q9-Q13 DEFERRED — not observable via read-only capture (see SHAPES.md)

Profile: ${PROFILE_ID:-"(auto-detect)"}
Nothing was sent. Re-run without --dry-run to capture.
PLAN
  exit 0
fi

# --- Guards ----------------------------------------------------------------
if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: 'jq' is required but was not found." >&2
  echo "  RU: установите jq — напр.  brew install jq   (macOS) /  sudo apt-get install jq  (Linux)" >&2
  echo "  EN: install jq — e.g.  brew install jq   (macOS) /  sudo apt-get install jq  (Linux)" >&2
  exit 2
fi
if ! command -v curl >/dev/null 2>&1; then
  echo "ERROR: 'curl' is required but was not found." >&2
  exit 2
fi
if [ ! -f "${SECRETS}" ]; then
  echo "ERROR: secrets file not found: ${SECRETS}" >&2
  echo "  Create it from Assets/StreamingAssets/secrets.json.example (see CLAUDE.md secrets policy)." >&2
  exit 2
fi

# --- Read the token LOCALLY (never printed / logged / written to samples) ---
TOKEN="$(jq -r '.wappiAuthToken // empty' "${SECRETS}")"
if [ -z "${TOKEN}" ]; then
  echo "ERROR: no auth token found in secrets.json (key is missing or empty)." >&2
  exit 2
fi
# The token is sent via a curl --config read from a process substitution (see
# auth_curl below), so it never appears on curl's argv — argv is readable by
# any local process via 'ps' while curl runs. Reject charsets that could break
# the config-file quoting ('"' or '\') before that config is ever built.
if [[ ! "${TOKEN}" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "ERROR: token in secrets.json has unexpected characters." >&2
  exit 2
fi

# --- auth_curl <curl args...> : adds the Authorization header WITHOUT putting
#     the token on curl's argv. printf is a bash builtin (no extra process) and
#     the config travels over a /dev/fd/N pipe: never on disk, never in 'ps'.
auth_curl() {
  curl --config <(printf 'header = "Authorization: %s"\n' "${TOKEN}") "$@"
}

mkdir -p "${SAMPLES_DIR}"

# --- fetch <label> <url> <outfile> : write response BODY only, degrade on non-2xx
fetch() {
  local label="$1" url="$2" outfile="$3" http_code
  http_code="$(auth_curl -s -o "${outfile}.raw" -w '%{http_code}' "${url}")"
  http_code="${http_code:-000}"
  if [ "${http_code}" -ge 200 ] && [ "${http_code}" -lt 300 ]; then
    if jq . "${outfile}.raw" > "${outfile}" 2>/dev/null; then
      rm -f "${outfile}.raw"
    else
      mv -f "${outfile}.raw" "${outfile}"   # non-JSON body: keep raw for inspection
    fi
  else
    mv -f "${outfile}.raw" "${outfile}"      # DEGRADE: keep the real error body for parser work
    echo "WARN: ${label} returned HTTP ${http_code}" >&2
  fi
}

# --- list_json <files...> : JSON array of the basenames that exist ----------
list_json() {
  local out="" f
  for f in "$@"; do
    if [ -f "$f" ]; then out="${out}$(basename "$f")
"; fi
  done
  printf '%s' "$out" | jq -R -s 'split("\n") | map(select(length > 0))'
}

# --- safe_id <id> : server-supplied chat/message ids get the same validation
#     as user args before they are interpolated into URLs (an id containing
#     '&', '?' or '#' could inject query parameters into an otherwise
#     read-only GET) or filenames (an id containing '/' could escape
#     SAMPLES_DIR). Anything with an unexpected shape is skipped with a WARN.
safe_id() { [[ "$1" =~ ^[A-Za-z0-9@._-]+$ ]]; }

# --- Profile selection ------------------------------------------------------
if [ -z "${PROFILE_ID}" ]; then
  PROFILES_JSON="$(auth_curl -s "${BASE}/tapi/profile/all/get")"
  PROFILE_ID="$(printf '%s' "${PROFILES_JSON}" | jq -r '
    [ ( if type=="array" then .[]
        elif has("profiles") then .profiles[]
        elif has("data") then .data[]
        else empty end )
      | select(.platform=="tg" and .authorized==true) ][0].profile_id // empty
  ' 2>/dev/null)"
fi

if [ -z "${PROFILE_ID}" ]; then
  echo "" >&2
  echo "No authorized Telegram profile found." >&2
  echo "  RU: сначала авторизуйте dev-профиль Telegram в приложении (Настройки → Telegram), затем запустите снова." >&2
  echo "  EN: authorize a dev Telegram profile in-app first (Settings -> Telegram auth), then re-run." >&2
  echo "  (Or pass --profile <id> if you know the profile id.)" >&2
  exit 3
fi

echo "Using Telegram profile (platform=tg, authorized). Capturing read-only samples..." >&2

# --- 1. status --------------------------------------------------------------
fetch "get/status" \
  "${BASE}/tapi/sync/get/status?profile_id=${PROFILE_ID}" \
  "${SAMPLES_DIR}/status.json"

# --- 2-4. all three list endpoints (docs contradict each other) -------------
fetch "chats/get" \
  "${BASE}/tapi/sync/chats/get?profile_id=${PROFILE_ID}&limit=200&offset=0" \
  "${SAMPLES_DIR}/chats_get.json"
fetch "chats/filter" \
  "${BASE}/tapi/sync/chats/filter?profile_id=${PROFILE_ID}" \
  "${SAMPLES_DIR}/chats_filter.json"
fetch "chats/days/get" \
  "${BASE}/tapi/sync/chats/days/get?profile_id=${PROFILE_ID}" \
  "${SAMPLES_DIR}/chats_days_get.json"

# --- Gather chat ids (prefer chats/get, fall back to filter then days) -------
get_chat_ids() {
  local f ids
  for f in chats_get chats_filter chats_days_get; do
    ids="$(jq -r '(.dialogs // .data // [])[]? | .id // empty' "${SAMPLES_DIR}/${f}.json" 2>/dev/null | grep -v '^$')"
    if [ -n "${ids}" ]; then printf '%s\n' "${ids}"; return 0; fi
  done
}
# Filter at the source so EVERY downstream use of CHAT_IDS (URL interpolation
# and messages_<id>.json read/write paths) only ever sees safe_id-clean ids.
FETCHED_CHAT_IDS="$(get_chat_ids | grep -v '^$' | head -n "${CHATS_N}")"
CHAT_IDS=""
for CID in ${FETCHED_CHAT_IDS}; do
  if safe_id "${CID}"; then
    CHAT_IDS="${CHAT_IDS}${CID}
"
  else
    echo "WARN: skipping chat with unexpected id shape." >&2
  fi
done

if [ -z "${CHAT_IDS}" ]; then
  echo "WARN: no chat ids found in any list endpoint — messages/contact steps skipped." >&2
fi

# --- 6. messages/get per chat + media-variety auto-detection ----------------
ALL_TYPES=""
for CID in ${CHAT_IDS}; do
  MSGFILE="${SAMPLES_DIR}/messages_${CID}.json"
  fetch "messages/get[chat ${CID}]" \
    "${BASE}/tapi/sync/messages/get?profile_id=${PROFILE_ID}&chat_id=${CID}&limit=100&offset=0&order=" \
    "${MSGFILE}"
  T="$(jq -r '(.messages // .data // [])[]? | .type // empty' "${MSGFILE}" 2>/dev/null)"
  ALL_TYPES="${ALL_TYPES}
${T}"
done
DISTINCT_TYPES="$(printf '%s\n' "${ALL_TYPES}" | grep -v '^$' | sort -u)"

# Save ONE full message sample per distinct type encountered.
for TY in ${DISTINCT_TYPES}; do
  TY_SAFE="$(printf '%s' "${TY}" | tr -c 'A-Za-z0-9_-' '_')"
  OUT="${SAMPLES_DIR}/message_type_${TY_SAFE}.json"
  for CID in ${CHAT_IDS}; do
    OBJ="$(jq -c --arg t "${TY}" 'first((.messages // .data // [])[]? | select(.type==$t)) // empty' \
      "${SAMPLES_DIR}/messages_${CID}.json" 2>/dev/null)"
    if [ -n "${OBJ}" ] && [ "${OBJ}" != "null" ]; then
      printf '%s' "${OBJ}" | jq . > "${OUT}" 2>/dev/null
      break
    fi
  done
done

# --- 7. messages/id/get : reply (isReply) + reactions target ----------------
REPLY_MID="$(for CID in ${CHAT_IDS}; do
  jq -r 'first((.messages // .data // [])[]? | select(.isReply==true) | .id) // empty' \
    "${SAMPLES_DIR}/messages_${CID}.json" 2>/dev/null
done | grep -v '^$' | head -n 1)"

if [ -n "${REPLY_MID}" ] && ! safe_id "${REPLY_MID}"; then
  echo "WARN: reply message id has unexpected shape — reply-snapshot sample skipped." >&2
  REPLY_MID=""
fi
if [ -n "${REPLY_MID}" ]; then
  fetch "messages/id/get[reply ${REPLY_MID}]" \
    "${BASE}/tapi/sync/messages/id/get?profile_id=${PROFILE_ID}&message_id=${REPLY_MID}" \
    "${SAMPLES_DIR}/message_id_reply.json"
else
  echo "WARN: no isReply message found — reply-snapshot sample skipped." >&2
fi

# Candidate ids for the reactions-target probe (reactions field only surfaces on id/get).
CAND_IDS="$(for CID in ${CHAT_IDS}; do
  jq -r '(.messages // .data // [])[]? | .id // empty' \
    "${SAMPLES_DIR}/messages_${CID}.json" 2>/dev/null
done | grep -v '^$' | head -n 8)"

FULL_DONE=0
REACT_DONE=0
N=0
for MID in ${CAND_IDS}; do
  safe_id "${MID}" || { echo "WARN: skipping message with unexpected id shape." >&2; continue; }
  N=$((N + 1)); [ "${N}" -gt 6 ] && break
  OUT="${SAMPLES_DIR}/message_id_${MID}.json"
  fetch "messages/id/get[${MID}]" \
    "${BASE}/tapi/sync/messages/id/get?profile_id=${PROFILE_ID}&message_id=${MID}" \
    "${OUT}"
  if [ "${FULL_DONE}" -eq 0 ]; then cp "${OUT}" "${SAMPLES_DIR}/message_id_full.json" 2>/dev/null; FULL_DONE=1; fi
  HAS="$(jq -r '((.message.reactions // .reactions) // null) | if . == null then "n" else "y" end' "${OUT}" 2>/dev/null || echo n)"
  if [ "${HAS}" = "y" ] && [ "${REACT_DONE}" -eq 0 ]; then
    cp "${OUT}" "${SAMPLES_DIR}/message_id_reactions.json" 2>/dev/null
    REACT_DONE=1
  fi
done
[ "${REACT_DONE}" -eq 0 ] && echo "NOTE: no message with a non-null reactions field found (see message_id_full.json for the field shape)." >&2

# --- 8. contact/get for one dialog (native-avatar evidence) -----------------
FIRST_CID="$(printf '%s\n' ${CHAT_IDS} | grep -v '^$' | head -n 1)"
if [ -n "${FIRST_CID}" ]; then
  fetch "contact/get[${FIRST_CID}]" \
    "${BASE}/tapi/sync/contact/get?profile_id=${PROFILE_ID}&recipient=${FIRST_CID}" \
    "${SAMPLES_DIR}/contact.json"
fi

# --- INDEX.json : which sample answers which §11 question -------------------
CAPTURED_AT="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
jq -n \
  --arg captured_at "${CAPTURED_AT}" \
  --argjson q1 "$(list_json "${SAMPLES_DIR}"/message_type_*.json "${SAMPLES_DIR}"/messages_*.json)" \
  --argjson q2 "$(list_json "${SAMPLES_DIR}"/message_type_*.json)" \
  --argjson q3 "$(list_json "${SAMPLES_DIR}/message_id_reactions.json" "${SAMPLES_DIR}/message_id_full.json" "${SAMPLES_DIR}"/messages_*.json)" \
  --argjson q4 "$(list_json "${SAMPLES_DIR}/chats_get.json" "${SAMPLES_DIR}/chats_filter.json" "${SAMPLES_DIR}/chats_days_get.json" "${SAMPLES_DIR}"/messages_*.json)" \
  --argjson q5 "$(list_json "${SAMPLES_DIR}/chats_filter.json" "${SAMPLES_DIR}/chats_get.json" "${SAMPLES_DIR}/chats_days_get.json" "${SAMPLES_DIR}/contact.json")" \
  --argjson q6 "$(list_json "${SAMPLES_DIR}/chats_get.json" "${SAMPLES_DIR}/chats_filter.json")" \
  --argjson q7 "$(list_json "${SAMPLES_DIR}/chats_get.json" "${SAMPLES_DIR}/chats_days_get.json")" \
  --argjson q8 "$(list_json "${SAMPLES_DIR}/message_id_reply.json" "${SAMPLES_DIR}"/messages_*.json)" \
  --argjson avatar "$(list_json "${SAMPLES_DIR}/contact.json" "${SAMPLES_DIR}/chats_days_get.json")" \
  '{
     captured_at: $captured_at,
     profile_platform: "tg",
     questions: {
       "1": $q1, "2": $q2, "3": $q3, "4": $q4, "5": $q5,
       "6": $q6, "7": $q7, "8": $q8,
       "9": [], "10": [], "11": [], "12": [], "13": []
     },
     deferred_questions: {
       "9":  "resend-code cooldown — not observable read-only (Phase 4 e2e / Phase 8 UAT)",
       "10": "webhook payloads — needs live tunnel (Phase 4 e2e)",
       "11": "quoted_message_id on send — send-side (Phase 4/5)",
       "12": "typing/start effect — send-side (Phase 5)",
       "13": "mark_all read mutation — mutating, not run here (Phase 5/8)"
     },
     avatar_evidence: $avatar
   }' > "${SAMPLES_DIR}/INDEX.json"

# --- Final report (stderr; no token, no PII) --------------------------------
COUNT="$(find "${SAMPLES_DIR}" -maxdepth 1 -type f -name '*.json' 2>/dev/null | wc -l | tr -d ' ')"
echo "" >&2
echo "Done. Wrote ${COUNT} sample file(s) to Tools/tapi/samples/ (gitignored)." >&2
echo "Distinct message types captured: $(printf '%s' "${DISTINCT_TYPES}" | tr '\n' ' ')" >&2
echo "Next: open Tools/tapi/SHAPES.md and use samples/INDEX.json to fill each of the 13 verdicts + the reactions go/no-go." >&2
