---
phase: 05-channel-aware-chatmanager-core
reviewed: 2026-07-15T06:18:54Z
depth: standard
files_reviewed: 6
files_reviewed_list:
  - Assets/Scripts/Chat/WappiStatusParser.cs
  - Assets/Tests/Editor/Chat/WappiStatusParserTests.cs
  - Assets/Scripts/Main/Manager.cs
  - Assets/Scripts/Main/BotSettings.Auth.cs
  - Assets/Editor/ChannelSwitcherBuilder.cs
  - Assets/Scenes/Main.unity
findings:
  critical: 0
  warning: 1
  info: 3
  total: 4
status: fixes_applied
fixes:
  applied_at: 2026-07-15
  fixed: 2        # WR-01 (c79fcf2), IN-01 (69ec217); IN-02 + IN-03 confirmed no-action
  tests: 1029     # 1028 baseline + 1 new (TryGetPhone lone "+")
---

# Phase 05-09: Code Review Report

**Reviewed:** 2026-07-15T06:18:54Z
**Depth:** standard
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Fix A replaces the fragile hand-rolled substring scan of the Wappi/tapi `get/status`
body with a JObject-based `WappiStatusParser` (`TryGetAuthorized` / `TryGetPhone` /
`IsPlausiblePhone`), wired into three Telegram (tapi) call sites plus a display
sanitizer for the `{bot}TelegramNumber` field. Fix B is a two-constant Editor-builder
tweak (label 28→22pt + 12px horizontal inset) with a full scene rebuild.

**The parser itself is correct and robust.** All six of the auth-critical checks pass:

1. **`TryGetAuthorized` semantics match the old truth-table** at all three sites —
   `true`→(true,true), `false`→(true,false), missing/malformed/empty/JSON-`null`→(false,false).
   The success-nav (`ShowAuthSuccess`/`telegramAuthCompleted`, Manager.cs:2601) and the
   settings re-auth `else` (BotSettings.Auth.cs:293-296) both gate on `... && isAuthorized`,
   preserving intent. The old code additionally **threw** on the real pretty-printed body
   (its `,"authorized_at":` guard never matches `,\n  "authorized_at":`, so `Substring` got
   a negative length) — so the new parser is strictly safer everywhere.
2. **`TryGetPhone` prefers top-level `phone`** over `account.phone` (verified against
   `Tools/tapi/samples/status.json` and the redacted dual-phone test fixture); the single
   leading-`+` strip leaves a plain number untouched; null/empty/missing → false, field
   not overwritten.
3. **`IsPlausiblePhone` never blanks a legit value** — 11-digit and `+`-prefixed numbers
   pass; the stale JSON blob, letters, punctuation, >20 chars, and lone `+` all fail. It is
   applied symmetrically on BOTH the field load (Manager.cs:396, 815) and the `EnableSave`
   dirty-check (Manager.cs:877), so the blob-heal cannot spuriously flip dirty/clean.
   The **WhatsApp** number field is confirmed NOT sanitized (Manager.cs:395, 814, 1396 raw).
4. **WhatsApp path is byte-identical** — the `api/sync` status parses (BotSettings.Auth.cs:111,
   197; Manager.cs:1961, 2081) still use the old substring scan and were not touched.
5. **Builder nav logic is byte-identical** — the diff is exactly the two intended constants
   (`LabelSize` 28→22, `offsetMin/Max` 0→±12); `RestructureNav()` (ChannelSwitcherBuilder.cs:241,
   called at :90) is outside the diff and unchanged.
6. **Parser never throws** — `JObject.Parse` is wrapped in `try/catch` (returns null → false);
   array/scalar roots, nested arrays, and `account` being a non-object are all handled.

One WARNING and three INFO items below — none block, but WR-01 is a genuine behavior
activation (a previously-dead destructive server call is now live) that warrants a device-UAT gate.

## Warnings

### WR-01: Telegram de-auth branch went from dead (threw) to live — destructive `profile/delete` on every settings-open probe, ungated by `isOnTelegram`

**File:** `Assets/Scripts/Main/BotSettings.Auth.cs:363-376`
**Issue:**
`CheckTelegramUnauthorizationOutsideApp()` fires from `BotSettings.OnEnable` (BotSettings.cs:365)
on **every** settings-panel open, with no `isOnTelegram == 1` guard — only the internal
`telegramProfileId != "-1"` check. On the real pretty-printed tapi body the OLD substring
parse **threw** before this branch could act, so de-auth detection was effectively dead. The
new `TryGetAuthorized(response, out isAuthorized) && !isAuthorized && ...` correctly implements
the intended semantics — but that means a previously-dormant destructive path is now live:
whenever tapi returns `authorized:false` for a bot that still has a real profile id, the app
clears the number, sets `isOnTelegram=0`, and calls `GetDeleteTelegramProfile(...)`.

This is the intended fix and mirrors the already-shipping WhatsApp twin
(`CheckWhatsappUnauthorizationOutsideApp`, which works because `api/sync` returns compact JSON),
so it is not a defect. The residual risk is a **transient or mid-pairing `authorized:false`**:
a bot whose Telegram profile exists (`id != "-1"`) but is not yet/again authorized — e.g. a
reconnecting profile, or an abandoned-pairing profile whose id was never reset to `"-1"` — would
have its profile deleted the next time settings OnEnable fires. Per CLAUDE.md this is "safe by
design" (re-auth recreates the profile), and the auth screen normally overlays settings during
pairing, so the window is likely unreachable — but this branch has never actually executed before,
so it has no field history.

