#!/usr/bin/env bash
#
# run-editor-builder.sh — Execute a scene-mutating Editor builder with Unity
# launched COLD in batch mode (no Editor open, no window focus needed). This is
# the builder counterpart to run-tests-headless.sh.
#
# Default target: ChannelSwitcherBuilder.BuildHeadless — builds the channel
# switcher pill into Screen_Whatsapp/ChatsPanel/TopBar/CenterZone, stamps the
# ChannelSwitcherView refs, and performs the nav restructure (removes the
# Telegram tab, relabels tab 0 «Чаты», deletes Screen_Telegram). BuildHeadless
# OPENS Main.unity, mutates it, and SAVES — so the scene is persisted on disk
# after a green run and must be committed immediately.
#
#   • Editor OPEN  -> this script REFUSES (batch mode cannot take the project
#                     lock while the Editor holds it). Quit the Editor first.
#   • Editor CLOSED -> use THIS script.
#
# Usage:
#   Tools/run-editor-builder.sh                                # ChannelSwitcherBuilder.BuildHeadless
#   Tools/run-editor-builder.sh SomeOther.EntryMethod         # override -executeMethod target
#                                                             # (sentinel auto-derives from the class segment)
#   Tools/run-editor-builder.sh Some.Entry "custom sentinel"  # override the success sentinel too
#   UNITY=/path/to/Unity Tools/run-editor-builder.sh          # override Unity binary
#
# Outputs (under Tools/test-output/, gitignored). NOT under Temp/ — Unity WIPES
# Temp/ on launch, which would delete the output dir mid-run:
#   builder.log          full Unity editor log (required: -nographics disables auto-logging)
#
# Exit codes (this script):
#   0  builder ran AND printed its success sentinel AND Unity exit code was 0
#   1  Unity ran but the success sentinel was NOT found (build error / wrong object)
#   2  could not run (Editor open, Unity binary missing)
#
# Notes baked in from run-tests-headless.sh research:
#   • -quit IS correct here for -executeMethod (UNLIKE -runTests, which must NOT
#     get -quit). BuildHeadless finishes synchronously, then Unity quits.
#   • Unity's exit code alone is NOT authoritative (1 masks lock/license/compile
#     errors) — the success sentinel in the log is the source of truth.

set -u

# --- Resolve project root (this script lives in <project>/Tools/) ----------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# --- Builder entry (optional override from $1) -----------------------------
METHOD="${1:-ChannelSwitcherBuilder.BuildHeadless}"

# --- Success sentinel the builder logs on completion -----------------------
# Derived from the method's class segment: every headless builder in this
# project logs the same shape, "[<Class>] Headless build + save complete".
# A hardcoded sentinel would make every $1 override report NOT GREEN after the
# scene had already been mutated AND saved — the opposite of reality.
# $2 (optional) = explicit override for builders with a different log line.
SENTINEL="${2:-[${METHOD%%.*}] Headless build + save complete}"

# --- Resolve the matching Unity binary (env override wins) -----------------
VERSION="$(grep -m1 '^m_EditorVersion:' "${PROJECT}/ProjectSettings/ProjectVersion.txt" 2>/dev/null | awk '{print $2}')"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"

if [ ! -x "${UNITY}" ]; then
  echo "ERROR: Unity binary not found/executable for version '${VERSION}':" >&2
  echo "       ${UNITY}" >&2
  echo "Installed editors under /Applications/Unity/Hub/Editor/:" >&2
  ls /Applications/Unity/Hub/Editor/ >&2 2>/dev/null || echo "  (none)" >&2
  exit 2
fi

# --- Lock guard: refuse if the Editor has this project open -----------------
# Authoritative signal = a non-batch Unity process holding THIS project.
# The Hub GUI launches with lowercase '-projectpath'; AssetImportWorkers use
# '-batchMode' and must be excluded. Match case-insensitively (verified fix).
# The path match is ANCHORED (exact path, optional trailing '/', then space or
# EOL) so a sibling project (e.g. .../Automation2) can't false-positively block
# this project's run — while exact/trailing-slash forms still refuse (fail-safe).
PROJECT_RE="$(printf '%s' "${PROJECT}" | sed -e 's/[][\.|$(){}?+*^]/\\&/g')"
GUI_PROC="$(pgrep -fl 'Unity.app/Contents/MacOS/Unity' 2>/dev/null \
  | grep -iE -- "-projectpath ${PROJECT_RE}/?( |\$)" \
  | grep -viE -- '-batchmode|assetimportworker' || true)"

