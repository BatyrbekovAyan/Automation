---
phase: 05-channel-aware-chatmanager-core
plan: 09
subsystem: ui
tags: [telegram, tapi, wappi, get-status, json-parse, newtonsoft, channel-switcher, device-uat]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: tapi get/status auth flows (GetTelegramProfileStatus, CheckTelegramAuthorization); ChannelSwitcherBuilder switcher pill (06-02)
provides:
  - WappiStatusParser pure seam (JObject) — TryGetAuthorized / TryGetPhone / IsPlausiblePhone; throw-safe
  - Telegram get/status auth + phone extraction robust to the pretty-printed dual-phone tapi body
  - Self-heal of the stale JSON-blob {bot}TelegramNumber pref (no re-auth needed)
  - Channel-switcher chip labels padded (22pt + 12px inset) so long words clear the chip edges
affects: [Phase 8 device UAT, WhatsApp status parse (safe future adopter of WappiStatusParser)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure JObject status reader (order/whitespace-agnostic, never throws) replaces hand-rolled substring scans"
    - "Stored-value self-heal: implausible persisted phone collapses to '' at the field load + dirty-check sites"

key-files:
  created:
    - Assets/Scripts/Chat/WappiStatusParser.cs
    - Assets/Tests/Editor/Chat/WappiStatusParserTests.cs
  modified:
    - Assets/Scripts/Main/Manager.cs
    - Assets/Scripts/Main/BotSettings.Auth.cs
    - Assets/Editor/ChannelSwitcherBuilder.cs
    - Assets/Scenes/Main.unity

key-decisions:
  - "Parse tapi get/status via a JObject seam, not substring — the pretty body has TWO phone keys (account.phone + top-level phone) and the no-whitespace guard never matched"
  - "Prefer the top-level phone over account.phone; strip only a leading '+'"
  - "IsPlausiblePhone gates stored values so the legacy JSON blob self-heals to '' without re-auth"
  - "Fixed a third Telegram tapi status site (CheckTelegramUnauthorizationOutsideApp) that THREW on the pretty response — same fragile parse, Telegram-only, throw-safe now [Rule 1]"
  - "WhatsApp api/sync status parses left byte-identical; switcher chip/track widths + RestructureNav untouched"

patterns-established:
  - "Wappi status bodies are read through WappiStatusParser, not substring index math"

requirements-completed: []

# Metrics
duration: ~22min
completed: 2026-07-15
---

# Phase 5 Plan 9: tapi Status Parse Hardening + Switcher Label Padding Summary

**Two device-UAT fixes from owner Editor screenshots: a JObject WappiStatusParser that ends the raw-JSON-blob in the Telegram number field (pretty tapi get/status has two "phone" keys), plus a 22pt/12px-inset pass on the channel-switcher chip labels.**

## Performance

- **Duration:** ~22 min
- **Completed:** 2026-07-15T06:07:43Z
- **Fixes:** 2 (Fix A parse hardening + self-heal, Fix B label padding)
- **Files:** 6 (2 created, 4 modified)
- **Tests:** 1028/1028 EditMode green (1007 baseline + 21 new parser tests)

## Accomplishments

- **Fix A — robust tapi status parse.** New pure `WappiStatusParser` (Newtonsoft `JObject`, null/throw-safe): `TryGetAuthorized`, `TryGetPhone` (top-level `phone` wins over `account.phone`, leading `+` stripped), `IsPlausiblePhone`. Wired into `Manager.GetTelegramProfileStatus` and `BotSettings.CheckTelegramAuthorization`, replacing the fragile substring scans that broke on the pretty-printed dual-phone tapi body (root cause of the owner's raw-JSON-blob screenshot).
- **Fix A — self-heal.** The stale blob persisted in `{bot}TelegramNumber` is now dropped at the two field-load sites (`Manager` L396 recreate, L815 `CloseSettings`) and the `EnableSave` dirty-check (L869) via `IsPlausiblePhone` → the field hides without re-auth; the real number repopulates on the next tapi status check.
- **Fix B — switcher label padding.** `ChannelSwitcherBuilder.LabelSize` 28→22 and a 12px horizontal inset on the chip label rect (`offsetMin (12,0)` / `offsetMax (-12,0)`), so "WhatsApp"/"Telegram" clear the 162px chip edges. Rebuilt Main.unity headlessly; grep-verified the payload.

## Task Commits

1. **Fix A: parser + wiring + self-heal + throw-fix** — `584be1d` (fix)
2. **Fix B: switcher chip-label constants** — `d68534f` (fix)
3. **Scene rebuild (Main.unity, padded labels)** — `e4f6451` (fix)

**Plan metadata:** this SUMMARY + STATE.md + UAT note (docs commit).

## Files Created/Modified

- `Assets/Scripts/Chat/WappiStatusParser.cs` — new pure JObject status reader (3 methods, throw-safe)
- `Assets/Tests/Editor/Chat/WappiStatusParserTests.cs` — 21 tests (pretty dual-phone, compact, authorized false, malformed/empty/null, IsPlausiblePhone matrix; synthetic redacted digits)
- `Assets/Scripts/Main/Manager.cs` — `GetTelegramProfileStatus` uses the parser; `PlausibleTelegramNumber` helper; 3 Telegram number sites sanitized
- `Assets/Scripts/Main/BotSettings.Auth.cs` — `CheckTelegramAuthorization` uses the parser; `CheckTelegramUnauthorizationOutsideApp` throw-fix
- `Assets/Editor/ChannelSwitcherBuilder.cs` — `LabelSize` 22 + 12px label inset
- `Assets/Scenes/Main.unity` — switcher pill rebuilt with padded labels

## Scene Verification (grep evidence)

- `m_text: WhatsApp` + `m_fontSize: 22` and `m_text: Telegram` + `m_fontSize: 22` — the ONLY two 22pt labels in the scene (other WhatsApp/Telegram texts are 42/50pt).
- `m_SizeDelta: {x: -24, y: 0}` appears exactly twice (the two label RectTransforms = `offsetMin (12,0)`/`offsetMax (-12,0)` with a centre pivot), both added by this rebuild — nothing else in the scene uses it.
- Nav restructure was a guarded no-op (scene already on the 4-tab layout).

## Decisions Made

- **JObject over substring** — key order and whitespace are irrelevant; the pretty tapi body's two `phone` keys + a no-whitespace `","platform":` guard were the exact failure. Prefer top-level `phone`; strip only a leading `+`.
- **Self-heal via `IsPlausiblePhone`** — rather than a migration, an implausible stored value (letters/JSON punctuation/length > 20) reads as empty, so bot "53" stops showing the blob immediately and the correct number repopulates on the next status check.
- **WhatsApp left byte-identical** — the `api/sync` status parses render correctly today; they could adopt `WappiStatusParser` later as a safe follow-up (noted, not done this pass).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed a throw on the pretty response in `CheckTelegramUnauthorizationOutsideApp`**
- **Found during:** Fix A (grep found a THIRD tapi `get/status` site beyond the two named)
- **Issue:** `BotSettings.Auth.cs` `CheckTelegramUnauthorizationOutsideApp` used the identical fragile `authorized` substring parse. On the pretty tapi body the `,"authorized_at":` guard never matches (`,\n  "authorized_at":`), so `endIndex = -1` and `Substring(startIndex, negative)` throws — silently breaking outside-app de-auth detection (the coroutine aborts).
- **Fix:** Replaced with `WappiStatusParser.TryGetAuthorized`, preserving the exact act-only-when-not-authorized-and-real-id semantics; now throw-safe. Telegram-only site, so WhatsApp stays byte-identical.
- **Files modified:** Assets/Scripts/Main/BotSettings.Auth.cs
- **Verification:** Compiles + 1028/1028 EditMode green; behaviour for the only real inputs (authorized true/false booleans) is unchanged, plus it no longer throws on the pretty body.
- **Committed in:** 584be1d (part of the Fix A commit)

**2. [Rule 1 - Bug] Sanitized the Telegram-number dirty-check (L869), not just the two load sites**
- **Found during:** Fix A step 4 (self-heal wiring)
- **Issue:** Sanitizing only the two field-load sites (L396/L815) would make the field read `""` while the raw blob still sat in PlayerPrefs, so `EnableSave`'s compare (`field "" != stored blob`) would light the Save button spuriously whenever bot "53" settings opened — a regression introduced by the load-sanitize.
- **Fix:** Passed the stored value at the dirty-check (L869) through the same `PlausibleTelegramNumber` helper, so the compare stays consistent (only affects the blob case; real phones and empty are unchanged).
- **Files modified:** Assets/Scripts/Main/Manager.cs
- **Verification:** Real-phone and empty cases compare equal as before; blob case now compares equal (`"" == ""`). 1028/1028 green.
- **Committed in:** 584be1d (part of the Fix A commit)

---

**Total deviations:** 2 auto-fixed (2 bugs). **Impact on plan:** Both are in-scope Telegram-only fixes tied directly to the parse hardening; WhatsApp remains byte-identical and switcher geometry/nav was untouched. No scope creep beyond the tapi status subsystem.

## Issues Encountered

None — both Unity cold runs (headless tests, then the headless builder) were green on the first attempt; no lockfile/Editor contention (no real Unity process was running).

## User Setup Required

None.

## Next Phase Readiness

- Both fixes are code-complete and headless-verified; pixel-perfect look of the switcher and the correct Telegram number in the field are **owner device-reverify in Phase 8** (noted in `05-HUMAN-UAT.md`).
- Safe follow-up available (not done): the WhatsApp `api/sync` status parses could adopt `WappiStatusParser` for the same robustness.

## Self-Check: PASSED

- Created files present: `WappiStatusParser.cs` (+.meta), `WappiStatusParserTests.cs` (+.meta), `05-09-SUMMARY.md`.
- Commits present: `584be1d` (Fix A), `d68534f` (Fix B code), `e4f6451` (scene rebuild).
- Suite: 1028/1028 EditMode green (1007 baseline + 21 new parser tests). Scene payload grep-verified (2× fontSize 22 labels, 2× -24 inset).

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-15*