**Fix:** Gate the newly-live branch on the bot's known-authed flag so a `false` only counts as
a *de*-auth when the bot was previously authed (defense-in-depth; matches the WhatsApp precedent):
```csharp
if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized)
    && !isAuthorized
    && PlayerPrefs.GetInt(Manager.openBot.name + "isOnTelegram", 0) == 1
    && !Manager.openBot.GetComponent<Bot>().telegramProfileId.Equals("-1"))
```
At minimum, confirm during device UAT that opening a healthy authorized bot's settings never
returns a transient `authorized:false` (which would silently delete a live Telegram bot's profile).

**Status:** FIXED (c79fcf2) — added the `PlayerPrefs.GetInt(Manager.openBot.name + "isOnTelegram", 0) == 1`
gate to the branch condition, so the destructive `GetDeleteTelegramProfile` only fires for a bot the user
actually has on Telegram. A code comment documents that this gate is DELIBERATELY stricter than the
byte-identical WhatsApp twin (`CheckWhatsappUnauthorizationOutsideApp`) because this Telegram branch only
just went live and has no field history. The WhatsApp twin was left byte-identical (not gated). A Phase-8
device-UAT line was appended to 05-HUMAN-UAT.md (a healthy authorized Telegram bot must NOT trip
outside-app de-auth on settings open).

## Info

### IN-01: `TryGetPhone` returns `true` with an empty phone for a lone `"+"` value (contract says false-when-no-value)

**File:** `Assets/Scripts/Chat/WappiStatusParser.cs:69-71`
**Issue:** A `phone` value of exactly `"+"` passes the `IsNullOrEmpty(raw)` guards, then
`raw.StartsWith("+") ? raw.Substring(1)` yields `""`, and the method returns `true` with
`phone=""`. The XML doc promises `False (phone="")` when no key holds a value. At the call
sites this only writes an empty string into the field (harmless — the field then hides), and
tapi never emits `"+"`, so impact is nil. Noted only because the review asked to scrutinize the
`+` strip / empty handling. `WappiStatusParserTests` covers `IsPlausiblePhone("+")` but not this
`TryGetPhone("+")` path or `authorized:null`.
**Fix:** Re-validate after the strip so the contract holds:
```csharp
phone = raw.StartsWith("+") ? raw.Substring(1) : raw;
if (string.IsNullOrEmpty(phone)) { phone = ""; return false; }
return true;
```
Optionally add `TryGetPhone("{\"phone\":\"+\"}")` and `TryGetAuthorized("{\"authorized\":null}")` tests.

**Status:** FIXED (69ec217) — after stripping the leading `+`, `TryGetPhone` now re-validates the
remainder: a lone `"+"` strips to `""` and returns `false` (phone=""), honoring the false-when-no-value
contract. Added `TryGetPhone_LonePlus_ReturnsFalseAndEmpty` (`{"phone":"+"}` → false, phone=""); the
headless EditMode suite is GREEN at 1029 (1028 baseline + 1). The optional `authorized:null` test was
left out of scope (only the "+" case was requested).

### IN-02: `git diff --stat` cannot confirm the Main.unity change is "scoped to the switcher" (16,072 lines = full-scene reserialization)

**File:** `Assets/Scenes/Main.unity`
**Issue:** The scene rebuild (commit e4f6451) produced a 16,072-line diff — a full-scene YAML
reserialization, not a switcher-only delta. Per project memory ("Main.unity save churn",
"Parallel scene clobber") this large churn is benign (layout zeroing + material regen), and the
YAML was intentionally not reviewed. But `--stat` alone therefore does **not** positively confirm
scope — a full rewrite can silently clobber an uncommitted scene component add from a parallel
session.
**Fix:** Confirm the intended switcher payload landed and nothing unrelated changed by grepping
the diff for the switcher chip's `fileID`/GUID and the new label metrics (`m_fontSize: 22`, the
±12 label offsets) rather than trusting the line count. Since the scene mutation is already
committed here (not left uncommitted), clobber risk is contained — no action needed if the
switcher payload verifies.

**Status:** CONFIRMED — no code action. The executor verified the Main.unity change is switcher-scoped
by grepping the diff for the switcher chip's fileID/GUID and the new label metrics (2× `m_fontSize: 22`,
2× the ±12 / -24 sizeDelta label insets) rather than trusting the 16,072-line `--stat`. The scene
mutation is already committed (e4f6451), so parallel-session clobber risk is contained. Payload verified;
nothing unrelated changed.

### IN-03: WhatsApp `api/sync` status still uses the fragile substring parser (untouched — correct for this phase, but a tracked parity risk)

**File:** `Assets/Scripts/Main/BotSettings.Auth.cs:111, 197`; `Assets/Scripts/Main/Manager.cs:1961, 2081`
**Issue:** The WhatsApp status parses were correctly left byte-identical (requirement 4). They work
only because `api/sync` currently returns compact JSON; if Wappi ever pretty-prints the WhatsApp
`get/status` body (as tapi already does), these four sites would break and throw exactly like the
Telegram bug this phase fixed. The parser's own doc comment already flags adopting `WappiStatusParser`
for WhatsApp as a safe follow-up.
**Fix:** No change this phase. Track migrating the four WhatsApp sites onto `WappiStatusParser`
(the parser is endpoint-agnostic — both channels share the shape) as a future hardening pass.

**Status:** CONFIRMED — no action this phase (correct). The four WhatsApp `api/sync` status sites were
left byte-identical per requirement 4; they are safe today because `api/sync` returns compact JSON.
Migrating them onto `WappiStatusParser` is already flagged as a safe follow-up in the parser's own doc
comment — tracked, not actioned here.

---

_Reviewed: 2026-07-15T06:18:54Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