if [ -n "${GUI_PROC}" ]; then
  echo "ERROR: Unity Editor is open on this project — refusing to launch a headless builder run." >&2
  echo "       Batch -executeMethod cannot take the project lock while the Editor holds it" >&2
  echo "       (it would abort: 'another Unity instance is running')." >&2
  echo "" >&2
  echo "  Fix: quit the Unity Editor, then re-run this script." >&2
  echo "  Or:  keep the Editor open and run the builder from the Tools menu instead" >&2
  echo "       (Tools/Channel Switcher/Build), then save the scene by hand." >&2
  echo "" >&2
  echo "  Detected: $(printf '%s' "${GUI_PROC}" | sed -E 's/(-accessToken )[^ ]*/\1<redacted>/g; s/(-hubSessionId )[^ ]*/\1<redacted>/g')" >&2
  exit 2
fi

if [ -f "${PROJECT}/Temp/UnityLockfile" ]; then
  echo "WARNING: Temp/UnityLockfile present but no running Editor detected for this project." >&2
  echo "         Likely stale (left by a previous crash); proceeding — Unity reclaims stale locks." >&2
fi

# --- Output paths ----------------------------------------------------------
# IMPORTANT: must NOT live under Temp/ — Unity clears Temp/ on launch.
OUT_DIR="${PROJECT}/Tools/test-output"
LOG="${OUT_DIR}/builder.log"
mkdir -p "${OUT_DIR}"
rm -f "${LOG}"

# --- Build args ------------------------------------------------------------
UNITY_ARGS=( -batchmode -nographics
  -projectPath "${PROJECT}"
  -executeMethod "${METHOD}"
  -quit
  -logFile "${LOG}" )

echo "Launching headless Unity ${VERSION} builder (cold start can take a few minutes)…"
echo "  Unity:   ${UNITY}"
echo "  Project: ${PROJECT}"
echo "  Method:  ${METHOD}"
echo "  Log:     ${LOG}"

# --- Run UNPIPED; capture exit code immediately ----------------------------
"${UNITY}" "${UNITY_ARGS[@]}"
UNITY_EXIT=$?
echo "Unity process exited with code ${UNITY_EXIT}."

# --- Verdict (log sentinel is the source of truth, exit code corroborates) --
if [ ! -s "${LOG}" ]; then
  echo "" >&2
  echo "FAILURE TO RUN: no builder log was produced (${LOG} missing/empty)." >&2
  echo "Unity exit code: ${UNITY_EXIT}." >&2
  exit 2
fi

if grep -qF -- "${SENTINEL}" "${LOG}"; then
  if [ "${UNITY_EXIT}" -eq 0 ]; then
    echo ""
    echo "GREEN ✅  builder sentinel found and Unity exited cleanly."
    echo "  Sentinel: ${SENTINEL}"
    echo "  Scene Assets/Scenes/Main.unity was mutated + saved — COMMIT IT NOW."
    exit 0
  fi
  echo "" >&2
  echo "WARNING: sentinel found but Unity exit code is ${UNITY_EXIT} (nonzero)." >&2
  echo "--- last 40 lines of ${LOG} ---" >&2
  tail -n 40 "${LOG}" >&2 2>/dev/null || echo "(no log)" >&2
  exit 1
fi

echo "" >&2
echo "NOT GREEN ❌  builder success sentinel NOT found in the log (build failed or wrong entry)." >&2
echo "  Expected: ${SENTINEL}" >&2
echo "Unity exit code: ${UNITY_EXIT}." >&2
echo "--- last 40 lines of ${LOG} ---" >&2
tail -n 40 "${LOG}" >&2 2>/dev/null || echo "(no log)" >&2
exit 1
