#!/usr/bin/env bash
#
# run-tests-headless.sh — Run the project's EditMode tests with Unity launched
# COLD in batch mode (no Editor open, no window focus needed). This is the
# fully hands-off counterpart to the in-Editor ClaudeTestBridge.
#
#   • Editor OPEN  -> use the bridge: drop Temp/claude/run-tests.trigger,
#                     read Temp/claude/test-summary.json. (This script will
#                     REFUSE, because batch mode can't take the project lock
#                     while the Editor holds it.)
#   • Editor CLOSED -> use THIS script. It launches Unity headless, runs the
#                     EditMode suite, parses the NUnit3 results, and reports.
#
# Usage:
#   Tools/run-tests-headless.sh                 # run all EditMode tests
#   Tools/run-tests-headless.sh "Chat\.Audio"   # -testFilter regex (full test name)
#   UNITY=/path/to/Unity Tools/run-tests-headless.sh   # override Unity binary
#
# Outputs (under Tools/test-output/, gitignored). NOT under Temp/ — Unity WIPES
# Temp/ on launch, which would delete the output dir mid-run:
#   results.xml          full NUnit3 result file
#   editor.log           full Unity editor log (required: -nographics disables auto-logging)
#   headless-summary.json compact machine-readable summary
#
# Exit codes (this script):
#   0  all tests green (result=Passed, failed=0, inconclusive=0)
#   1  run completed but NOT green (failures/inconclusive) — a real test result
#   2  could not run (Editor open, Unity binary missing, or no results produced)
#
# Notes baked in from research/verification:
#   • NEVER pass -quit with -runTests (it exits before tests run).
#   • -nographics is safe ONLY because these are pure-logic tests; drop it if a
#     test ever needs real GPU work (Camera.Render/ReadPixels, GI bake, etc.).
#   • Unity's exit code alone is not authoritative (1 masks lock/license/compile
#     errors) — the parsed <test-run> XML is the source of truth.

set -u

# --- Resolve project root (this script lives in <project>/Tools/) ---------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# --- Resolve the matching Unity binary (env override wins) ----------------
VERSION="$(grep -m1 '^m_EditorVersion:' "${PROJECT}/ProjectSettings/ProjectVersion.txt" 2>/dev/null | awk '{print $2}')"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"

if [ ! -x "${UNITY}" ]; then
  echo "ERROR: Unity binary not found/executable for version '${VERSION}':" >&2
  echo "       ${UNITY}" >&2
  echo "Installed editors under /Applications/Unity/Hub/Editor/:" >&2
  ls /Applications/Unity/Hub/Editor/ >&2 2>/dev/null || echo "  (none)" >&2
  exit 2
fi

# --- Lock guard: refuse if the Editor has this project open ----------------
# Authoritative signal = a non-batch Unity process holding THIS project.
# The Hub GUI launches with lowercase '-projectpath'; AssetImportWorkers use
# '-batchMode' and must be excluded. Match case-insensitively (verified fix).
GUI_PROC="$(pgrep -fl 'Unity.app/Contents/MacOS/Unity' 2>/dev/null \
  | grep -iF -- "-projectpath ${PROJECT}" \
  | grep -viE -- '-batchmode|assetimportworker' || true)"

if [ -n "${GUI_PROC}" ]; then
  echo "ERROR: Unity Editor is open on this project — refusing to launch a headless run." >&2
  echo "       Batch -runTests cannot take the project lock while the Editor holds it" >&2
  echo "       (it would abort: 'another Unity instance is running')." >&2
  echo "" >&2
  echo "  Fix: quit the Unity Editor, then re-run this script." >&2
  echo "  Or:  keep the Editor open and use the in-Editor bridge instead —" >&2
  echo "       drop Temp/claude/run-tests.trigger, read Temp/claude/test-summary.json." >&2
  echo "" >&2
  echo "  Detected: $(printf '%s' "${GUI_PROC}" | sed -E 's/(-accessToken )[^ ]*/\1<redacted>/g; s/(-hubSessionId )[^ ]*/\1<redacted>/g')" >&2
  exit 2
fi

if [ -f "${PROJECT}/Temp/UnityLockfile" ]; then
  echo "WARNING: Temp/UnityLockfile present but no running Editor detected for this project." >&2
  echo "         Likely stale (left by a previous crash); proceeding — Unity reclaims stale locks." >&2
  echo "         If the run aborts with 'another Unity instance is running', the Editor really is" >&2
  echo "         open somewhere: quit it and retry." >&2
fi

