---
phase: 11-first-run-onboarding-flow
reviewed: 2026-07-23T12:21:53Z
depth: standard
files_reviewed: 24
files_reviewed_list:
  - Assets/Editor/FirstStepsCardBuilder.cs
  - Assets/Editor/NavRestructureBuilder.cs
  - Assets/Editor/OnboardingAuthBlocksBuilder.cs
  - Assets/Editor/OnboardingScreenBuilder.cs
  - Assets/Scripts/Main/Bot.cs
  - Assets/Scripts/Main/BotSettings.Auth.cs
  - Assets/Scripts/Main/BotsPage.cs
  - Assets/Scripts/Main/Manager.cs
  - Assets/Scripts/Main/Onboarding/FirstStepsCard.cs
  - Assets/Scripts/Main/Onboarding/FirstStepsCardVisibility.cs
  - Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs
  - Assets/Scripts/Main/Onboarding/OnboardingGate.cs
  - Assets/Scripts/Main/Onboarding/OnboardingKeys.cs
  - Assets/Scripts/Main/Onboarding/OnboardingPageMath.cs
  - Assets/Scripts/Main/Onboarding/OnboardingPager.cs
  - Assets/Scripts/Main/Onboarding/OnboardingScreen.cs
  - Assets/Scripts/Main/Onboarding/SuccessCheckPop.cs
  - Assets/Scripts/Main/Onboarding/SuccessCtaSelector.cs
  - Assets/Tests/Editor/Chat/FirstStepsCardVisibilityTests.cs
  - Assets/Tests/Editor/Chat/FirstStepsChecklistTests.cs
  - Assets/Tests/Editor/Chat/OnboardingGateTests.cs
  - Assets/Tests/Editor/Chat/OnboardingPageMathTests.cs
  - Assets/Tests/Editor/Chat/SuccessCtaSelectorTests.cs
  - Tools/render_lock_icon.js
findings:
  critical: 0
  warning: 3
  info: 8
  total: 11
status: issues_found
---

# Phase 11: Code Review Report

**Reviewed:** 2026-07-23T12:21:53Z
**Depth:** standard
**Files Reviewed:** 24
**Status:** issues_found

## Summary

