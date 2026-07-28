---
phase: 05-channel-aware-chatmanager-core
plan: 05
subsystem: auth
tags: [unity, csharp, wappi, telegram, tapi, 2fa, cloud-password, auth, tdd]

# Dependency graph
requires:
  - phase: 04-n8n-telegram-template-parity
    provides: Telegram workflows on tapi (server side) — this fix lets 2FA-protected accounts authorize
provides:
  - "TelegramAuthResponseParser — pure detail-string classifier (ExtractDetail / IsTwoFactor / IsAuthSuccess); fail-closed, never throws"
  - "Manager 2FA cloud-password step: detail:\"2fa\" in BOTH the code and QR flows switches the existing code panel into password mode and POSTs tapi/sync/auth/2fa {pwd_code}"
  - "_telegram2faMode gate that relaxes TelegramCodeInputChanged validation (any non-empty enables submit)"
affects: [05-06, phase-6-channel-switcher, phase-8-device-uat]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure static classification seam (TelegramAuthResponseParser) — unit-testable, no I/O, no logging (WhatsAppSyncGate / CrossChatResponseGuard precedent)"
    - "Fail-closed auth state machine: only detail startswith auth_success authorizes; every other/malformed detail re-prompts"
    - "Panel reuse by code (no new scene objects): the numeric code-entry input is repurposed as the cloud-password field via a mode flag"

key-files:
  created:
    - Assets/Scripts/Main/TelegramAuthResponseParser.cs
    - Assets/Tests/Editor/Chat/TelegramAuthResponseParserTests.cs
  modified:
    - Assets/Scripts/Main/Manager.cs
    - CLAUDE.md

key-decisions:
  - "Same-button dispatch: SendTelegramCode early-routes to SubmitTelegram2fa when _telegram2faMode, so the existing SendTelegramCodeButton wiring is reused (no listener swap)."
  - "QR 2FA check runs BEFORE the base64 branch — both branches match {detail,uuid}, so ordering prevents decoding \"2fa\" into a broken texture."
  - "Only touched English status strings were RU-ified (Authorizing../Authorization Complete/Failed in the success path + new 2FA strings); the non-Success error branch and sibling GetTelegramCode were left untouched per D5 (no file sweep)."
  - "No input masking added — the threat model lists the exact mitigations (no-log/no-persist/clear-after-submit) and does not include masking; changing TMP contentType risks the known iOS responder-view-type quirk, so it was deliberately skipped."

patterns-established:
  - "Pattern 1: tapi auth response classification lives in a pure seam with EditMode coverage; Manager branches call the seam instead of fragile inline substring parsing."
  - "Pattern 2: a secret entered into a reused UI field is cleared immediately after the request on every path and never logged/persisted."

requirements-completed: [TGAUTH-01]

# Metrics
duration: 26min
completed: 2026-07-12
---

# Phase 5 Plan 05: Telegram 2FA Auth Fix Summary

**2FA-protected Telegram accounts can now authorize: a `detail:"2fa"` response in both the code and QR flows switches the existing code panel into a cloud-password prompt that POSTs `tapi/sync/auth/2fa {pwd_code}`, classified by a new pure, fail-closed `TelegramAuthResponseParser` — full EditMode suite 839/839 green.**

## Performance

- **Duration:** 26 min
- **Started:** 2026-07-12T15:50:27Z
- **Completed:** 2026-07-12T16:16:04Z
- **Tasks:** 3 (Task 1 TDD: RED → GREEN)
- **Files modified:** 4 (2 created, 2 modified)

## Accomplishments
- `TelegramAuthResponseParser` — pure `ExtractDetail` (tolerant substring parse, `""` on malformed/missing/non-string, never throws) + `IsTwoFactor` (exact `2fa`) + `IsAuthSuccess` (fail-closed `startswith auth_success`), with 12 EditMode cases.
- Code flow: `SendTelegramCode` now classifies the `auth/code` 200 via the parser — `2fa` enters cloud-password mode («Облачный пароль» / «Введите пароль от Telegram») instead of showing "Authorization Failed".
- `SubmitTelegram2fa` coroutine: `POST tapi/sync/auth/2fa?profile_id={id}` with `{pwd_code}` via `UploadHandlerRaw` + `Content-Type: application/json` + `Authorization` + `timeout = 30`; `auth_success` → existing `ShowAuthSuccess` path (via `GetTelegramProfileStatus`), every non-success re-prompts.
- QR flow: `OpenTelegramQRPanel` diverts a `detail:"2fa"` response to the same password mode before the base64 branch, so it no longer paints a broken QR texture.
- `_telegram2faMode` relaxes `TelegramCodeInputChanged` (any non-empty enables submit) and is reset on panel close / number change / fresh auth open.
- `CLAUDE.md` Telegram endpoints list gains `auth/2fa`.

## Task Commits

1. **Task 1: Pure TelegramAuthResponseParser + tests** (TDD)
   - `4122f61` (test) — failing parser tests + RED stub (9/12 fail)
   - `52aa495` (feat) — implement classifier; 12/12 green
