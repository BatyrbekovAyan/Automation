---
phase: 11
slug: first-run-onboarding-flow
status: partial
fix_scope: targeted (WR-02 then WR-03, owner-directed)
findings_in_scope: 2
fixed: 2
skipped: 0
iteration: 2
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

## Not in scope this run

| Finding | Severity | Status |
|---|---|---|
| WR-01 — `FirstStepsCard` event subscription one-shot / silently dead | warning | **Already resolved** during the phase (commit `26ab638`): the first-reply latch moved to the event SOURCE (`OnboardingFirstReplyLatch`, installed in `ChatManager.Awake`, never unsubscribed), which removes the subscription-lifecycle failure mode entirely rather than patching it. Stale in `11-REVIEW.md`. |
| WR-03 — fragile `IndexOf`/`Substring` status parsing can throw mid-coroutine | warning | **Fixed** — see above (`28f3a84`). |
| 8 × Info findings | info | **Open.** Includes the orphaned bare `"BotN"` activation key on `Bot.DeleteBot`, success-overlay body copy vs the «Открыть чаты» CTA, settings-back not stopping `_whatsappQrCoroutine`, checklist cascade replaying on every refresh, and the inert `LayoutElement` on the carousel dots. Run with `--all` to include. |

### Follow-ups surfaced by the WR-03 verification (new — not in `11-REVIEW.md`)

The adversarial pass enumerated the *other* hand-rolled Wappi/tapi body scrapes. None are
`get/status` parsing (so none are WR-03), but they share the defect class and are recorded here
so the migration isn't mistaken for complete:

| Site | Risk | Note |
|---|---|---|
| `Manager.cs` `GetWhatsappCode` — pairing-code read via a fixed `Substring(startIndex, 9)` | **medium** | Throws `ArgumentOutOfRangeException` if fewer than 9 chars follow the token (short/empty code). The throw strands the LoadingPanel **and** leaves `GetWhatsappCodeButton` disabled — the exact soft-lock WR-03 exists to eliminate. Same code-panel flow. Best next candidate. |
| `Manager.cs` `CreateWhatsappProfile` / `CreateTelegramProfile` — `"profile_id":` +14 up to `,"status":` | low | Negative length (hangs the parent coroutine) if the server ever emits `status` before `profile_id`; the `+14` also hard-codes the compact form, so a pretty body would store an id with a leading quote and silently break every later call for that bot. |
| ~8 further `Contains`-guarded scrapes (error `detail` extraction, QR base64, a fixed `Substring(start, 4)`) | low | Mostly error/degraded paths. The `detail` sites could route through the existing bounds-checked `TelegramAuthResponseParser.ExtractDetail`; the two QR sites should also wrap `Convert.FromBase64String` in try/catch. |

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
