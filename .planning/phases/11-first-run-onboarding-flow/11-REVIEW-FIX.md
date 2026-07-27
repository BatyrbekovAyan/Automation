---
phase: 11
slug: first-run-onboarding-flow
status: partial
fix_scope: targeted (WR-02, owner-directed)
findings_in_scope: 1
fixed: 1
skipped: 0
iteration: 1
source_review: 11-REVIEW.md
created: 2026-07-27
---

# Phase 11 — Code Review Fix Report

Owner-directed scope: fix **WR-02** only (`/gsd-code-review-fix 11` invoked for
"that WR-02 auth cancel race"). The other findings from `11-REVIEW.md` are
dispositioned at the bottom — WR-01 was already resolved during the phase; WR-03
and the 8 Info items remain open and available.

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

## Not in scope this run

| Finding | Severity | Status |
|---|---|---|
| WR-01 — `FirstStepsCard` event subscription one-shot / silently dead | warning | **Already resolved** during the phase (commit `26ab638`): the first-reply latch moved to the event SOURCE (`OnboardingFirstReplyLatch`, installed in `ChatManager.Awake`, never unsubscribed), which removes the subscription-lifecycle failure mode entirely rather than patching it. Stale in `11-REVIEW.md`. |
| WR-03 — fragile `IndexOf`/`Substring` status parsing can throw mid-coroutine | warning | **Open.** Unguarded substring parses in `BotSettings.Auth.cs` (~104-131) and `Manager.cs` (~2249-2276) can throw a negative-length `Substring` and strand the `LoadingPanel` / kill the auth poll — the same failure class that previously broke the Telegram twin. Recommended fix: route through the existing `WappiStatusParser`. Run `/gsd-code-review-fix 11` again (default scope) to address. |
| 8 × Info findings | info | **Open.** Includes the orphaned bare `"BotN"` activation key on `Bot.DeleteBot`, success-overlay body copy vs the «Открыть чаты» CTA, settings-back not stopping `_whatsappQrCoroutine`, checklist cascade replaying on every refresh, and the inert `LayoutElement` on the carousel dots. Run with `--all` to include. |

---

*Phase: 11-first-run-onboarding-flow — targeted fix pass, WR-02 only (owner-directed).*