2. **Task 2: SendTelegramCode 2FA branch + auth/2fa submit + password-mode panel reuse** — `5ca1a48` (feat); full suite 839/839
3. **Task 3: QR flow 2FA branch + touched-string RU-ification + full suite green** — `03efa9e` (feat); full suite 839/839

**Plan metadata:** _(docs commit — this SUMMARY + CLAUDE.md + STATE/ROADMAP/REQUIREMENTS)_

## Files Created/Modified
- `Assets/Scripts/Main/TelegramAuthResponseParser.cs` (created) — pure tapi-auth `detail` classifier.
- `Assets/Tests/Editor/Chat/TelegramAuthResponseParserTests.cs` (created) — 12 cases (2fa / auth_success / whitespace / no-key / malformed / null-empty / non-string / wrong-password).
- `Assets/Scripts/Main/Manager.cs` (modified) — `_telegram2faMode` field; `SendTelegramCode` 2FA dispatch + parser-based classification; `SetTelegram2faTexts` / `EnterTelegram2faMode` / `ResetTelegram2faMode` / `SubmitTelegram2fa`; `ShowTelegram2faFromQr` + `OpenTelegramQRPanel` divert; 2FA-mode resets in `CloseTelegramCodePanel` / `ChangeTelegramNumber` / `ShowTelegramAuth`; relaxed `TelegramCodeInputChanged`.
- `CLAUDE.md` (modified) — added `auth/2fa` to the Wappi.pro (Telegram) endpoints line.

## Decisions Made
- **Same-button dispatch, not listener swap.** `SendTelegramCode` early-returns into `SubmitTelegram2fa` when `_telegram2faMode`, reusing the existing `SendTelegramCodeButton.onClick` wiring — no runtime re-binding, less state to get wrong.
- **QR 2FA check ordered before the base64 branch.** Both a `2fa` response and a QR-image response contain `{"detail":"…","uuid":…}`, so the `IsTwoFactor` check must precede the base64 decode to avoid a broken texture.
- **RU-ification kept to touched strings.** Per D5 (no file sweep), only the success-path strings I rewrote and the new 2FA strings became Russian; the non-Success error branch and the sibling `GetTelegramCode` were left untouched.
- **No password masking.** The threat model's mitigations (no-log / no-persist / clear-after-submit) are the binding contract; TMP `contentType` masking was skipped to avoid the documented iOS responder-view-type quirk and extra reset-state surface.

## Deviations from Plan

None - plan executed exactly as written. No Rule 1-4 deviations were required; all three tasks landed on-contract.

## Threat Register Coverage
- **T-0505-01 (Information Disclosure, cloud password):** mitigated — password sent ONLY to `tapi/sync/auth/2fa` over HTTPS with the standard `Authorization` header; the `jsonBody`/password is never `Debug.Log`'d and never written to PlayerPrefs or any store; `TelegramCodeInput.text = ""` runs immediately after the request on both success and failure; `_telegram2faMode` is reset on panel close / number change / auth open. Verified: `grep -ic "Debug.Log.*pwd_code"` = 0, `grep -ic "PlayerPrefs.*pwd|password"` = 0.
- **T-0505-02 (Tampering, wrong-password / unknown detail):** mitigated — `IsAuthSuccess` is fail-closed (`startswith auth_success` only); `IsTwoFactor` is exact-match; every other/malformed detail returns both-false → re-prompt (no false success). Covered by `UnknownOrWrongPasswordDetail_IsNeitherTwoFactorNorSuccess`.
- **T-0505-03 (DoS, malformed auth response body):** mitigated — `ExtractDetail` returns `""` (never throws) on malformed/missing/non-string input, so a garbage response can't crash the auth coroutine. Covered by `ExtractDetail_MalformedBody_*` / `_NonStringDetailValue_` / `_NullOrEmpty_`.

## Issues Encountered
None. The scene structure (QR panel + code panel coexist under `TelegramAuth`, both active after `ShowTelegramAuth`) let the QR divert reuse the already-active code panel without any new objects.

## User Setup Required
None - no external service configuration required. Device UAT of the live 2FA round-trip against a real cloud-password Telegram account carries to Phase 8 (Editor is closed; only headless EditMode was run here).

## Next Phase Readiness
- TGAUTH-01 closed on the client; 2FA works in both code and QR flows, WhatsApp/other auth flows untouched.
- Full EditMode suite: **839/839 green** via `Tools/run-tests-headless.sh` (Editor closed) — 827 baseline + 12 new parser tests.
- Wave-1 independent plan; does not touch the ChatManager channel seam (05-02..05-04) — no cross-plan coupling.
- Live 2FA round-trip (real cloud-password account) remains a device-UAT item for Phase 8.

## Self-Check: PASSED

- All 4 source/test files (+ .meta) present on disk plus this SUMMARY.md.
- All 4 task commits (1× RED test + 3× GREEN feat) present in git history.

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-12*
