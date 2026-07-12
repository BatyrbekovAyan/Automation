---
phase: 03-tapi-live-shape-capture
reviewed: 2026-07-12T12:28:00Z
depth: standard
files_reviewed: 4
files_reviewed_list:
  - Tools/tapi/capture-shapes.sh
  - Tools/tapi/SHAPES.md
  - Tools/tapi/README.md
  - .gitignore
findings:
  critical: 1
  warning: 3
  info: 2
  total: 6
status: issues_found
---

# Phase 3: Code Review Report

**Reviewed:** 2026-07-12T12:28:00Z
**Depth:** standard
**Files Reviewed:** 4
**Status:** issues_found

## Summary

Reviewed the owner-run tapi live-shape capture tooling: `capture-shapes.sh` (read-only bash probe), `SHAPES.md` (verdict record), `README.md` (owner instructions), and the `.gitignore` addition. Several claims were verified empirically on this machine (macOS `/bin/bash` 3.2.57, curl 8.7.1), not just by inspection.

**Invariants verified as HOLDING:**

- **Read-only guarantee (as written):** every curl invocation is a plain GET (no `-d`, `-X`, `--data*`, `--form` anywhere); the 8 URLs constructed match the 8 documented read endpoints exactly; `mark_all` appears nowhere in any URL. No auth, profile-mutation, or webhook endpoint is reachable from any code path. (One *theoretical* injection channel exists via server-supplied ids — see WR-02.)
- **Token never printed/logged/written:** `TOKEN` appears at exactly 3 sites (assignment L176, `fetch()` L188, profile listing L214). No `echo`/`printf` of it, no `set -x`, error paths print only labels + HTTP codes, URLs never embed the token, only response *bodies* are written to samples. `--dry-run` genuinely exits (L155) before the token read (L176). However, the token IS on curl's argv — see CR-01.
- **bash 3.2 compatibility:** no `mapfile`, `${var,,}`, or associative arrays; `[[ =~ ]]` patterns are unquoted (required on 3.2); tested constructs run clean on `/bin/bash` 3.2.57. One arg-parsing hang found — see WR-01.
- **Guards:** missing `jq`/`curl`/secrets each produce a clear exit-2 with install hints; non-2xx responses degrade (keep error body + WARN) instead of dying; empty chat list and no-reply/no-reaction paths are all guarded with WARN/NOTE messages.
- **`--profile` / `--chats` injection:** both validated with strict character classes (`^[A-Za-z0-9_-]*$` / `^[0-9]+$`) *before* any URL use — user-argument injection is closed.
- **gitignore:** verified with `git check-ignore -v` — `.gitignore:74` (`Tools/tapi/samples/`, root-anchored) matches files inside `Tools/tapi/samples/`. Correct.
- **Secrets key path:** `.wappiAuthToken` matches the top-level key in `Assets/StreamingAssets/secrets.json.example` (verified against the example, not the deny-ruled real file).
- **Docs:** README exit codes match the implementation; SHAPES.md's 13 questions + go/no-go align with the script's INDEX.json keys (1–8 populated, 9–13 deferred with honest reasons); cross-referenced files (`.planning/research/telegram-parity/tapi-shapes.md`, `03-HUMAN-UAT.md`) exist.

**Key concerns:** the token is exposed on curl's argv (visible in `ps`), directly contradicting the script's own documented guarantee (CR-01); a reproducible infinite-loop hang on a missing option value (WR-01); and server-supplied ids flow unvalidated into URLs and output filenames (WR-02).

## Critical Issues

### CR-01: Wappi token passed on curl argv — visible in `ps`, contradicts the documented guarantee

**File:** `Tools/tapi/capture-shapes.sh:187-188` (also `:214`)
**Issue:** Both curl call sites pass the secret as a command-line argument:

```bash
http_code="$(curl -s -o "${outfile}.raw" -w '%{http_code}' \
  -H "Authorization: ${TOKEN}" "${url}")"          # L187-188
PROFILES_JSON="$(curl -s -H "Authorization: ${TOKEN}" "${BASE}/tapi/profile/all/get")"  # L214
```

On macOS, any local process/user can read another process's argv via `ps -ef` while curl runs — and this script spawns 15+ sequential curl processes per capture (CWE-214). This directly contradicts the script's stated hard invariant: header L27 ("never passed as an argument"), banner L78-79, and `Tools/tapi/README.md:22` make the same claim to the owner. The whole point of this artifact is owner trust, so the guarantee must be true as written.

**Fix:** Pass the header via curl's `--config` read from a process substitution (never touches disk, never on argv — `/dev/fd/N` is what appears in `ps`). Verified working on this machine (`/bin/bash` 3.2.57 + curl 8.7.1):

```bash
# helper near TOKEN read; also validate the token charset so the curl
# config quoting can never be broken by a stray '"' or '\'
if [[ ! "${TOKEN}" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "ERROR: token in secrets.json has unexpected characters." >&2; exit 2
fi

auth_curl() {  # auth_curl <curl args...>  — token never on argv
  curl --config <(printf 'header = "Authorization: %s"\n' "${TOKEN}") "$@"
}

# fetch() becomes:
http_code="$(auth_curl -s -o "${outfile}.raw" -w '%{http_code}' "${url}")"
# profile listing becomes:
PROFILES_JSON="$(auth_curl -s "${BASE}/tapi/profile/all/get")"
```

No doc change needed — the fix makes the existing claims true.

## Warnings