# --- Output paths ----------------------------------------------------------
# IMPORTANT: must NOT live under Temp/ — Unity clears Temp/ on launch, which
# would delete this directory (and our log/results) while the batch run is starting.
OUT_DIR="${PROJECT}/Tools/test-output"
RESULTS="${OUT_DIR}/results.xml"
LOG="${OUT_DIR}/editor.log"
SUMMARY="${OUT_DIR}/headless-summary.json"
mkdir -p "${OUT_DIR}"
rm -f "${RESULTS}" "${SUMMARY}"

# --- Build args (optional -testFilter regex from $1) -----------------------
FILTER="${1:-}"
UNITY_ARGS=( -runTests -batchmode -nographics
  -projectPath "${PROJECT}"
  -testPlatform EditMode
  -testResults "${RESULTS}"
  -logFile "${LOG}" )
if [ -n "${FILTER}" ]; then
  UNITY_ARGS+=( -testFilter "${FILTER}" )
  echo "Filter: -testFilter \"${FILTER}\""
fi

echo "Launching headless Unity ${VERSION} (this can take a few minutes on a cold start)…"
echo "  Unity:   ${UNITY}"
echo "  Project: ${PROJECT}"
echo "  Results: ${RESULTS}"
echo "  Log:     ${LOG}"

# --- Run UNPIPED; capture exit code immediately ----------------------------
"${UNITY}" "${UNITY_ARGS[@]}"
UNITY_EXIT=$?
echo "Unity process exited with code ${UNITY_EXIT}."

# --- Parse results (XML is the source of truth) ----------------------------
if [ ! -s "${RESULTS}" ]; then
  echo "" >&2
  echo "FAILURE TO RUN: no results file was produced (${RESULTS} missing/empty)." >&2
  echo "Unity exit code: ${UNITY_EXIT} (1=process/lock/license/compile error, 3=run error/timeout)." >&2
  echo "--- last 40 lines of ${LOG} ---" >&2
  tail -n 40 "${LOG}" >&2 2>/dev/null || echo "(no log)" >&2
  printf '{"status":"failed-to-run","source":"headless","unityExit":%s}\n' "${UNITY_EXIT}" > "${SUMMARY}"
  exit 2
fi

TOTAL="$(xmllint --xpath 'string(/test-run/@total)'         "${RESULTS}" 2>/dev/null)"
PASSED="$(xmllint --xpath 'string(/test-run/@passed)'       "${RESULTS}" 2>/dev/null)"
FAILED="$(xmllint --xpath 'string(/test-run/@failed)'       "${RESULTS}" 2>/dev/null)"
SKIPPED="$(xmllint --xpath 'string(/test-run/@skipped)'     "${RESULTS}" 2>/dev/null)"
INCONC="$(xmllint --xpath 'string(/test-run/@inconclusive)' "${RESULTS}" 2>/dev/null)"
RESULT="$(xmllint --xpath 'string(/test-run/@result)'       "${RESULTS}" 2>/dev/null)"
GREEN="$(xmllint --xpath 'boolean(/test-run[@result="Passed" and @failed="0" and @inconclusive="0"])' "${RESULTS}" 2>/dev/null)"

printf '{"status":"completed","source":"headless","overall":"%s","total":%s,"passed":%s,"failed":%s,"skipped":%s,"inconclusive":%s,"green":%s,"unityExit":%s}\n' \
  "${RESULT:-Unknown}" "${TOTAL:-0}" "${PASSED:-0}" "${FAILED:-0}" "${SKIPPED:-0}" "${INCONC:-0}" "${GREEN:-false}" "${UNITY_EXIT}" > "${SUMMARY}"

echo ""
echo "================ EditMode test results (headless) ================"
echo "  result=${RESULT}  total=${TOTAL}  passed=${PASSED}  failed=${FAILED}  skipped=${SKIPPED}  inconclusive=${INCONC}"

if [ "${GREEN:-false}" != "true" ]; then
  echo "  Failing / inconclusive tests:"
  # Space-safe extraction (does NOT split on spaces inside test names):
  xmllint --xpath '//test-case[@result="Failed" or @result="Inconclusive"]' "${RESULTS}" 2>/dev/null \
    | grep -o 'fullname="[^"]*"' | sed 's/fullname="//;s/"$//' | sed 's/^/    - /' || true
fi
echo "================================================================="

# --- Final verdict (XML source of truth, exit code as corroboration) -------
if [ "${GREEN:-false}" = "true" ] && [ "${UNITY_EXIT}" -eq 0 ]; then
  echo "GREEN ✅  all ${TOTAL} tests passed."
  exit 0
else
  echo "NOT GREEN ❌  (unity exit=${UNITY_EXIT})."
  exit 1
fi
