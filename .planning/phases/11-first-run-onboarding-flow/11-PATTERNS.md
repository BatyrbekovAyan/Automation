# Phase 11: First-Run Onboarding Flow - Pattern Map

**Mapped:** 2026-07-17
**Files analyzed:** 20 (11 new source + 4 new tests + 4 modified + prefab/scene targets)
**Analogs found:** 19 / 20 (1 partial — the horizontal pager has only a base-class analog)

This phase is pure client UI woven into five existing seams. Almost every new file has a strong in-repo analog. The dominant patterns are: (1) idempotent `[MenuItem]` editor builders cloned from `NavRestructureBuilder.cs`; (2) pure static logic classes tested from `Assets/Tests/Editor/Chat/`; (3) thin event-driven MonoBehaviours cloned from `DashboardPage.cs` / `BotStatusPill.cs`. Follow the analog verbatim — do not invent new idioms.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Assets/Editor/OnboardingScreenBuilder.cs` | editor builder | batch / scene-construction | `Assets/Editor/NavRestructureBuilder.cs` | exact |
| `Assets/Editor/OnboardingAuthBlocksBuilder.cs` | editor builder | batch / scene-construction | `NavRestructureBuilder.BuildOverlayChrome` (injects children into an existing panel) | role-match |
| `Assets/Editor/FirstStepsCardBuilder.cs` | editor builder | batch / scene-construction | `Assets/Editor/DashboardPageBuilder.cs` + `NavRestructureBuilder.BuildEmptyState` | exact |
| `Assets/Scripts/Main/Onboarding/OnboardingGate.cs` | utility (pure) | transform (bool over facts) | `ChatRowSwipePolicy` (via `ChatRowSwipePolicyTests`) | exact |
| `Assets/Scripts/Main/Onboarding/OnboardingKeys.cs` | config (const strings) | — | `Bot.UnauthedProfileSentinel` / `BottomTabManager` consts | role-match |
| `Assets/Scripts/Main/Onboarding/OnboardingPageMath.cs` | utility (pure) | transform (index math) | `Assets/Scripts/Main/ServerPageMath.cs` / `ScrollTargetMath` | exact |
| `Assets/Scripts/Main/Onboarding/OnboardingPager.cs` | hook (MonoBehaviour) | event-driven (drag → snap → page event) | `Assets/Scripts/Main/SnappyFlickScrollRect.cs` (base ScrollRect subclass only) | partial |
| `Assets/Scripts/Main/Onboarding/OnboardingScreen.cs` | controller (MonoBehaviour) | request-response (CTA → nav) | `Assets/Scripts/Main/BotsPage.cs` (thin nav controller) | role-match |
| `Assets/Scripts/Main/Onboarding/SuccessCtaSelector.cs` | utility (pure) | transform (facts → enum) | `ChatRowSwipePolicy` | exact |
| `Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs` | utility (pure) | transform (facts → step states + label + latch) | `Assets/Scripts/Main/Dashboard/DashboardMetrics.cs` (pure derivation) | role-match |
| `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs` | component (MonoBehaviour) | event-driven (render + deep-links) | `Assets/Scripts/Main/Dashboard/DashboardPage.cs` + `BotStatusPill.cs` | exact |
| `Assets/Tests/Editor/Chat/OnboardingGateTests.cs` | test | — | `Assets/Tests/Editor/Chat/ChatRowSwipePolicyTests.cs` | exact |
| `Assets/Tests/Editor/Chat/OnboardingPageMathTests.cs` | test | — | `Assets/Tests/Editor/Chat/ScrollTargetMathTests.cs` | exact |
| `Assets/Tests/Editor/Chat/SuccessCtaSelectorTests.cs` | test | — | `ChatRowSwipePolicyTests.cs` | exact |
| `Assets/Tests/Editor/Chat/FirstStepsChecklistTests.cs` | test | — | `ScrollTargetMathTests.cs` / `ChatRowSwipePolicyTests.cs` | exact |
| `Assets/Scripts/Main/BotsPage.cs` (MODIFY) | controller | request-response | itself (`RefreshEmptyState`, lines 37-42) | self |
| `Assets/Scripts/Main/Manager.cs` (MODIFY) | controller | request-response + coroutine | itself (`LoadBots`/`CreateBotFromForm`/`ShowAuthSuccess`) | self |
| `Assets/Editor/NavRestructureBuilder.cs` (MODIFY) | editor builder | batch | itself (`ReorderScreens`, lines 449-473) | self |
| `Assets/Scripts/Main/Bot.cs` (MODIFY) | model/MonoBehaviour | — | itself (`OpenSettings`, lines 100-161) | self |

---

## Pattern Assignments

### `Assets/Editor/OnboardingScreenBuilder.cs` (editor builder, scene-construction)

**Analog:** `Assets/Editor/NavRestructureBuilder.cs` — clone its structure wholesale (this is the exact pattern the CONTEXT/spec/RESEARCH all point to).

**File envelope + entry points** (`NavRestructureBuilder.cs:1-9, 64-84`):
```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class OnboardingScreenBuilder
{
    [MenuItem("Tools/Onboarding/Build")]
    public static void Build() { /* BuildInternal(); MarkSceneDirty; Debug.Log("... SAVE THE SCENE"); */ }

    // Headless entry — the sentinel string is the build-runner's success contract:
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[OnboardingScreenBuilder] Headless build + save complete");
    }
}
#endif
```
**CRITICAL sentinel:** `Tools/run-editor-builder.sh` derives the success sentinel as `"[<Class>] Headless build + save complete"` from the class name (`run-editor-builder.sh` line deriving `SENTINEL`). The final `Debug.Log` MUST end with exactly `Headless build + save complete` or the runner reports NOT GREEN even after the scene is saved.

**Font GUID constants — copy verbatim** (`NavRestructureBuilder.cs:50-52`):
```csharp
const string RegularGuid  = "e0cdfe2d6a51446bcba7d2df147e2415";
const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
const string BoldGuid     = "1cd715823fef34be4a3d3f3c5572594c";
```
Default TMP font's weight table is empty — always assign a font explicitly via `LoadFont(guid)` (`NavRestructureBuilder.cs:535-541`).

**Design tokens — match the spec's deck** (`NavRestructureBuilder.cs:36-52`; spec §Screen specs):
```csharp
Ink="#1A1A2E"  Muted="#65676B"  Primary="#1B7CEB"  Card=white
Divider="#E4E6EB"  HeaderHeight=300  CardRadius=40  ButtonHeight=144
// Trust block (spec): bg ≈ #F2F8F2, border ≈ #DCEDDD ; H1 50-55, body 38-40, caption 28-32
```

**Reusable helpers — copy verbatim** (`NavRestructureBuilder.cs:552-659`): `Hex`, `NewChild`, `SetAnchors`, `StretchFill`, `SetPreferredSize`, `AddText` (sets `textWrappingMode=Normal`, `raycastTarget=false` — line 593-606), `AddIconImage` (Image+sprite, `preserveAspect`, `raycastTarget=false` — line 608-615), `AddRounded`/`RefreshRounded` (deferred RoundedCorners bake via `_roundedToRefresh` list + `Canvas.ForceUpdateCanvases()` before refresh — lines 617-638), `DestroyAllByName` (idempotent teardown — 640-648), `FindDeepChild`, `LoadFont`, `LoadSprite`, `EnsureIconImportSettings`.

**Deferred RoundedCorners bake** (the non-obvious quirk — `NavRestructureBuilder.cs:214-217, 617-638`):
```csharp
// AddRounded queues the component; radius bake needs SIZED rects, so refresh
// AFTER the whole tree is built:
Canvas.ForceUpdateCanvases();
foreach (var rounded in _roundedToRefresh) RefreshRounded(rounded);
```

**Screen insertion + ordering:** Build `Screen_Onboarding` as a child of the same `ScreenContainer` (`screenBots.transform.parent`, resolved as in `NavRestructureBuilder.cs:174-181`), start it `SetActive(false)`, then call the (newly-`internal`) `NavRestructureBuilder.ReorderScreens(container)` after adding `"Screen_Onboarding"` to its `order[]` — see the NavRestructureBuilder modify section below. Insert AFTER `Screen_New`, BEFORE `WhatsappAuth`/`TelegramAuth` (auth stays LAST — anti-pattern: never runtime `SetAsLastSibling`).

**Idempotency:** `DestroyAllByName(container, "Screen_Onboarding")` first, then rebuild (mirrors `BuildDashboard`, `NavRestructureBuilder.cs:223-260`).

---

### `Assets/Editor/OnboardingAuthBlocksBuilder.cs` (editor builder, injects children into existing panels)

**Analog:** `NavRestructureBuilder.BuildOverlayChrome` (`NavRestructureBuilder.cs:264-337`) — the pattern for idempotently injecting children into an EXISTING hand-built panel (there it injects into `Screen_New`; here into the auth code/success panels).

**Idempotent teardown-then-append** (`NavRestructureBuilder.cs:266-269`):
```csharp
DestroyAllByName(codePanel.transform, "TrustBlock");   // remove our own node first
// ... rebuild TrustBlock as the LAST child
```

**⚠ Pitfall 2 — HARDCODED sibling indices (must preserve).** `Manager.ShowWhatsappAuth` addresses code-panel children by index:
```csharp
// Manager.cs:1664-1668
WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);
WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
// Telegram mirror — Manager.cs:1707
TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
```
The trust card MUST be appended as a NEW LAST child (index ≥ 6 for WhatsApp, ≥ 4 for Telegram) so these `GetChild(n)` indices don't shift — OR the plan must update those constants in the same task. The builder appending last (via `DestroyAllByName` + fresh `NewChild`) is the safe path.

**Trust-card content:** green-tinted rounded card (`AddRounded`, bg `#F2F8F2`, border `#DCEDDD`), lock icon as `Image + sprite` (NEVER a TMP glyph — anti-pattern), title «Это безопасно» + channel-specific body (spec copy deck, verbatim). Success-CTA button uses `AddRounded` + `AddText` like the empty-state CTA (`NavRestructureBuilder.cs:384-395`).