### WR-01: `--profile` / `--chats` as the last argument hangs the script in a silent infinite loop

**File:** `Tools/tapi/capture-shapes.sh:103,105`
**Issue:** `shift 2` when only 1 positional parameter remains fails *without shifting* in bash, so the `while [ $# -gt 0 ]` loop never terminates. Reproduced on macOS `/bin/bash` 3.2.57: `Tools/tapi/capture-shapes.sh --profile` spins forever — and since arg parsing happens before `print_banner` (L125), it hangs with **zero output**, which is exactly the failure mode that erodes owner trust in an owner-run tool. (`set -e` is not in effect, so the failed `shift` is silently ignored.)
**Fix:**

```bash
--profile)   [ $# -ge 2 ] || { print_banner; echo "ERROR: --profile requires a value." >&2; exit 2; }
             PROFILE_ID="$2"; shift 2 ;;
--chats)     [ $# -ge 2 ] || { print_banner; echo "ERROR: --chats requires a value." >&2; exit 2; }
             CHATS_N="$2"; shift 2 ;;
```

### WR-02: Server-supplied chat/message ids interpolated unvalidated into URLs and output filenames

**File:** `Tools/tapi/capture-shapes.sh:267-271,299-301,317-320,331-335`
**Issue:** `CID`, `MID`, `REPLY_MID`, and `FIRST_CID` come from jq over API response bodies and are interpolated raw into:
- **URLs** — e.g. L270 `...&chat_id=${CID}&limit=100...`. An id containing `&` would inject query parameters; notably, a hostile/corrupt id like `123&mark_all=true` reaching `messages/get` would breach the script's hardest invariant (mutating the owner's read state) through an otherwise read-only call. Ids with `#`, `?`, or spaces silently break the request instead.
- **Filenames** — L268 `messages_${CID}.json`, L317 `message_id_${MID}.json`. An id containing `/` (e.g. `../../x`) writes outside `SAMPLES_DIR` (path traversal). The script already sanitizes message *types* for filenames (`TY_SAFE`, L280) but not ids.

Exploitation requires wappi.pro itself to return hostile ids, so likelihood is low — but the script's user-arg validation (L114) shows the right pattern; server data deserves the same treatment, and the fix is 4 lines.
**Fix:** Validate every id before use, skipping (with a WARN) anything that fails:

```bash
safe_id() { [[ "$1" =~ ^[A-Za-z0-9@._-]+$ ]]; }   # no & # ? / or spaces

for CID in ${CHAT_IDS}; do
  safe_id "${CID}" || { echo "WARN: skipping unexpected chat id shape" >&2; continue; }
  ...
done
# same guard before the REPLY_MID fetch (L298) and inside the MID loop (L315)
```

### WR-03: Profile auto-detection conflates network failure / rejected token with "no authorized profile" (misleading exit 3)

**File:** `Tools/tapi/capture-shapes.sh:213-231`
**Issue:** The profile-listing curl (L214) is the only network call with no HTTP-status handling. If the network is down (`PROFILES_JSON` empty), the token is rejected (401/error object — `has("profiles")` on an error body yields false, or jq errors and is swallowed by `2>/dev/null`), or wappi returns any non-2xx, `PROFILE_ID` ends up empty and the script exits 3 telling the owner to *authorize a Telegram profile in-app* — the wrong remediation for all of those causes. Exit code 3 is documented as specifically "no authorized Telegram profile found", so the misdiagnosis leaks into the documented contract.
**Fix:** Capture the HTTP code like `fetch()` does and fail with a distinct exit-2 diagnosis first:

```bash
RESP="$(auth_curl -s -w '\n%{http_code}' "${BASE}/tapi/profile/all/get")"
HTTP="${RESP##*$'\n'}"; PROFILES_JSON="${RESP%$'\n'*}"
if [ "${HTTP:-000}" -lt 200 ] || [ "${HTTP:-000}" -ge 300 ]; then
  echo "ERROR: profile/all/get returned HTTP ${HTTP} — check network and the token in secrets.json." >&2
  exit 2
fi
```

(Bash-3.2-safe; keeps exit 3 meaning exactly what the docs say.)

## Info

### IN-01: `fetch()` `mv` errors noisily when curl creates no output file on hard connection failure

**File:** `Tools/tapi/capture-shapes.sh:197`
**Issue:** Verified locally: on DNS/connect failure, curl 8.x with `-o` does **not** create the output file (and `-w` prints `000`). The else branch's `mv -f "${outfile}.raw" "${outfile}"` then emits a raw `mv: ... No such file or directory` to stderr alongside the intended `WARN ... HTTP 000` line. Harmless (no leak, script continues), just confusing output on the exact failure path where the owner needs clarity.
**Fix:** `[ -f "${outfile}.raw" ] && mv -f "${outfile}.raw" "${outfile}"` and extend the WARN with a hint when the code is `000` (e.g. "network unreachable?").

### IN-02: `--chats 0` accepted despite the error message promising "positive integer"

**File:** `Tools/tapi/capture-shapes.sh:119-122`
**Issue:** The regex `^[0-9]+$` admits `0`, which yields `head -n 0` → zero chats sampled → a confusing "no chat ids found" WARN instead of an upfront rejection; the validation error text already says "positive integer".
**Fix:** `if [[ ! "${CHATS_N}" =~ ^[1-9][0-9]*$ ]]; then ...`

---

_Reviewed: 2026-07-12T12:28:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