Reviewed the first-run onboarding flow: the carousel (`OnboardingScreen` + `OnboardingPager` + `OnboardingPageMath`), the derived-state «Первые шаги» checklist (`FirstStepsCard` + pure `FirstStepsChecklist`/`FirstStepsCardVisibility`), the auth trust blocks and standalone success overlay (`OnboardingAuthBlocksBuilder`, `SuccessCheckPop`, `SuccessCtaSelector`, Manager's `ShowInteractiveSuccessMoment` / `ShowAuthSuccess` rework), the gate wiring in `BotsPage`/`Manager.LoadBots` (`OnboardingGate`/`OnboardingKeys`), the four editor builders, the EditMode tests, and the lock-icon render script.

Overall quality is high: the pure-logic classes are cleanly separated and fully unit-tested; every builder-stamped `[SerializeField]` in the new MonoBehaviours is null-guarded before use (a missing builder run degrades to a no-op, never an NRE); the three global PlayerPrefs keys are centralized in `OnboardingKeys`, verified collision-free with the per-bot namespace, and correctly covered by the «Удалить все данные» wipe; `SuccessCheckPop` and `OnboardingPager` both kill their tweens on disable; and all four builders are idempotent delete-and-rebuild with careful guards (trust cards appended last to protect Manager's hardcoded `GetChild(3/4/5)` indices; the standalone `SuccessOverlay` teardown deliberately scopes to direct canvas children so the nested per-channel panels survive; `NavRestructureBuilder` keeps its pre-restructure identity guard and its `ReorderScreens` now slots `Screen_Onboarding` before the auth pages).

No critical or security issues found. Three warnings concern lifecycle/ordering fragility: a one-shot event subscription that can silently miss `ChatManager.Instance`, a re-opened cancel-race window in the creation wizard that the D2 overlay relocation uncovered, and the surviving fragile substring parse of Wappi status responses whose failure mode is a stuck `LoadingPanel`. The remaining findings are hygiene/polish items.

## Warnings

### WR-01: FirstStepsCard event subscription is one-shot and silently dies if ChatManager.Instance is not yet assigned

**File:** `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs:95-112`
**Issue:** `Subscribe()` runs only from `OnEnable()`, and the card root is deliberately always active (per the class doc, `OnEnable` fires exactly once at scene load and never again — visibility is CanvasGroup-only). `Subscribe()` returns without subscribing when `ChatManager.Instance == null`, and nothing ever retries: `RefreshFromFacts()`/`Refresh()` never call `Subscribe()`. `ChatManager.Instance` is assigned in `ChatManager.Awake` (ChatManager.cs:216), but Unity's per-object Awake/OnEnable ordering across GameObjects at scene load is undefined — if `FirstStepsCard.OnEnable` ever runs before `ChatManager.Awake` (e.g., after a hierarchy reorder or scene re-serialization), the `OnBatchMessagesLoaded`/`OnLiveMessagesReceived` subscriptions are missed for the entire session and the row-4 «Получить первый ответ бота» latch can never fire. It works today only because the current scene serialization happens to order ChatManager first — a silent, scene-order-dependent failure mode.
**Fix:** Make subscription self-healing — retry from the refresh path, which is invoked at every fact-changing moment:

```csharp
public void RefreshFromFacts()
{
    Subscribe();   // idempotent (_subscribed guard); heals a missed OnEnable-time subscribe
    Refresh();
}
```

(or call `Subscribe()` at the top of `Refresh()`).

### WR-02: CreateBotFromForm has no isCreatingBot re-check after auth completes — back-press in the poll gap deletes the profile but still creates the bot

**File:** `Assets/Scripts/Main/Manager.cs:1371-1397` (also 1389-1397 for the Telegram leg)
**Issue:** The auth-wait loops only check `isCreatingBot` *inside* the loop body:

```csharp
while (!whatsappAuthCompleted)
{
    if (!isCreatingBot) yield break;
    yield return new WaitForSeconds(0.5f);
}
// ← no re-check here; falls straight through to Step 3 (Instantiate bot)
```

Once `GetWhatsappProfileStatus` sets `whatsappAuthCompleted = true`, the loop condition exits without ever re-evaluating `isCreatingBot`. In the new D2 flow, the final creating-bot auth branch of `ShowAuthSuccess` deliberately does nothing (Manager.cs:1735-1736) — so unlike the old 2s success panel (which set `cg.interactable = false` and covered this window), the auth page's back button stays interactive for up to ~0.5s between auth completion and the wizard coroutine's next poll tick (plus until `ShowInteractiveSuccessMoment` deactivates the auth pages). A back tap in that window runs `CancelBotCreation` — which sets `isCreatingBot = false` and deletes the just-authorized Wappi profile — yet `CreateBotFromForm` then resumes, exits the loop (condition already satisfied), and proceeds to instantiate and persist a bot card pointing at a deleted (or `"-1"`) profile.
**Fix:** Re-check the flag immediately after each auth-wait loop (and once more before Step 3):

```csharp
while (!whatsappAuthCompleted)
{
    if (!isCreatingBot) yield break;
    yield return new WaitForSeconds(0.5f);
}
if (!isCreatingBot) yield break;   // back pressed in the completion→resume gap
```

Alternatively (belt-and-suspenders), have `GetWhatsappProfileStatus`/`GetTelegramProfileStatus` disable the auth page's back button the moment `authorized == true` is detected.

### WR-03: Fragile substring status parsing can throw mid-coroutine and strand the LoadingPanel / auth poll

**File:** `Assets/Scripts/Main/BotSettings.Auth.cs:104-131` (`CheckWhatsappAuthorization`); `Assets/Scripts/Main/Manager.cs:2249-2276` (`GetWhatsappProfileStatus`)
**Issue:** Both sites compute `lenght = endIndex - startIndex` where `endIndex = response.IndexOf(",\"authorized_at\":")` with no `>= 0` guard — only the presence of `"authorized":` is checked. If Wappi ever returns a success body containing `"authorized":` but not the exact compact `,"authorized_at":` token (the precise failure that already bit the Telegram twin, per the comment at BotSettings.Auth.cs:358-362 — the pretty-printed tapi body made the old parse throw a negative-length `Substring`), the exception kills the coroutine mid-flight. Consequences differ by site and both are user-visible soft-locks: in `CheckWhatsappAuthorization` the throw skips `LoadingPanel.SetActive(false)` at line 133, leaving the full-screen loading overlay stuck on; in `GetWhatsappProfileStatus` the polling coroutine dies silently, so a successful QR/code auth is never detected and the wizard hangs. The project has already built and adopted `WappiStatusParser` for exactly this (used at lines 283 and 372 of the same file).
**Fix:** Route both WhatsApp sites through the existing throw-safe parser (semantics preserved):

```csharp
if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized))
{
    if (isAuthorized) { ... WappiStatusParser.TryGetPhone(response, out var phone) ... }
    else ShowWhatsappAuthFromSettings(bot.whatsappProfileId);
}
```

At minimum, guard `if (startIndex >= 0 && endIndex > startIndex)` before every `Substring` (the pattern `CheckWhatsappAuthorized` at Manager.cs:2131-2138 already uses), and/or wrap the parse so `LoadingPanel.SetActive(false)` always runs.

## Info

### IN-01: Bot.DeleteBot leaves the bare "BotN" activation key orphaned (dual-key activation state)

**File:** `Assets/Scripts/Main/Bot.cs:184-256` (delete), `282` (write), `320` (read)
**Issue:** Activation state lives under two keys: `EnableBot` persists the bare `transform.name` key (`"Bot0"`) and `SetSwitches` reads it, while `"Bot0isOn"` is written only once at creation and read by `LoadBots`. `DeleteBot` removes `"...isOn"` but never the bare key, so it leaks on every per-bot delete (only `PlayerPrefs.DeleteAll()` in the full wipe cleans it). No functional impact today because bot slot names are never reused (the `"ids"` counter is monotonic), but it is exactly the kind of key drift the bot-persistence skill guards against.
**Fix:** Add `PlayerPrefs.DeleteKey(transform.name);` to `DeleteBot`, and consider consolidating on a single activation key.

### IN-02: Success overlay body copy mismatches the CTA in the files-exist case

**File:** `Assets/Scripts/Main/Manager.cs:1768-1771`
**Issue:** `successBodyLabel` is always set to «Осталось научить бота вашим ценам — загрузите прайс-лист…» even when `SuccessCtaSelector.Choose` resolves to `OpenChats` (files already uploaded, settings re-auth) and the primary button reads «Открыть чаты» — the body urges an action the CTA no longer offers.
**Fix:** Branch the body text on `cta`, e.g. for `OpenChats`: «Бот уже знает ваши цены — откройте чаты и посмотрите, как он отвечает».

### IN-03: Wait-for-dismiss coroutine can be stranded if the success moment is ever re-shown over itself

**File:** `Assets/Scripts/Main/Manager.cs:1774-1798`
**Issue:** `ShowInteractiveSuccessMoment` spins `while (!dismissed) yield return null;` on a closure captured by the button listeners. A second invocation calls `RemoveAllListeners()` and rewires the buttons to a new closure — the first coroutine's `dismissed` can then never become true and it yields forever (leaked coroutine). Today this is unreachable (the opaque overlay blocks all input, and both fire sites are gated), so this is defensive hardening, not a live bug. Note also the overlay has no non-button escape (no Android back handling) — acceptable per design, but worth remembering if a hardware-back handler is ever added.
**Fix:** Track the active moment, e.g. store a `Coroutine _successMoment` and `StopCoroutine` it at the top of the method before rewiring, or guard with an `if (SuccessOverlay.activeSelf) yield break;` re-entrancy check.

### IN-04: OnSettingsAuthBackPressed does not stop the WhatsApp QR coroutine

**File:** `Assets/Scripts/Main/Manager.cs:1010-1020` (vs `CancelBotCreation` at 1540-1542)
**Issue:** `CancelBotCreation` stops `_whatsappQrCoroutine`, `_whatsappStatusCoroutine`, and `_telegramStatusCoroutine`; the settings-mode back handler stops only the two status coroutines. `OpenWhatsappQRPanel`'s early-exit guard checks `WhatsappQRPanel.activeSelf`, which stays `true` when only the parent `WhatsappAuth` was deactivated — so after a settings-auth back, the QR loop keeps polling `qr/get` (up to 5 attempts × 3s) against a profile that `OnWhatsappAuthFromSettingsBack` is concurrently deleting. Bounded and harmless downstream (`GetWhatsappProfileStatus` exits on `!WhatsappAuth.activeInHierarchy`), but it is wasted network churn and an inconsistency between the two cancel paths.
**Fix:** Mirror the wizard cancel: add `if (_whatsappQrCoroutine != null) { StopCoroutine(_whatsappQrCoroutine); _whatsappQrCoroutine = null; }` to `OnSettingsAuthBackPressed`.

### IN-05: Checklist row cascade replays on every Refresh; row tweens are not linked to their GameObjects

**File:** `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs:208-217, 251-257`
**Issue:** `RenderRows` calls `PlayCascade` unconditionally, so every `RefreshFromFacts` (each return to the Bots tab, each fact change, and the live first-reply latch while the card is on screen) resets all four rows to alpha 0 and re-fades them — a visible blink when nothing changed. Additionally, the row `DOFade` tweens have no `.SetLink(row.gameObject)` (contrast `SuccessCheckPop`, which links correctly); the card is never destroyed in-session so this is only a teardown-warning risk, but linking is cheap insurance.
**Fix:** Only play the cascade on a hidden→visible transition (track previous visibility) or when a row's state changed; append `.SetLink(row.gameObject)` to the fade tween.

### IN-06: Dead guard in BotsPage.OnEnable

**File:** `Assets/Scripts/Main/BotsPage.cs:32`
**Issue:** `if (isActiveAndEnabled) Invoke(...)` — `isActiveAndEnabled` is always true inside `OnEnable`, so the condition is dead code that reads as if it gates something.
**Fix:** Drop the condition: `Invoke(nameof(RefreshEmptyState), 0f);`.

### IN-07: OnboardingPager.pageCount is invisible in the Inspector

**File:** `Assets/Scripts/Main/Onboarding/OnboardingPager.cs:15`
**Issue:** `ScrollRect` ships a custom editor (`ScrollRectEditor`) that renders only ScrollRect's own properties — subclass fields like `pageCount` do not appear in the Inspector. The builder stamps it via `SerializedObject` (OnboardingScreenBuilder.cs:151-153), so it works, but a future slide-count change cannot be made by hand in the Inspector and the field will silently stay at its serialized value.
**Fix:** Either add a trivial `[CustomEditor(typeof(OnboardingPager))]` that draws the default inspector, or document in the field's comment that the builder is the only assignment path (Debug mode Inspector also works).

### IN-08: Runtime FindFirstObjectByType in tap paths

**File:** `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs:279`; `Assets/Scripts/Main/Manager.cs:1783, 1808`; `Assets/Scripts/Main/BotsPage.cs:77`
**Issue:** `.claude/rules/unity-general.md` says "Don't use Find()/FindObjectOfType() at runtime — cache references". These `FindFirstObjectByType<BottomTabManager>()` calls are all on cold tap paths (checklist row 4, success CTA/dismiss, StartNewBot) and match the pre-existing project idiom, so impact is negligible — noted for consistency only.
**Fix:** Optionally expose a `BottomTabManager.Instance` (the class already follows the singleton pattern elsewhere) or a cached lookup on Manager, and route these calls through it.

---

_Reviewed: 2026-07-23T12:21:53Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