**Serialized-field stamping** for the new success-panel buttons/labels into `Manager` uses the `SerializedObject` idiom below (Shared Pattern: Field Stamping).

---

### `Assets/Editor/FirstStepsCardBuilder.cs` (editor builder, card + MonoBehaviour stamping)

**Analog:** `Assets/Editor/DashboardPageBuilder.cs` (builds a rich card into a screen and stamps a MonoBehaviour's many `[SerializeField]` refs) combined with `NavRestructureBuilder.BuildEmptyState` (`NavRestructureBuilder.cs:341-412`) which builds a card under `BotsPage` and stamps its fields.

**Locate host + stamp fields** (`NavRestructureBuilder.cs:343-409`):
```csharp
var botsPage = Object.FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
DestroyAllByName(botsPage.transform, "FirstStepsCard");
var cardGo = NewChild(botsPage.gameObject, "FirstStepsCard", out var cardRt);
// ... build title + «N из 4» + progress bar + 4 rows (each: check/empty circle Image, label, chevron Image)
var card = cardGo.AddComponent<FirstStepsCard>();
var so = new SerializedObject(card);
so.FindProperty("<fieldName>").objectReferenceValue = <ref>;   // field names = the builder↔component contract
so.ApplyModifiedPropertiesWithoutUndo();
```
Position the card above the bots list (sibling ordering under `BotsPage`, using the located `BotsParent` via `FindDeepChild(botsPage.transform, "BotsParent")` — `NavRestructureBuilder.cs:400`). Cascade + row templates follow the `DashboardPage` row-template convention (an inactive template child cloned per row).

---

### `Assets/Scripts/Main/Onboarding/OnboardingGate.cs` (pure utility)

**Analog:** `ChatRowSwipePolicy` (seen through `ChatRowSwipePolicyTests.cs`) — a `static` class in the **runtime assembly, global namespace** exposing a single pure predicate.

**Shape:**
```csharp
public static class OnboardingGate
{
    // First run: no bots AND flag unset ⇒ show carousel. Any bot present ⇒ never.
    public static bool ShouldShowCarousel(bool hasBots, bool seen) => !hasBots && !seen;

    // Existing-user auto-flag rule (used at end of Manager.LoadBots):
    public static bool ShouldAutoFlagSeen(bool hasBots, bool seen) => hasBots && !seen;
}
```
No `using UnityEngine` needed if it stays booleans-only (matches `ChatRowSwipePolicy`). The MonoBehaviour supplies facts (`botsParent.childCount > 0`, `PlayerPrefs.GetInt("OnboardingSeen",0)==1`) and acts on the verdict.

---

### `Assets/Scripts/Main/Onboarding/OnboardingPageMath.cs` (pure utility)

**Analog:** `Assets/Scripts/Main/ServerPageMath.cs` (pure index arithmetic, XML-doc'd, unit-tested) and `ScrollTargetMath` (normalized-position clamp).

**Shape (mirror ServerPageMath's doc + Math.Clamp discipline):**
```csharp
using System;
public static class OnboardingPageMath
{
    /// <summary>Nearest 0-based page index for a horizontal normalized scroll X in [0,1].</summary>
    public static int NearestPage(float normalizedX, int pageCount)
    {
        if (pageCount <= 1) return 0;
        int idx = (int)Math.Round(Math.Clamp(normalizedX, 0f, 1f) * (pageCount - 1));
        return Math.Clamp(idx, 0, pageCount - 1);
    }

    /// <summary>Normalized X target that lands page `index` under the viewport.</summary>
    public static float PageToNormalizedX(int index, int pageCount) =>
        pageCount <= 1 ? 0f : Math.Clamp(index, 0, pageCount - 1) / (float)(pageCount - 1);
}
```

---

### `Assets/Scripts/Main/Onboarding/OnboardingPager.cs` (MonoBehaviour, gesture → snap → page-changed)

**Analog (PARTIAL):** `Assets/Scripts/Main/SnappyFlickScrollRect.cs` — the ONLY existing `ScrollRect` subclass, but it does **vertical flick momentum only, no paging** (Pitfall 1). Reuse its override structure (`OnBeginDrag`/`OnEndDrag` capturing drag distance/time) but replace the momentum math with page-snap.

**Reusable override skeleton** (`SnappyFlickScrollRect.cs:29-62`):
```csharp
public override void OnEndDrag(PointerEventData eventData)
{
    base.OnEndDrag(eventData);
    // Instead of flick math: compute nearest page + DOTween to it.
    int page = OnboardingPageMath.NearestPage(horizontalNormalizedPosition, _pageCount);
    float targetX = OnboardingPageMath.PageToNormalizedX(page, _pageCount);
    DOTween.To(() => horizontalNormalizedPosition, x => horizontalNormalizedPosition = x,
               targetX, 0.3f).SetEase(Ease.OutCubic);   // 0.3s OutCubic per CONTEXT
    if (page != _currentPage) { _currentPage = page; OnPageChanged?.Invoke(page); }  // drives dots
}
```
Configure the ScrollRect: `horizontal=true, vertical=false, movementType=Clamped, inertia=false`. DOTween usage matches `DashboardPage.MovePeriodHighlight`/`OpenStatusList` (`DashboardPage.cs:261, 314`). Dots: active dot = elongated Primary pill (spec), toggled in the `OnPageChanged` handler.

---

### `Assets/Scripts/Main/Onboarding/OnboardingScreen.cs` (thin controller)

**Analog:** `Assets/Scripts/Main/BotsPage.cs` — a small controller that wires a CTA to a singleton nav call (`StartNewBot` → `AddBotPanel.Instance?.Open()`, `BotsPage.cs:49-55`).

**Slide-3 CTA handler (the gate hand-off from CONTEXT/spec):**
```csharp
public void OnCreateBotTapped()
{
    PlayerPrefs.SetInt("OnboardingSeen", 1);
    PlayerPrefs.Save();
    gameObject.SetActive(false);                  // hide Screen_Onboarding
    BotsPage.Instance?.StartNewBot();             // existing path → AddBotPanel.Open()
}
```
`AddBotPanel.Instance.Open()` is idempotent (RESEARCH §Don't Hand-Roll). Button wiring in `Start()` mirrors `BotsPage.Start` (`BotsPage.cs:17-22`).

---

### `Assets/Scripts/Main/Onboarding/SuccessCtaSelector.cs` (pure utility)

**Analog:** `ChatRowSwipePolicy` (pure enum/bool selector).

**Shape:**
```csharp
public enum SuccessCta { UploadPriceList, OpenChats }
public static class SuccessCtaSelector
{
    // Files already exist (settings re-auth case) ⇒ «Открыть чаты»; else «Загрузить прайс-лист».
    public static SuccessCta Choose(bool hasUploadedFiles) =>
        hasUploadedFiles ? SuccessCta.OpenChats : SuccessCta.UploadPriceList;
}
```
Fact source (RESEARCH §Checklist fact — uploaded files):
```csharp
bool hasFiles = UploadedFilesStore.Load(bot.name, "product").Count > 0
             || UploadedFilesStore.Load(bot.name, "service").Count > 0;
```

---

### `Assets/Scripts/Main/Onboarding/FirstStepsChecklist.cs` (pure derivation)

**Analog:** `Assets/Scripts/Main/Dashboard/DashboardMetrics.cs` (pure fact→result derivation, no MonoBehaviour) + `ChatRowSwipePolicy` shape.

**Shape — take facts as primitives, return step states + label + completion:**
```csharp
public static class FirstStepsChecklist
{
    // Channel label from the bot's actual channel (CONTEXT: Telegram parity).
    public static string ChannelLabel(bool isOnWhatsapp, bool isOnTelegram) =>
        isOnWhatsapp ? "WhatsApp" : "Telegram";   // default 1/1; WhatsApp wins the dual case

    public static bool[] StepStates(bool botExists, bool channelAuthed,
                                    bool hasFiles, bool firstReplySeen)
        => new[] { botExists, channelAuthed, hasFiles, firstReplySeen };

    public static bool AllDone(bool[] steps) => System.Array.TrueForAll(steps, s => s);
}
```
Facts (all verified, RESEARCH §Code Examples):
- `botExists` ← `BotsParent.childCount > 0`
- `channelAuthed` ← `bot.whatsappProfileId != Bot.UnauthedProfileSentinel` (or telegram) — never inline-compare to `""`/`"-1"` (RESEARCH §Don't Hand-Roll)
- `hasFiles` ← `UploadedFilesStore.Load(...)` (both content types)
- `firstReplySeen` ← global latch `PlayerPrefs.GetInt("FirstBotReplySeen",0)==1` (Pitfall 5: `isIncoming==false` is a pragmatic proxy)
- channel label ← `PlayerPrefs.GetInt(bot.name+"isOnWhatsapp",1)`/`isOnTelegram`
- Latch `OnboardingChecklistDone` only at 4/4 (never store per-step — anti-pattern).

---

### `Assets/Scripts/Main/Onboarding/FirstStepsCard.cs` (component MonoBehaviour, event-driven render)

**Analog:** `Assets/Scripts/Main/Dashboard/DashboardPage.cs` (renders derived rows, wires per-row deep-links, subscribes to `ChatManager` events, DOTween) + `BotStatusPill.cs` (thin card with `Hex`/palette constants).

**Refresh trigger** — `OnEnable` + relevant events, mirroring `DashboardPage.OnEnable` (`DashboardPage.cs:64-77`):
```csharp
private void OnEnable() { Refresh(); }   // BotsPage.OnEnable path re-shows this
// First-reply latch subscription (RESEARCH §Checklist fact — first bot reply):
ChatManager.Instance.OnBatchMessagesLoaded += (msgs, _, _) => LatchIfReplySeen(msgs);
ChatManager.Instance.OnLiveMessagesReceived += LatchIfReplySeen;
void LatchIfReplySeen(List<MessageViewModel> msgs) {
    if (msgs != null && msgs.Exists(m => !m.isIncoming))
        PlayerPrefs.SetInt("FirstBotReplySeen", 1);
}
```
**Per-row deep-links** (mirror `DashboardPage.BindRow`/`OpenChat`, `DashboardPage.cs:402-433`): row 1 → `BotsPage.Instance.StartNewBot()`; row 2 → open the bot's settings General tab (connect toggle); row 3 → `Bot.OpenSettings` then `Manager.openBotSettings.OpenProductTab()` (needs `OpenSettings` exposed — Bot.cs modify below); row 4 → `FindFirstObjectByType<BottomTabManager>().SwitchTab(BottomTabManager.WhatsAppTabIndex)`.

**Cascade** (0.05s stagger, spec): DOTween `.SetDelay(index * 0.05f)` per row (ui-scripts rule). Struck-through completed rows.

**Palette/`Hex` helper** — copy `BotStatusPill.cs:82-86` `Hex` + `static readonly Color` constant block idiom.

**Permanent hide:** at 4/4, `PlayerPrefs.SetInt("OnboardingChecklistDone",1)`; `Refresh()` returns early / `gameObject.SetActive(false)` when that latch is set (never resurrect — spec).

---

### Tests: `OnboardingGateTests.cs`, `OnboardingPageMathTests.cs`, `SuccessCtaSelectorTests.cs`, `FirstStepsChecklistTests.cs`

**Analog:** `Assets/Tests/Editor/Chat/ChatRowSwipePolicyTests.cs` and `ScrollTargetMathTests.cs`. NO asmdef (compile into `Assembly-CSharp-Editor`), plain NUnit, one test class per pure class, message-bearing asserts.

```csharp
using NUnit.Framework;
public class OnboardingGateTests
{
    [Test] public void FirstRun_NoBots_FlagUnset_ShowsCarousel() =>
        Assert.IsTrue(OnboardingGate.ShouldShowCarousel(hasBots:false, seen:false));
    [Test] public void ExistingUser_BotsPresent_NeverShows() =>
        Assert.IsFalse(OnboardingGate.ShouldShowCarousel(hasBots:true, seen:false));
}
```
Math tests follow the expression-bodied `Assert.AreEqual(expected, Fn(...), tolerance)` form of `ScrollTargetMathTests.cs:5-20`. Run via `Tools/run-tests-headless.sh` (Editor closed) or the `Temp/claude/run-tests.trigger` bridge (Editor open).

---

### `Assets/Scripts/Main/BotsPage.cs` (MODIFY — gate insertion)

**The one chokepoint** (`BotsPage.cs:37-42`, verified — every zero-bot auto-open routes here):
```csharp
public void RefreshEmptyState() {
    bool hasBots = botsParent != null && botsParent.childCount > 0;
    if (emptyState != null) emptyState.SetActive(!hasBots);
    if (!hasBots) StartNewBot();   // ← REPLACE:
    // if (!hasBots) {
    //     bool seen = PlayerPrefs.GetInt("OnboardingSeen", 0) == 1;
    //     if (OnboardingGate.ShouldShowCarousel(hasBots, seen)) ShowOnboarding();
    //     else StartNewBot();
    // }
}
```
`ShowOnboarding()` = activate `Screen_Onboarding` (resolve reference the same way Manager holds screen refs; or a serialized field stamped by the builder). Open Question 3: whether explicit Chats-CTA taps also gate — decide at planning.

---

### `Assets/Scripts/Main/Manager.cs` (MODIFY — 3 edits)

**Edit A — existing-user auto-flag at end of `LoadBots()`** (`Manager.cs:426`, right after the instantiation loop, before/after the orphan sweep at 428-440). Use the LIVE post-load bot count `BotsParent.transform.childCount`, NOT the loop bound `id` — `id` is a MONOTONIC bot-creation counter that is never decremented, so a user who created then fully deleted their bots would be wrongly auto-flagged and permanently lose the carousel despite having ZERO bots (violates ONB-01):
```csharp
// After the bot-instantiation loop; the LIVE child count under the bots container is the
// authoritative "has bots" fact (bots are instantiated under BotsParent.transform, line 359).
bool hasBotsNow = BotsParent != null && BotsParent.transform.childCount > 0;
if (OnboardingGate.ShouldAutoFlagSeen(hasBots: hasBotsNow,
        seen: PlayerPrefs.GetInt(OnboardingKeys.Seen, 0) == 1))
{
    PlayerPrefs.SetInt(OnboardingKeys.Seen, 1);
    PlayerPrefs.Save();
}
```

**Edit B + C — interactive success moment (Pitfall 3), owned by Plan 04.** The current `ShowAuthSuccess` coroutine (`Manager.cs:1610-1651`) shows the panel for a fixed `WaitForSeconds(2f)` then hides it, for `selectedPlatform==3` runs TWICE and BEFORE the bot card exists (bot is created at Step 3, `Manager.cs:1366`), AND unconditionally `authPage.SetActive(false)` at line 1650 — which would hide the success panel that is NESTED inside `WhatsappAuth`/`TelegramAuth`. Plan 04 re-sequences this into a single interactive «Бот подключён!» moment:

- **Per-channel success fields (BLOCKER).** `WhatsappAuthSuccessPanel` and `TelegramAuthSuccessPanel` are SEPARATE GameObjects in separate hierarchies, so a single shared TMP label/Button cannot child both. `Manager` declares TWO parallel `[SerializeField] private` field sets — `waSuccessTitleLabel/waSuccessBodyLabel/waSuccessPrimaryButton/waSuccessPrimaryLabel/waSuccessLaterButton` and the `tgSuccess…` mirror — stamped by Plan 05's `OnboardingAuthBlocksBuilder`. A new coroutine `ShowInteractiveSuccessMoment(Bot bot, bool useTelegram)` selects the channel's panel/host/field set by `useTelegram`.
- **authPage reactivation + deferred deactivation.** Because the success panel is nested inside `authPage`, the moment does `authPage.SetActive(true)` before showing the panel, and defers `authPage.SetActive(false)` to dismissal (`CloseSuccessAndOverlay`). `ShowAuthSuccess`'s trailing `authPage.SetActive(false)` (line 1650) AND its transient `WaitForSeconds(2f)` check are gated to run ONLY under `moreAuthSteps` (the intermediate WhatsApp-of-both transient step); final creating-bot auth and settings re-auth leave `authPage` active for the moment and never flash the 2s panel.
- **Fired once, after the bot exists, from exactly two `Manager.cs` sites.** Creation: `CreateBotFromForm` fires `StartCoroutine(ShowInteractiveSuccessMoment(newBotComp, useTelegram))` AFTER Step 6 (bot card exists; `useTelegram = selectedPlatform == 2 || 3`). Settings re-auth: `ShowAuthSuccess`'s non-`moreAuthSteps` else branch fires it gated on `!isCreatingBot` (`Manager.openBot`, `useTelegram = authPage == TelegramAuth`), so the creation flow never double-fires from there. `ShowInteractiveSuccessMoment` is a PRIVATE `Manager` member — never invoked from `BotSettings.Auth.cs` (`OnWhatsappAuthFromSettingsDone` lives in class `BotSettings` and cannot reach a private `Manager` method).
- **CTA target.** «Загрузить прайс-лист» → `bot.OpenSettingsAtProductTab()` (the Product tab hosts «Прайс-листы», BotSettings.cs:405); files-exist fallback «Открыть чаты» via `SuccessCtaSelector.Choose(hasFiles)` → `SwitchTab(WhatsAppTabIndex)`. Replaces the fixed 2s auto-dismiss with wait-for-user-input.

Keep `GetChild` indices intact in `ShowWhatsappAuth`/`ShowTelegramAuth` (Pitfall 2).

---

### `Assets/Editor/NavRestructureBuilder.cs` (MODIFY — teach ReorderScreens)

**Pitfall 4:** `ReorderScreens` is `private static` and only reachable via `BuildInternal`, which THROWS on the restructured scene (identity guard, lines 163-170). Change signature to `internal static` (so `OnboardingScreenBuilder` can call it) and add the new screen to the `order[]` array (`NavRestructureBuilder.cs:449-461`):
```csharp
internal static void ReorderScreens(Transform container)   // was: private static
{
    string[] order = {
        "Screen_Whatsapp", "Screen_Bots", "Screen_Profile", "Screen_Dashboard",
        "Screen_New",
        "Screen_Onboarding",     // ← INSERT after Screen_New, BEFORE auth pages
        "WhatsappAuth", "TelegramAuth",   // auth MUST stay LAST
    };
    foreach (string name in order) { var c = container.Find(name); if (c != null) c.SetAsLastSibling(); }
}
```
Do NOT re-run `NavRestructureBuilder.Build()` — it throws on the restructured scene (anti-pattern). `OnboardingScreenBuilder.BuildInternal` calls `ReorderScreens(container)` directly.

---

### `Assets/Scripts/Main/Bot.cs` (MODIFY — expose settings-at-tab entries)

**Open Question 1:** `OpenSettings` is `private` (`Bot.cs:100`) and only wired to the card Edit button (`Bot.cs:95`). The success CTA + checklist rows need to open a SPECIFIC bot's settings at a specific tab — the Product tab (success CTA «Загрузить прайс-лист» + checklist row 3 «Прайс-листы») AND the General tab (checklist row 2 «Подключить {channel}», where the connect toggles live). Minimal additive change — keep `OpenSettings` private, add TWO public entries that reuse it:
```csharp
public void OpenSettingsAtProductTab()
{
    OpenSettings();
    if (Manager.openBotSettings != null) Manager.openBotSettings.OpenProductTab();  // BotSettings.cs:405 hosts «Прайс-листы»
}

public void OpenSettingsAtGeneralTab()
{
    OpenSettings();
    if (Manager.openBotSettings != null) Manager.openBotSettings.OpenGeneralTab();   // BotSettings.cs:403 hosts the connect toggles
}
```
`OpenSettings` already sets `Manager.openBot`/`openBotSettings`, matches the paired `BotSettings` by sibling index, and calls `RefreshUploadedFiles` (`Bot.cs:100-160`). No behavior change to the Edit-button path.

---

## Shared Patterns

### SerializedObject field stamping (builder → MonoBehaviour refs)
**Source:** `NavRestructureBuilder.StampManagerFields` (`NavRestructureBuilder.cs:477-492`) and `BuildEmptyState` stamping `BotsPage` (`NavRestructureBuilder.cs:405-409`).
**Apply to:** All three new builders (stamp `FirstStepsCard`, `OnboardingScreen`/`OnboardingPager`, and the new success-panel button/label refs on `Manager`).
```csharp
var so = new SerializedObject(component);
so.FindProperty("privateFieldName").objectReferenceValue = reference;
so.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(component);
```
Never use public fields — the `[SerializeField] private` field name IS the builder↔component contract (editor-scripts rule; unity-general naming). New MonoBehaviour refs must be `[SerializeField] private` (BotStatusPill.cs:18-20 shows the shape).

### Idempotent teardown
**Source:** `DestroyAllByName` (`NavRestructureBuilder.cs:640-648`).
**Apply to:** Every builder — call it on your own nodes before rebuilding so re-runs are safe (`BuildDashboard`/`BuildOverlayChrome`/`BuildEmptyState` all lead with it).

### Pure-logic + thin-MonoBehaviour split
**Source:** `ChatRowSwipePolicy` (+ `ChatRowSwipePolicyTests`), `ServerPageMath`, `DashboardMetrics`.
**Apply to:** Gate, page-math, success-CTA, checklist derivation. Logic in a `static` class (runtime assembly, GLOBAL namespace); the MonoBehaviour supplies facts and acts on the verdict. This is what makes the EditMode tests possible without a MonoBehaviour harness.

### DOTween UI motion
**Source:** `DashboardPage` (`DashboardPage.cs:261, 314, 320`), `Bot.EnableBot` (`Bot.cs:258-260`), ui-scripts rule.
**Apply to:** Pager snap (0.3s OutCubic — CONTEXT), success check `DOScale 0.9→1 OutBack`, checklist cascade `.SetDelay(index*0.05f)`. Never Animator for UI.

### Headless builder run + immediate commit
**Source:** `Tools/run-editor-builder.sh` (sentinel = `"[<Class>] Headless build + save complete"`), CLAUDE.md parallel-scene-clobber rule.
**Apply to:** After each builder run, commit `Main.unity` (and any new prefab) immediately — parallel sessions saving the scene clobber uncommitted component adds. Editor must be CLOSED for headless (single-instance lock); else Tools-menu build + manual save + immediate commit.

### PlayerPrefs global flags (outside bot namespace)
**Source:** existing non-bot keys `"ids"`, `"Locale"`, `"WhatsappCooldownFinishTime"` (Manager.cs:1672); bot-persistence SKILL.
**Apply to:** `OnboardingSeen`, `OnboardingChecklistDone`, `FirstBotReplySeen` — plain top-level keys, verified collision-free (RESEARCH §Runtime State Inventory). Optionally centralize in `OnboardingKeys.cs` as `const string` (analog: `Bot.UnauthedProfileSentinel`, Bot.cs:67).

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `Assets/Scripts/Main/Onboarding/OnboardingPager.cs` | hook (MonoBehaviour) | event-driven horizontal paging | Only a base-class analog exists. `SnappyFlickScrollRect` is a VERTICAL flick-momentum `ScrollRect` subclass with no page-snap, no dots, no page-changed event (Pitfall 1). Reuse its `OnBeginDrag`/`OnEndDrag` override skeleton, but the snap/dots/`OnPageChanged` logic is net-new. Pull the nearest-page math into `OnboardingPageMath` (which DOES have an analog: `ServerPageMath`). This is the phase's single genuinely-new UI mechanism. |

`OnboardingKeys.cs` is only a partial-match (const-string holder) but that's trivial and well-precedented (`Bot.UnauthedProfileSentinel`) — not a real gap.

---

## Metadata

**Analog search scope:** `Assets/Editor/` (54 builders), `Assets/Scripts/Main/` (+ `Dashboard/`, `BotSettings/` subfolders), `Assets/Scripts/UI/`, `Assets/Tests/Editor/Chat/` (120 test files), `Tools/`.
**Files scanned/read at source:** `NavRestructureBuilder.cs`, `BotsPage.cs`, `Bot.cs`, `SnappyFlickScrollRect.cs`, `Dashboard/DashboardPage.cs`, `BotStatusPill.cs`, `Manager.cs` (LoadBots 400-441 / CreateBotFromForm 1318-1474 / ShowAuthSuccess+ShowWhatsappAuth+ShowTelegramAuth 1590-1736), `ServerPageMath.cs`, `ChatRowSwipePolicyTests.cs`, `ScrollTargetMathTests.cs`, `Tools/run-editor-builder.sh`. Plus RESEARCH.md verified file:line index.
**Pattern extraction date:** 2026-07-17
