---
phase: 11
slug: first-run-onboarding-flow
status: all_fixed
fix_scope: full (WR-02, WR-03, all 8 Info, + 2 parser follow-ups; owner-directed)
findings_in_scope: 12
fixed: 12
skipped: 0
iteration: 3
source_review: 11-REVIEW.md
created: 2026-07-27
updated: 2026-07-27
---

# Phase 11 — Code Review Fix Report

Owner-directed scope, run in two passes: **WR-02** then **WR-03**. Remaining findings from `11-REVIEW.md` are dispositioned at the bottom — WR-01 was already resolved during the phase; the 8 Info items remain open.




---

## Fixed

### WR-02 — `CreateBotFromForm` had no `isCreatingBot` re-check after auth completes

**Severity:** warning
**File:** `Assets/Scripts/Main/Manager.cs` (WhatsApp leg ~1399-1408, Telegram leg ~1421-1429)
**Commit:** `c4ea246`

**Defect.** Both auth-wait loops tested the cancel flag only *inside* the loop body:

```csharp
while (!whatsappAuthCompleted)
{
    if (!isCreatingBot) yield break;
    yield return new WaitForSeconds(0.5f);
}
// ← fell straight through to Step 3 (Instantiate bot)
```

Once `GetWhatsappProfileStatus` set `whatsappAuthCompleted = true`, the loop exited
without re-evaluating `isCreatingBot`. The D2 success-moment relocation removed the old
2s success panel that used to cover this window (it set `cg.interactable = false`), so the
auth page's back button stayed live for up to ~0.5s between auth completing and the wizard
coroutine's next tick. A back tap there ran `CancelBotCreation` — which clears the flag and
**deletes the just-authorized Wappi profile** — yet the wizard resumed, exited the loop on
the already-satisfied condition, and instantiated + persisted a bot card pointing at a
deleted (or `"-1"`) profile.

**Fix applied.** The review's primary recommendation: re-check the flag immediately after
each auth-wait loop, so a cancel landing in the completion→resume gap aborts before Step 3.
Applied to BOTH legs (WhatsApp-only, Telegram-only, and the "both" path all funnel through
these two exits). The belt-and-suspenders alternative (disabling the auth back button on
`authorized == true`) was deliberately NOT taken — it would modify the auth page interaction
surface that this phase's security pass certified byte-identical, for no additional coverage.

**Related hardening already in place (commit `a855595`, pre-dating this report):** the
in-box checkmark dwell in `ShowInteractiveSuccessMoment` sets the auth page's CanvasGroup
`interactable = false` for its duration — narrowing the same window from the other side.

**Verification.**
- EditMode suite **1218/1218 green** (in-Editor bridge, fresh recompile).
- Auth flow byte-identical: `GetChild(3/4/5)` count **21** and `auth/code`+`auth/2fa` count
  **7** — both unchanged from the `11-SECURITY.md` certified baseline.
- Diff scope: `Manager.cs` only, +7 lines, no scene mutation.

---

### WR-03 — fragile substring status parsing could throw mid-coroutine

**Severity:** warning
**Commit:** `28f3a84`
**Files:** `Assets/Scripts/Main/BotSettings.Auth.cs`, `Assets/Scripts/Main/Manager.cs`

**Defect.** Hand-rolled scans computed `length = endIndex - startIndex` where
`endIndex = response.IndexOf(",\"authorized_at\":")` with **no `>= 0` guard** — only the
presence of `"authorized":` was checked. Any body lacking that exact *compact* token threw a
negative-length `Substring` and killed the coroutine mid-flight. Not hypothetical: the
pretty-printed tapi body is precisely what broke the Telegram twin and caused
`WappiStatusParser` to be written in the first place.

**Sites fixed — 4, not the 2 the review cited:**

| # | Site | Consequence of the throw | In review? |
|---|---|---|---|
| A | `BotSettings.Auth.cs` `CheckWhatsappAuthorization` | skipped `LoadingPanel.SetActive(false)` → **stranded full-screen overlay** | yes |
| B | `BotSettings.Auth.cs` `CheckWhatsappUnauthorizationOutsideApp` | silent background probe; **destructive** false-path (deletes the Wappi profile, clears the number, `isOnWhatsapp=0`) | **no — same defect, missed by the review** |
| C | `Manager.cs` `GetWhatsappProfileStatus` (polling) | killed the poll → a successful QR/code auth was **never detected**, wizard hung | yes |
| D | `Manager.cs` `CheckWhatsappAuthorized` | *length-guarded — could not throw*; migrated anyway (see below) | cited as the **good** pattern |

**On site D — a deliberate reversal.** It was initially left alone because it cannot throw.
Adversarial verification (two independent lenses, converging) showed *"cannot throw" is the
wrong bar here*: this bool is the pre-delete guard for the resend-code path, whose entire job
per `CLAUDE.md` is *"a pre-delete `get/status` guard so a just-authorized profile is never
destroyed."* Its scan needs the exact compact token, so a pretty-printed or key-reordered body
reads as **not authorized** → `RecreateWhatsappProfileForNewCode` deletes and re-provisions a
profile the user had just successfully paired. A false negative there is irreversible
user-visible harm (forced re-pairing), unlike the recoverable hangs at A/C. Migrated.

**Per-site semantics preserved.** Most important, site B's destructive delete now fires **only**
on a definitively parsed `authorized:false`. Malformed, empty, missing-key, `authorized:null`,
`authorized:0` and nested-only bodies all leave the profile untouched — *strictly stronger* than
the old scan, which deleted on any `"authorized":` slice that merely differed from `true`.

**Three intended deltas (all fail-safer, recorded so a future diff-vs-old audit doesn't re-flag them):**
1. **Present-but-non-boolean `authorized`** (e.g. `null`): now takes *neither* branch; the old
   slice fell into the re-auth branch (A) / deleted the profile (B).
2. **`"authorized":"true"` as a JSON string**: now read as authorized. The old code sliced it
   *with* the quotes, so `.Equals("true")` failed — meaning an authorized bot was pushed to the
   auth screen (A), polled forever (C), or had its profile **deleted** (B). Wappi documents a
   real boolean, so this is latent.
3. **Phone extraction**: the old code required an adjacent `","platform":` token that the
   WhatsApp `get/status` body may not even contain (Wappi's own WA doc sample has no `platform`
   key), so it likely never populated the field. The phone is now read whenever the body carries
   one. The write is **dirty-checked** (`WhatsappNumberField.Value != phone`) so a background
   status probe cannot light the Save button with no user edit. `TryGetPhone` also strips a
   leading `+` — the parser's deliberate contract, already shipped for Telegram.

**Verification.**
- EditMode suite **1218/1218 green**.
- Zero hand-rolled `authorized` status scans remain in either file.
- Auth request code untouched: `auth/code`+`auth/2fa` = **7**, `GetChild(3/4/5)` = **21** — both
  matching the `11-SECURITY.md` certified baseline. Telegram paths not touched (WhatsApp-only change).
- Three-lens adversarial pass; `auth-flow-integrity` returned SOLID, and every CONCERNS item is
  either fixed above or recorded below.

---

### Follow-up — WhatsApp pairing-code read (same defect class as WR-03, not in `11-REVIEW.md`)

**Severity:** medium (surfaced by the WR-03 verification pass, fixed on owner request)
**Commit:** `381c3cf`
**Files:** `Assets/Scripts/Chat/WappiStatusParser.cs`, `Assets/Scripts/Main/Manager.cs`, `Assets/Tests/Editor/Chat/WappiStatusParserTests.cs`

**Defect.** `GetWhatsappCode` displayed the pairing code with a hard-coded
`response.Substring(startIndex, 9)` — assuming the canonical 9-char `XXXX-XXXX` shape behind
nothing but a `Contains("\"code\":\"")` guard. A shorter code, or any truncated body, threw
`ArgumentOutOfRangeException` and killed the coroutine **before** `LoadingPanel.SetActive(false)`,
leaving a stranded full-screen overlay *and* a disabled request button — the same soft-lock WR-03
was raised to eliminate, in the same code-panel flow.

**Fix.** New `WappiStatusParser.TryGetCode` (JObject-based, whitespace/order-agnostic, throw-safe,
consistent with the four WR-03 sites). It reads the code **verbatim at whatever length the server
sends**, so a longer code is no longer silently truncated and a shorter one no longer drags in
trailing JSON. Absent / blank / non-scalar / unparseable ⇒ returns false and the label is left
untouched instead of the coroutine dying.

**Verification.** 6 new EditMode tests — canonical 9-char, **short code (the case that used to
throw)**, longer-than-9, pretty-printed body, missing/blank/whitespace/malformed/null, and
non-scalar. Suite **1232/1232 green**. Auth request code untouched (`auth/code`+`auth/2fa` = 7,
`GetChild(3/4/5)` = 21).

No Telegram twin exists for this — Telegram delivers its code in-app rather than displaying one.

---

### Follow-up — `profile_id` scrape + QR base64 decode (owner-directed)

**Severity:** low–medium · **Commit:** `2cd53a2`
**Files:** `WappiStatusParser.cs`, `Manager.cs`, `WappiStatusParserTests.cs`

**`profile_id` (2 sites — `CreateWhatsappProfile`, `CreateTelegramProfile`).** The
`"profile_id":` **+14** offset scan bounded by `","status":` had two failure modes: if the
server ever emitted `status` **before** `profile_id` the length went negative and threw —
killing a *nested* coroutine, so its awaiting parent (creation wizard / resend-recreate) never
resumed and the LoadingPanel hung **unrecoverably**; and the hard-coded offset assumed the
compact `"profile_id":"` form, so a pretty-printed body would store an id with a **leading
quote**, silently breaking every later Wappi call for that bot with no trace at the point of
corruption. → `WappiStatusParser.TryGetProfileId`.

**QR base64 (2 sites).** The two endpoints differ in both key and shape — WhatsApp `qr/get`
returns a `data:image/png;base64,…` URI under **`qrCode`**, Telegram `auth/qr` returns **raw**
base64 under **`detail`**. Each sliced the payload between two literal tokens (negative length
if ever out of order — and note the WhatsApp bound `","task_id":` does **not** appear in the
documented response shape at all), then called `Convert.FromBase64String` **unguarded on the
success path**, so a malformed or non-base64 payload threw `FormatException` and killed the QR
coroutine with the spinner still on screen. → `WappiStatusParser.TryGetQrPng(json, key, out
byte[] png)` guards **extraction and decode**, strips an optional data-URI prefix, and returns
false for anything unusable. The Telegram `detail:"2fa"` divert that runs *ahead* of this block
is untouched.

**Verification.** 9 new EditMode tests — reversed key order (the negative-length case),
pretty-printed body (the leading-quote case), WA data-URI vs TG raw base64, key-order
independence, and the `detail:"2fa"` / `auth_success` / non-base64 payloads that used to throw.
Suite **1241/1241 green** (+9, exactly the new tests). Auth request code untouched
(`auth/code`+`auth/2fa` = 7, `GetChild(3/4/5)` = 21).

---

### All 8 Info findings (owner-directed)

**Commits:** `0c4eb02` (the eight) + `6539959` (gaps found by the verification pass)

| # | Fix |
|---|---|
| IN-01 | `Bot.DeleteBot` now deletes the bare `"BotN"` activation key. Activation lives under **two** keys (bare name written by `EnableBot`/read by `SetSwitches`; `"{name}isOn"` at creation/read by `LoadBots`) and only the latter was cleaned, so the bare one leaked on every per-bot delete. Safe because slot names are never reused — the `"ids"` counter is monotonic. |
| IN-02 | Success-overlay **body** now branches on the CTA. The files-exist path showed «Открыть чаты» while the body still urged «загрузите прайс-лист» — an action the button no longer offered. |
| IN-03 | Re-entrancy latch on `ShowInteractiveSuccessMoment`. Verification caught that an `activeSelf` test left the ~1.2s in-box-checkmark dwell unguarded (the overlay only activates *after* it), so it is a **field** set when the moment commits and cleared in `CloseSuccessAndOverlay`. |
| IN-04 | `OnSettingsAuthBackPressed` stops the QR loop like `CancelBotCreation` does. Verification found the **Telegram half had the same defect with no stored handle at all** — added `_telegramQrCoroutine`, stopped in both cancel paths. |
| IN-05 | Row cascade plays only on a hidden→visible entrance (it used to reset all rows to alpha 0 and re-fade on *every* refresh — a visible blink); tweens now `SetLink` to their row. Verification found the entrance was being consumed **off-screen** on the primary first-bot path, so `CloseSuccessAndOverlay` now calls `FirstStepsCard.ReplayEntrance()` once the Bots page is actually visible. |
| IN-06 | Dropped the dead `isActiveAndEnabled` guard in `BotsPage.OnEnable` (always true there). |
| IN-07 | New `OnboardingPagerEditor` draws `pageCount`, which `ScrollRect`'s custom editor hides — previously reachable only through the builder's `SerializedObject` stamp, leaving no hand-editable path if the slide count changed. |
| IN-08 | Added `BottomTabManager.Instance` (project singleton idiom) and routed the four scoped tap-path lookups through it, per `.claude/rules/unity-general.md`. Cleared in `OnDestroy` — C#'s `?.` bypasses Unity's null-equality overload, so a stale `Instance` would throw where the old lookup returned null. |

**Copy-deck addition (bookkeeping).** IN-02 introduces a **new** user-facing string not in the
phase's locked copy deck: «Бот уже знает ваши цены — откройте чаты и посмотрите, как он отвечает»
(files-exist body variant). Grammar/register match the deck (formal «вы», same em-dash
construction, no terminal period). One nuance for a future copy pass: `hasFiles` is also true when
only a **service** list was uploaded, where «знает ваши цены» reads slightly product-flavoured.

**Verification.** Three-lens adversarial pass — all three returned **SOLID** (no regressions from
the eight). The five low-severity observations it raised are either fixed in `6539959` (four of
them) or recorded above (the copy-deck entry). Suite **1246/1246 green**; auth request code
untouched (`auth/code`+`auth/2fa` = 7, `GetChild(3/4/5)` = 21).

Two `FindFirstObjectByType<BottomTabManager>` calls remain in `EmptyStateView.cs` and
`DashboardPage.cs` — pre-existing, from earlier phases, outside this review's scope
(`EmptyStateView`'s `FindObjectsInactive.Include` variant also has different semantics).

---

## Not in scope this run

| Finding | Severity | Status |
|---|---|---|
| WR-01 — `FirstStepsCard` event subscription one-shot / silently dead | warning | **Already resolved** during the phase (commit `26ab638`): the first-reply latch moved to the event SOURCE (`OnboardingFirstReplyLatch`, installed in `ChatManager.Awake`, never unsubscribed), which removes the subscription-lifecycle failure mode entirely rather than patching it. Stale in `11-REVIEW.md`. |
| WR-03 — fragile `IndexOf`/`Substring` status parsing can throw mid-coroutine | warning | **Fixed** — see above (`28f3a84`). |
| 8 × Info findings | info | ✅ **ALL FIXED** (`0c4eb02` + `6539959`) — see below. |

### Follow-ups surfaced by the WR-03 verification (new — not in `11-REVIEW.md`)

The adversarial pass enumerated the *other* hand-rolled Wappi/tapi body scrapes. None are
`get/status` parsing (so none are WR-03), but they share the defect class and are recorded here
so the migration isn't mistaken for complete:

| Site | Risk | Note |
|---|---|---|
| ~~`Manager.cs` `GetWhatsappCode` — pairing-code read via a fixed `Substring(startIndex, 9)`~~ | ~~medium~~ | ✅ **FIXED** (`381c3cf`, owner-directed follow-up) — see below. |
| ~~`Manager.cs` `CreateWhatsappProfile` / `CreateTelegramProfile` — `"profile_id":` +14 up to `,"status":`~~ | ~~low~~ | ✅ **FIXED** (`2cd53a2`) — see below. |
| ~~The two QR base64 sites~~ | ~~low~~ | ✅ **FIXED** (`2cd53a2`) — extraction *and* decode guarded; see below. |
| ~~5 remaining `Contains`-guarded error-`detail` scrapes + a fixed `Substring(start, 4)`~~ | ~~low~~ | ✅ **FIXED** (`ea0e248`) — all five detail reads routed through the bounds-checked `TelegramAuthResponseParser.ExtractDetail` (bound-agnostic, so the uuid-vs-status bound mismatch is moot); the status read uses the new `WappiStatusParser.TryGetStatus`. **No hand-rolled Wappi response scrapes remain in `Manager.cs`.** |

Two optional hardenings deliberately **not** taken (recorded, not silently dropped):
- **Site B twin-parity gate.** The Telegram twin carries an extra `isOnTelegram == 1` gate on its
  destructive path; WhatsApp has no equivalent. Not added — site B is already strictly safer than
  before, and adding it would change behaviour beyond the finding.
- **`{bot}WhatsappNumber` self-heal.** The Telegram number is sanitised through
  `IsPlausiblePhone` on load; WhatsApp's is not, so a legacy `+`-prefixed value could sit
  permanently dirty against the new normalised read. Latent only (both documented samples are
  bare digits).

---

*Phase: 11-first-run-onboarding-flow — targeted fix passes: WR-02 + WR-03 (owner-directed).*
