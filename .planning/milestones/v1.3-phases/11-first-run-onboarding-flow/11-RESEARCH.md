# Phase 11: First-Run Onboarding Flow - Research

**Researched:** 2026-07-17
**Domain:** Unity client UI — first-run onboarding woven into the existing bot-creation + auth + settings surfaces (no network, no n8n, no chat-pipeline changes)
**Confidence:** HIGH (codebase archaeology; every integration point read at source)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Telegram parity everywhere**: slide 3 shows both channels; trust block + success moment exist for WhatsApp AND Telegram; checklist channel label = bot's actual channel (`isOnWhatsapp`/`isOnTelegram`).
- **No «Пропустить»**: informative slides advance only with «Далее»/«Создать бота».
- **Price-list upload strictly AFTER authorization**: carousel does not pitch upload; success screen's primary CTA is «Загрузить прайс-лист» → BotSettings «Прайс-листы» tab (fallback «Открыть чаты» when `UploadedFilesStore` already has files — settings re-auth case).
- **No QR**: only the real code flows (WhatsApp pairing code; Telegram phone → code → optional 2FA).
- `PlayerPrefs "OnboardingSeen"` outside the bot key namespace; existing users (bots present) auto-flagged so they never see the carousel; `DeleteAll` wipe re-runs onboarding by design.
- `PlayerPrefs "OnboardingChecklistDone"` latch; step states always derived live from facts, never stored per-step.
- `Screen_Onboarding` built by a new idempotent `[MenuItem]` builder (`OnboardingScreenBuilder`, NavRestructureBuilder pattern); inserted after `Screen_New`, BEFORE the auth screens (auth stays LAST); `NavRestructureBuilder.ReorderScreens` must learn the new screen; scene mutation committed immediately after the builder run.
- Carousel paging = existing `SnappyFlickScrollRect`; transitions 0.3s OutCubic; success check DOScale 0.9→1 OutBack; checklist cascade 0.05s stagger. **(⚠ see Pitfall 1 — SnappyFlickScrollRect does NOT do horizontal paging; this decision needs the planner's attention.)**
- Gate: on first run with no bots and flag unset, show carousel INSTEAD of the AddBotPanel auto-open; carousel CTA sets the flag and calls the existing `AddBotPanel.Instance.Open()`.
- All sizes in 1080×1920 reference units per `unity-ui-builder`; icons = Image + sprite (never TMP glyphs); RoundedCorners script on cards/CTAs.
- Full RU copy deck locked in the spec (formal «вы»).

### Claude's Discretion
- Exact detection point for «Получить первый ответ бота» (first incoming chat containing a fromMe reply — cache-scan vs event hook).
- Pure-class extraction boundaries for EditMode tests; file/prefab naming.
- Whether the carousel re-entry from «О приложении» ships in this phase (cheap) or is dropped.
- Hero compositions' exact construction (mini-chat mock, mode cards, channel cards) within the mockups' visual intent.

### Deferred Ideas (OUT OF SCOPE)
- Localized KZ copy variants, analytics/funnel instrumentation, pre-auth demo-bot sandbox.
- Requirements ONB-01..ONB-05 formalized in REQUIREMENTS.md at v1.3 milestone start (definitions in the spec).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ONB-01 | One-time 3-slide carousel, no skip, CTA into the existing wizard; never re-shown (`OnboardingSeen`); existing users never see it | Gate chokepoint = `BotsPage.RefreshEmptyState()` (`BotsPage.cs:37-42`); existing-user auto-flag = end of `Manager.LoadBots()`; `OnboardingSeen` key namespace verified collision-free; carousel CTA calls existing `AddBotPanel.Instance.Open()` (`AddBotPanel.cs:43`). Screen built by new `OnboardingScreenBuilder` cloning `NavRestructureBuilder`; ordered via `ReorderScreens`. |
| ONB-02 | «Это безопасно» trust block on both channels' auth panels, channel-specific copy, code flows only (no QR) | Auth panel roots `WhatsappAuth`/`TelegramAuth` + `WhatsappCodePanel`/`TelegramCodePanel` (`Manager.cs:18-19,45-47`). ⚠ `ShowWhatsappAuth` uses hardcoded `WhatsappCodePanel.transform.GetChild(3/4/5)` — trust block insertion MUST NOT shift those indices (Pitfall 2). |
| ONB-03 | Post-auth success moment on both channels with «Загрузить прайс-лист» deep link (fallback «Открыть чаты» when files exist) | Success panels `WhatsappAuthSuccessPanel`/`TelegramAuthSuccessPanel` shown by `ShowAuthSuccess(authPage, successPanel)` (`Manager.cs:1598`). ⚠ currently a 2s auto-dismiss that fires BEFORE the bot card exists and TWICE for "both" (Pitfall 3). Deep-link target = `openBotSettings.OpenProductTab()`; files-exist test = `UploadedFilesStore.Load(bot,"product"/"service")`. |
| ONB-04 | Derived-state «Первые шаги» card on BotsPage: 4 steps, channel-aware label, per-row deep links, permanent hide on completion | Facts all verified (§Checklist Facts): `BotsParent.childCount`; `Bot.whatsappProfileId`/`telegramProfileId != "-1"`; `UploadedFilesStore`; `isIncoming==false` via ChatManager events. Card is a new prefab + `FirstStepsCard` MonoBehaviour on BotsPage. |
| ONB-05 | Zero regression — empty state, AddBotPanel auto-open (post-onboarding), auth flows, full EditMode suite green | Gate is additive at one chokepoint; auth-panel edits must preserve `GetChild` indices + `ShowAuthSuccess` semantics for settings re-auth; suite currently ~1124–1134 EditMode tests. |
</phase_requirements>

## Summary

This is a pure client-UI phase with **zero network, n8n, secrets, or chat-pipeline surface**. Everything hangs off five existing, well-understood seams in `Manager.cs`, `BotsPage.cs`, `BotSettings*.cs`, and one editor builder (`NavRestructureBuilder.cs`). The codebase is a single-scene Unity app; all UI is canvas panels toggled with `SetActive`, and screens are built by idempotent `[MenuItem]` editor builders that stamp `[SerializeField]` refs via `SerializedObject`. The onboarding screen, trust blocks, success extension, and checklist card should all follow that builder pattern; the decision/gate logic should be extracted into pure static classes (global namespace, runtime assembly) tested from `Assets/Tests/Editor/Chat/`.

The research confirms most of the approved design maps cleanly onto existing code — **with three material corrections the planner must absorb**: (1) `SnappyFlickScrollRect` is a *vertical* momentum-flick `ScrollRect` subclass, not a horizontal pager — it provides no page-snapping, no dots, and no page-changed event, so the 3-slide pager needs a small new snap/dots controller; (2) `Manager.ShowWhatsappAuth`/`ShowTelegramAuth` address code-panel children by *hardcoded sibling index* (`GetChild(3/4/5)`), so inserting the trust card into those panels will silently break panel setup unless indices are preserved; (3) the auth **success panel currently auto-dismisses after 2 s and fires before the new bot card exists** (and twice for "both" channels), so turning it into an interactive success moment with a deep-link CTA is a behavioral change to `ShowAuthSuccess` + `CreateBotFromForm` sequencing, not just a scene edit.

**Primary recommendation:** Build `Screen_Onboarding` + the checklist card + the two auth enhancements via a small family of idempotent editor builders cloned from `NavRestructureBuilder`; gate the carousel at the single auto-open chokepoint (`BotsPage.RefreshEmptyState`); extract gate/checklist/success-CTA/page-index logic into pure static classes for EditMode tests; and treat the pager, the `GetChild`-index fragility, and the success-panel lifecycle as the three risk areas requiring explicit task steps.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| First-run gate decision | Client / pure logic (`OnboardingGate`) | Client MonoBehaviour (`BotsPage`) | Pure boolean over (hasBots, OnboardingSeen); the MonoBehaviour only supplies facts + acts on the verdict |
| Welcome carousel (3 slides + paging + dots) | Client / Canvas UI (`Screen_Onboarding` + pager MonoBehaviour) | Client / pure page-index math | UI is canvas + DOTween; nearest-page math is testable pure logic |
| Auth trust blocks | Client / Canvas UI (scene children of existing auth panels) | — | Static copy cards; no logic |
| Success moment + deep-link CTA | Client / Canvas UI + `Manager` coroutine (`ShowAuthSuccess`) | Client / pure CTA selector | Selecting «Загрузить прайс-лист» vs «Открыть чаты» is pure; navigation is Manager-owned |
| «Первые шаги» checklist | Client / pure logic (`FirstStepsChecklist`) + `FirstStepsCard` MonoBehaviour | PlayerPrefs (facts) | Step derivation + label + latch are pure; the card renders + deep-links |
| Persistence (flags) | PlayerPrefs (global namespace) | — | `OnboardingSeen`, `OnboardingChecklistDone`, and the step-4 latch live outside the per-bot key namespace |

## Standard Stack

This phase adds **no new libraries**. It uses exactly the project's existing stack. Verified against the codebase.

### Core (already in project — do not add)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Unity | 6000.3.9f1 | Runtime + Editor | Project engine (`ProjectVersion.txt`) |
| TextMeshPro (TMPro) | bundled | All text | Project convention — never legacy `Text` [VERIFIED: rules/ui-scripts.md] |
| DOTween | `Assets/Plugins/DOTween` | All UI animation | Project convention — never Animator for UI [VERIFIED: CLAUDE.md] |
| Nobi.UiRoundedCorners | UPM (own assembly) | Rounded corners on cards/CTAs | `ImageWithRoundedCorners` / `ImageWithIndependentRoundedCorners`; lives in its own assembly [VERIFIED: NavRestructureBuilder.cs:4,619] |
| NUnit | test framework | EditMode tests | `using NUnit.Framework;` in every test [VERIFIED: Tests/Editor/Chat/*.cs] |

**Installation:** none. `npm`/registry checks are N/A (Unity C#, no package additions this phase).

### Supporting (existing project types the phase consumes)
| Type / API | Location | Purpose |
|------------|----------|---------|
| `UploadedFilesStore` (static) | `Assets/Scripts/Main/UploadedFilesStore.cs` | Per-bot, per-type (`"product"`/`"service"`) uploaded-file list in PlayerPrefs |
| `Bot` (MonoBehaviour) | `Assets/Scripts/Main/Bot.cs` | Per-bot state; `whatsappProfileId`/`telegramProfileId`, `UnauthedProfileSentinel = "-1"` |
| `BottomTabManager` (static consts) | `Assets/Scripts/Main/BottomTabManager.cs` | `WhatsAppTabIndex = 0` (Chats), `BotsTabIndex = 2`; `SwitchTab(int)` |
| `AddBotPanel` (singleton) | `Assets/Scripts/Main/AddBotPanel.cs` | `Instance.Open()` / `Close()`; idempotent |
| `ChatManager` (events) | `Assets/Scripts/Main/ChatManager.cs` | `OnLiveMessagesReceived`, `OnBatchMessagesLoaded` (carry `List<MessageViewModel>`) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `SnappyFlickScrollRect` as pager | New `OnboardingPager` (horizontal `ScrollRect` + snap on end-drag + dot binder) | **Recommended.** `SnappyFlickScrollRect` gives no paging (Pitfall 1); reuse its base `ScrollRect` at most |
| Runtime instantiation of onboarding UI | Editor builder (`[MenuItem]` + headless entry) | **Builder recommended** — matches project rule "UI built by builders", enables idempotent rebuild + `SerializedObject` stamping [VERIFIED: rules/editor-scripts.md, CLAUDE.md] |
| Cache-scan for "first bot reply" | Event-hook + PlayerPrefs latch | **Latch recommended** (§Checklist Facts) — cache-scan needs chat-id enumeration + disk reads per refresh |

## Architecture Patterns

### System Architecture Diagram

```
COLD LAUNCH
  Manager.Start() ─────────────────────────────────────────────┐
    ├─ PopulateBusinessTypes()                                  │
    ├─ StartCoroutine(LoadBots())                               │
    │     ├─ instantiate Bot cards from PlayerPrefs (id/BotN*)  │
    │     ├─ [INSERT] existing-user auto-flag:                  │  ← set OnboardingSeen=1 here
    │     │     if bots exist && !OnboardingSeen → OnboardingSeen=1
    │     └─ orphan-profile sweep (PendingProfileLedger)        │
    └─ BottomTabManager.Start() → SwitchTab(defaultTabIndex=0=Chats)
                                                                 │
USER NAVIGATES TO BOTS TAB (or taps Chats empty-state CTA → SwitchTab(Bots))
  BotsPage.OnEnable → Invoke(RefreshEmptyState)                 │
    RefreshEmptyState():                                        │
      hasBots = botsParent.childCount > 0                       │
      emptyState.SetActive(!hasBots)                            │
      if (!hasBots):                                            │
        ┌── [GATE — INSERT] ────────────────────────────────┐  │
        │ if OnboardingGate.ShouldShowCarousel(hasBots,     │  │
        │      OnboardingSeen):  ShowScreen_Onboarding()     │  │  ← carousel instead of auto-open
        │ else:                  StartNewBot()  (existing)   │  │
        └────────────────────────────────────────────────────┘ │
                                                                 │
CAROUSEL (Screen_Onboarding, 3 slides)                          │
  slide-3 CTA «Создать бота»:                                   │
    PlayerPrefs.SetInt("OnboardingSeen",1)                      │
    hide Screen_Onboarding                                      │
    BotsPage.Instance.StartNewBot()  → AddBotPanel.Open()  ─────┘

ADD-BOT WIZARD  (Manager.CreateBotFromForm — UNCHANGED logic)
  name · business · channel → CreateWhatsappProfile → ShowWhatsappAuth()
    (+ [INSERT] «Это безопасно» trust card child of WhatsappCodePanel/TelegramCodePanel)
  auth poll → ShowAuthSuccess(authPage, successPanel)
    (+ [ENHANCE] interactive success moment: «Загрузить прайс-лист» / «Позже»,
       fallback «Открыть чаты» when UploadedFilesStore already has files)
  → bot card instantiated (Step 3) → SetActiveBot(newBot.name)

BOTS PAGE (post-auth)
  FirstStepsCard.Refresh()  (on OnEnable + relevant events)
    step facts (derived live):
      1 create bot     ← BotsParent.childCount > 0            → deep-link AddBotPanel
      2 connect W/T    ← bot.{wa,tg}ProfileId != "-1"         → deep-link auth screen
      3 upload price   ← UploadedFilesStore(product|service)  → deep-link BotSettings.OpenProductTab
      4 first reply    ← isIncoming==false latch (event hook) → deep-link SwitchTab(Chats)
    hide permanently when 4/4 && OnboardingChecklistDone latched
```

### Recommended file/component layout

```
Assets/Scripts/Main/Onboarding/          # new folder (mirrors Dashboard/ subfolder pattern)
├── OnboardingGate.cs            # pure: ShouldShowCarousel(hasBots, seen) + existing-user auto-flag rule
├── OnboardingKeys.cs            # (optional) const strings for the 3 global PlayerPrefs keys
├── OnboardingPageMath.cs        # pure: NearestPage(normalizedX, pageCount), PageToNormalizedX(...)
├── OnboardingPager.cs           # MonoBehaviour: horizontal ScrollRect snap + OnPageChanged → dots
├── OnboardingScreen.cs          # MonoBehaviour: slide-3 CTA → set flag + AddBotPanel.Open()
├── SuccessCtaSelector.cs        # pure: files-exist → «Открыть чаты» vs «Загрузить прайс-лист»
├── FirstStepsChecklist.cs       # pure: per-step fact→bool, channel label, 4/4 completion
└── FirstStepsCard.cs            # MonoBehaviour: renders rows, cascade, per-row deep links

Assets/Editor/
├── OnboardingScreenBuilder.cs        # [MenuItem] + BuildHeadless — Screen_Onboarding (clone NavRestructureBuilder)
├── OnboardingAuthBlocksBuilder.cs    # [MenuItem] + BuildHeadless — trust cards + success CTA into auth panels
└── FirstStepsCardBuilder.cs          # [MenuItem] + BuildHeadless — checklist prefab/card into BotsPage

Assets/Tests/Editor/Chat/         # NO asmdef — plain NUnit into Assembly-CSharp-Editor
├── OnboardingGateTests.cs
├── OnboardingPageMathTests.cs
├── SuccessCtaSelectorTests.cs
└── FirstStepsChecklistTests.cs
```

### Pattern 1: Idempotent editor builder (the project's UI-construction pattern)
**What:** A static class with `[MenuItem("Tools/…/Build")]` + a `BuildHeadless()` entry that opens `Main.unity`, mutates, saves, and logs the sentinel `"[<Class>] Headless build + save complete"`. All construction goes through shared helpers; teardown-then-rebuild is idempotent.
**When to use:** Any new screen, card, or scene child in this project.
**Reusable helpers to copy verbatim from `NavRestructureBuilder.cs`:**
```csharp
// Source: Assets/Editor/NavRestructureBuilder.cs
// Font GUIDs (default font's weight table is empty — always assign explicitly):
const string RegularGuid  = "e0cdfe2d6a51446bcba7d2df147e2415";
const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
const string BoldGuid     = "1cd715823fef34be4a3d3f3c5572594c";

// Helpers (lines 552-659): Hex, NewChild, SetAnchors, StretchFill, SetPreferredSize,
// AddText (sets textWrappingMode=Normal, raycastTarget=false), AddIconImage,
// AddRounded + RefreshRounded (RoundedCorners deferred-bake via _roundedToRefresh
// list + Canvas.ForceUpdateCanvases() before refresh), DestroyAllByName (idempotent
// teardown), FindDeepChild, LoadFont, LoadSprite, EnsureIconImportSettings,
// StampManagerFields (SerializedObject stamping of [SerializeField] refs).

// Design tokens (match the spec's color deck exactly):
Ink="#1A1A2E"  Muted="#65676B"  Primary="#1B7CEB"  Card=white
Divider="#E4E6EB"  HeaderHeight=300  CardRadius=40  ButtonHeight=144
```
**Headless run (Editor CLOSED):** `Tools/run-editor-builder.sh OnboardingScreenBuilder.BuildHeadless`
— refuses if the Editor holds the project lock; the sentinel must be exactly `"[OnboardingScreenBuilder] Headless build + save complete"` or the script reports NOT GREEN. Commit `Main.unity` immediately after a green run (parallel-scene-clobber rule).

### Pattern 2: Pure-logic class + thin MonoBehaviour (the project's testability pattern)
**What:** Decision logic lives in a `static` class in the **runtime assembly, global namespace** (e.g. `ChatRowSwipePolicy.Enabled(channel)`, `ChannelSwitcherModel.StateFor(...)`); the MonoBehaviour supplies facts and acts on the verdict.
**When to use:** Gate, checklist derivation, success-CTA selection, page-index math.
**Example (test shape):**
```csharp
// Source: Assets/Tests/Editor/Chat/ChatRowSwipePolicyTests.cs
using NUnit.Framework;
public class OnboardingGateTests {
    [Test] public void FirstRun_NoBots_FlagUnset_ShowsCarousel() =>
        Assert.IsTrue(OnboardingGate.ShouldShowCarousel(hasBots:false, seen:false));
    [Test] public void ExistingUser_BotsPresent_NeverShows() =>
        Assert.IsFalse(OnboardingGate.ShouldShowCarousel(hasBots:true, seen:false));
}
```

### Pattern 3: SerializedObject field stamping (builder → MonoBehaviour refs)
**What:** Builders stamp `[SerializeField] private` references into MonoBehaviours via `SerializedObject`/`FindProperty`/`ApplyModifiedPropertiesWithoutUndo` (never public fields). Field names are the builder↔component contract.
**Example:** `NavRestructureBuilder.StampManagerFields` (lines 477-492) and `BuildEmptyState` stamping `BotsPage.emptyState`/`botsParent` (lines 405-409).

### Anti-Patterns to Avoid
- **TMP-drawn icons** (chevrons/checks/locks as glyphs) — they silently don't render. Use `Image` + sprite [VERIFIED: unity-ui-builder SKILL].
- **Runtime `SetAsLastSibling` for screen order** — order is baked by `ReorderScreens` at build time; runtime reorder buries the auth pages behind the overlay [VERIFIED: AddBotPanel.cs:48-50, NavRestructureBuilder.cs:439-473].
- **Rebuilding via `NavRestructureBuilder.Build()`** — it now **throws** on the restructured scene (identity guard, lines 163-170). The new builder must not depend on re-running it.
- **Storing per-step checklist state** — derive live from facts; only latch the 4/4 completion (`OnboardingChecklistDone`).
- **Hardcoding `"Bot0"`** — derive the bot key prefix from a live `Bot.name` [VERIFIED: bot-persistence SKILL].

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Per-bot uploaded-file count | New PlayerPrefs reads | `UploadedFilesStore.Load(botName, "product"/"service")` | Handles count key + item keys + tail-orphan conventions [VERIFIED: UploadedFilesStore.cs] |
| Unauthed-channel test | String compares to `""`/`"-1"` inline | `!= Bot.UnauthedProfileSentinel` (`"-1"`) | Named sentinel; `""` and `"-1"` both mean "no profile" [VERIFIED: Bot.cs:67] |
| Open Add-Bot overlay | New show/hide code | `AddBotPanel.Instance.Open()` (idempotent) | Already handles slide-in + sibling-order invariants [VERIFIED: AddBotPanel.cs:43] |
| Switch to a tab | Manual `SetActive` juggling | `BottomTabManager.SwitchTab(index)` | Closes AddBotPanel overlay, guards indices, re-syncs chats [VERIFIED: BottomTabManager.cs:144] |
| Open a bot's settings at Files | New settings-open flow | `Bot.OpenSettings` path + `BotSettings.OpenProductTab()` | Sets `Manager.openBot`/`openBotSettings`, matches paired settings by sibling index, refreshes files [VERIFIED: Bot.cs:100, BotSettings.cs:405] — **but see Open Question 1: `OpenSettings` is `private`** |
| Rounded corners | Custom mesh/shader | `AddRounded`/`RefreshRounded` (Nobi.UiRoundedCorners) | Own-assembly component with a deferred-bake quirk already solved in the builder [VERIFIED: NavRestructureBuilder.cs:617-638] |
| Scene screen ordering | Runtime `SetAsLastSibling` | `NavRestructureBuilder.ReorderScreens` (teach it the new name) | Keeps auth pages LAST above the overlay [VERIFIED: NavRestructureBuilder.cs:449-473] |

**Key insight:** Almost every capability this phase needs already has a canonical entry point. The net-new code is: the gate boolean, the pager snap/dots, the checklist derivation, the success-CTA selector, and the scene-construction builders. Everything else is wiring to existing APIs.

## Runtime State Inventory

> Onboarding adds three **global** PlayerPrefs flags. There is no rename/migration; this table documents the new persistent state and confirms no collisions.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data (new flags) | `OnboardingSeen` (int 1), `OnboardingChecklistDone` (int 1), and a step-4 latch (recommend `FirstBotReplySeen`, int 1) — all **global**, outside the `BotN…` namespace | Write-only new keys; wiped by `PlayerPrefs.DeleteAll()` in «Удалить все данные» (intended — full wipe re-runs onboarding) |
| Global key collision check | Existing non-bot keys verified: `"ids"`, `"Locale"`, `"WhatsappCooldownFinishTime"`, `"TelegramCooldownFinishTime"`, and the `"Bot"` prefix namespace | **None** — the three new names do not collide (verified by grep over `Assets/Scripts`) |
| Live service config | None — no n8n/Wappi/Supabase config touched | None |
| OS-registered state | None | None |
| Secrets/env vars | None — no secrets read or written | None |
| Build artifacts | Scene `Main.unity` gains `Screen_Onboarding`, trust-card + success-CTA children in the auth panels, and the checklist card under BotsPage — all via builders | Run each builder headless, commit `Main.unity` immediately (parallel-scene-clobber rule) |

**Existing-user auto-flag (verified requirement):** On first run after this update, users who already have bots must be flagged `OnboardingSeen=1` so the carousel never appears. The single place that knows the bot count post-load is the **end of `Manager.LoadBots()`** (`Manager.cs:426`, after the instantiation loop). Set the flag there when `id > 0` / any `BotN Name` key exists and `OnboardingSeen` is unset.

## Common Pitfalls

### Pitfall 1: `SnappyFlickScrollRect` is a vertical flick enhancer, NOT a horizontal pager
**What goes wrong:** The spec/CONTEXT say "Carousel paging = existing `SnappyFlickScrollRect`." Reading the class (`Assets/Scripts/Main/SnappyFlickScrollRect.cs`) shows it is a `ScrollRect` subclass whose *only* behavior is amplifying **vertical** flick momentum (`velocity.y`, `preDragVelocityY`, `maxVelocity`). It has **no page snapping, no dot/page-index concept, and no page-changed event**. Dropping it in as the pager yields a free-scrolling horizontal strip with no snap and no dot sync.
**Why it happens:** The name suggests "snappy paging"; it actually means "snappy flick momentum" for long vertical lists.
**How to avoid:** Build a small `OnboardingPager` MonoBehaviour: a **horizontal** `ScrollRect` (movementType = Clamped, inertia off or low), snap to the nearest page on `IEndDragHandler` via `DOAnchorPos`/`DONormalizedPos` 0.3s OutCubic, and raise `OnPageChanged(int)` to drive the dot pills. Extract the nearest-page math into a pure `OnboardingPageMath` (testable). Reuse `SnappyFlickScrollRect` only if a base `ScrollRect` is wanted — but it contributes nothing to horizontal paging.
**Warning signs:** Slides don't settle on a page; dots never update; a half-swipe leaves two slides visible.
**Confidence:** HIGH [VERIFIED: SnappyFlickScrollRect.cs read in full].

### Pitfall 2: Auth code panels are addressed by HARDCODED sibling index
**What goes wrong:** `Manager.ShowWhatsappAuth` (`Manager.cs:1652-1656`) does `WhatsappCodePanel.transform.GetChild(3)/(4)/(5)` and `GetChild(4).GetChild(0)` to toggle sub-states and clear the code label. Inserting the «Это безопасно» trust card as a child of `WhatsappCodePanel` (or `TelegramCodePanel`) will **shift those indices** and the panel setup will target the wrong children — breaking the code flow with no compile error.
**Why it happens:** The panels are hand-built scene objects (no dedicated builder exists for them); positional child access is baked into `Manager`.
**How to avoid:** Insert the trust card at a sibling index **at or after the last referenced child** (append as the new last child, index ≥ 6), OR insert it into a container that isn't the indexed panel (e.g. a wrapper above the code panel), OR update the `GetChild(n)` constants in `ShowWhatsappAuth`/`ShowTelegramAuth` in the same task. The builder should `DestroyAllByName` its own trust card first (idempotent) and re-append last. Verify Telegram's mirror (`ShowTelegramAuth`, `Manager.cs:1695-1696` uses `GetChild(3)`).
**Warning signs:** After the trust card lands, "Получить код" shows the wrong state, the code label doesn't clear, or the number input hides unexpectedly.
**Confidence:** HIGH [VERIFIED: Manager.cs:1641-1724].

### Pitfall 3: The success panel auto-dismisses in 2 s, fires BEFORE the bot exists, and fires TWICE for "both"
**What goes wrong:** `ShowAuthSuccess(authPage, successPanel)` (`Manager.cs:1598-1639`) shows `successPanel` for a fixed `WaitForSeconds(2f)`, hides it, and — for the final auth step — closes AddBotPanel + switches to the Bots tab. In `CreateBotFromForm`, `whatsappAuthCompleted`/`telegramAuthCompleted` are set right after `ShowAuthSuccess`, and the **bot card + settings are only instantiated afterward (Step 3, `Manager.cs:1366`)**. So at success time the just-created bot GameObject does not yet exist, and for `selectedPlatform == 3` ("both") `ShowAuthSuccess` runs **twice** (once per channel; the WhatsApp one hits the `moreAuthSteps` branch and just flashes a check + shows `LoadingPanel`).
**Why it happens:** The success animation was designed as a transient checkmark, not an interactive moment; bot creation is deliberately deferred until both channels authorize.
**How to avoid (planner):** The «Бот подключён!» interactive moment (CTA «Загрузить прайс-лист» / «Позже», fallback «Открыть чаты») must fire **only on the final auth completion and after the bot card exists**, i.e. re-sequence so the success CTA phase runs after Step 3–6 of `CreateBotFromForm` (or defers its deep-link to `ChatManager.Instance` active bot, which is set to `newBot.name` at `Manager.cs:1468`). It must also handle the **settings re-auth path** (`isCreatingBot == false`), where the bot already exists and the files-exist fallback applies. Replace the fixed 2 s auto-dismiss with a wait-for-user-input state on the final success only; keep the intermediate WhatsApp-of-"both" step as the existing transient check.
**Warning signs:** CTA deep-links to a null bot; success moment shows twice; success dismisses itself before the user can tap.
**Confidence:** HIGH [VERIFIED: Manager.cs:1318-1474, 1598-1639, 2098].

### Pitfall 4: `ReorderScreens` is private and only reachable through a self-aborting build
**What goes wrong:** `NavRestructureBuilder.ReorderScreens(container)` is `private static` and is called only from `BuildInternal`, which **throws** on the already-restructured scene (identity guard at lines 163-170). So the new builder cannot "just call NavRestructureBuilder to reorder," and adding `Screen_Onboarding` to the `order[]` array alone does nothing unless that method actually runs.
**How to avoid:** Change `ReorderScreens` to `internal static` (or add an `internal static ReorderScreensPublic`), add `"Screen_Onboarding"` to the `order[]` array **after `"Screen_New"` and before `"WhatsappAuth"`**, and have `OnboardingScreenBuilder` call it after building the screen. Alternatively, replicate the short ordered-`SetAsLastSibling` loop in the new builder with the full name list. Either way the auth pages must remain LAST.
**Confidence:** HIGH [VERIFIED: NavRestructureBuilder.cs:33,140-219,449-473].

### Pitfall 5: "First bot reply" (`fromMe`) cannot distinguish bot from owner
**What goes wrong:** The checklist step «Получить первый ответ бота» keys on a `fromMe` (outgoing) message. `MessageViewModel` exposes `bool isIncoming` (`MessageViewModel.cs:17`) — but `isIncoming == false` is true for **both** a genuine bot auto-reply **and** the owner's own manual message; there is no flag separating them (both are sent from the owner's linked account).
**How to avoid:** Accept `isIncoming == false` as a pragmatic proxy for "a reply has gone out on this account" (the spec's intent — demonstrate the reply mechanism), and document it. If a stricter signal is ever needed it would require server/n8n metadata (explicitly out of scope this phase).
**Confidence:** HIGH [VERIFIED: MessageViewModel.cs, ChatManager.cs:698,794].

### Pitfall 6: A brand-new user lands on the CHATS tab, not Bots
**What goes wrong:** `BottomTabManager.Start()` calls `SwitchTab(defaultTabIndex)` with `defaultTabIndex = WhatsAppTabIndex = 0` (Chats). There is **no** automatic redirect to the Bots tab on zero bots. The AddBotPanel auto-open only fires on the first `BotsPage.OnEnable` (user navigates to Bots, or taps the Chats empty-state «Создать бота» which calls `SwitchTab(Bots)` → `RefreshEmptyState`). The spec's "opens onto the Bots empty state" is therefore a simplification.
**How to avoid:** Gate at the true chokepoint — `BotsPage.RefreshEmptyState()`'s `if (!hasBots) StartNewBot()` — which every zero-bot auto-open path routes through. Decide (Open Question 3) whether an explicit Chats-CTA tap on true first run should also show the carousel.
**Confidence:** HIGH [VERIFIED: BottomTabManager.cs:103,129-133; BotsPage.cs:37-55; EmptyStateView.cs:314].

## Code Examples

Verified integration points (signatures + preconditions), pulled from source.

### Gate chokepoint — `BotsPage.RefreshEmptyState`
```csharp
// Source: Assets/Scripts/Main/BotsPage.cs:37-42  (INSERT the gate here)
public void RefreshEmptyState() {
    bool hasBots = botsParent != null && botsParent.childCount > 0;
    if (emptyState != null) emptyState.SetActive(!hasBots);
    if (!hasBots) StartNewBot();   // ← replace with: carousel if OnboardingGate.ShouldShowCarousel(...), else StartNewBot()
}
// StartNewBot() → SwitchTab(BotsTabIndex) then AddBotPanel.Instance?.Open()  (BotsPage.cs:49-55)
```

### Deep-link A — open a bot's settings at the «Прайс-листы» (Product) tab
```csharp
// Source: Assets/Scripts/Main/Bot.cs:100 (private OpenSettings sets Manager.openBot/openBotSettings,
//   matches paired BotSettings by identical sibling index, calls RefreshUploadedFiles)
// Source: Assets/Scripts/Main/BotSettings.cs:405 — public OpenProductTab() (the Product tab HOSTS the
//   «Прайс-листы» section + upload button — there is NO separate Files tab; see BotSettings.Files.cs)
// Precondition: the target bot's card must exist; OpenSettings is PRIVATE (Open Question 1).
// After settings open: Manager.openBotSettings.OpenProductTab();
```

### Deep-link B — open the Chats screen (success-panel «Открыть чаты» / checklist step 4)
```csharp
// Source: Assets/Scripts/Main/BottomTabManager.cs:82,144
BottomTabManager.WhatsAppTabIndex; // == 0 (the «Чаты» tab)
tabManager.SwitchTab(BottomTabManager.WhatsAppTabIndex);
// optional: ChatManager.Instance.SetActiveBot(botName) first (Manager.cs:1468 already does this on create)
```

### Deep-link C — open the auth screen for a bot's channel (settings-mode entry, public)
```csharp
// Source: Assets/Scripts/Main/Manager.cs:913,931
public void OpenWhatsappAuthFromSettings(string profileId, System.Action onDone, System.Action onBack);
public void OpenTelegramAuthFromSettings(string profileId, System.Action onDone, System.Action onBack);
// Precondition: Manager.openBot must be set to the target bot; a "-1" profileId means "not provisioned"
//   — the settings connect flow (BotSettings.CheckWhatsappAuthorization, BotSettings.Auth.cs:68) first
//   calls GetCreateWhatsappProfile then ShowWhatsappAuthFromSettings. For the checklist "connect" row,
//   prefer routing through the bot's settings General tab (where the connect toggle lives) rather than
//   calling OpenWhatsappAuthFromSettings raw — it needs onDone/onBack wired to persist the new id.
```

### Checklist fact — uploaded files (MUST check both content types)
```csharp
// Source: Assets/Scripts/Main/UploadedFilesStore.cs:22
bool hasFiles =
    UploadedFilesStore.Load(bot.name, "product").Count > 0 ||
    UploadedFilesStore.Load(bot.name, "service").Count > 0;
```

### Checklist fact — channel authed + channel label
```csharp
// Source: Assets/Scripts/Main/Bot.cs:67-70
bool waAuthed = bot.whatsappProfileId != Bot.UnauthedProfileSentinel; // "-1"
bool tgAuthed = bot.telegramProfileId != Bot.UnauthedProfileSentinel;
// channel label from PlayerPrefs isOnWhatsapp/isOnTelegram (int 0/1), default 1:
//   PlayerPrefs.GetInt(bot.name + "isOnWhatsapp", 1) == 1  → label "WhatsApp"
//   PlayerPrefs.GetInt(bot.name + "isOnTelegram", 1) == 1  → label "Telegram"
```

### Checklist fact — first bot reply (recommended: event-hook + latch)
```csharp
// Source: Assets/Scripts/Main/ChatManager.cs:69-70 (events carry List<MessageViewModel>)
// Source: Assets/Scripts/UI/MessageViewModel.cs:17 (public bool isIncoming)
// FirstStepsCard subscribes on enable:
ChatManager.Instance.OnBatchMessagesLoaded += (msgs, _, _) => LatchIfReplySeen(msgs);
ChatManager.Instance.OnLiveMessagesReceived += LatchIfReplySeen;
void LatchIfReplySeen(List<MessageViewModel> msgs) {
    if (msgs != null && msgs.Exists(m => !m.isIncoming))
        PlayerPrefs.SetInt("FirstBotReplySeen", 1); // global latch — proxy (Pitfall 5)
}
```

## State of the Art

Not applicable in the usual sense — this is codebase archaeology, not evolving external tech. The relevant "state" is the current shape of the surfaces this phase touches, all read at source on 2026-07-17:

| Surface | Current shape (as of 2026-07-17) | Impact on plan |
|---------|----------------------------------|----------------|
| Screen order | `Whatsapp, Bots, Profile, Dashboard, New, WhatsappAuth, TelegramAuth` (auth LAST) | Insert `Screen_Onboarding` after `New`, before auth |
| Add-Bot | Slide-in overlay (`AddBotPanel`), not a tab; tab 2 = «Сводка» dashboard | Carousel CTA calls `AddBotPanel.Open()` |
| Tabs | `Chats=0`, `Dashboard=1`, `Bots=2`, `Profile=3` (Telegram tab removed in 06-02) | Chats deep-link = `SwitchTab(0)`; Bots = 2 |
| Auth panels | Shared by wizard + settings re-auth; QR panels present but the phase uses code flows only | Trust block appears in both contexts (desirable per spec) |
| BotSettings tabs | `General, Business, Product, Service, Prompt`; «Прайс-листы» lives inside Product/Service | Deep-link = `OpenProductTab()` |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | «Прайс-листы» deep-link target is the **Product** tab (`OpenProductTab`) — the price-list upload UI lives in Product/Service tabs, there is no dedicated Files tab | Deep-link A | Low — if the intent is the Service tab instead, swap the call; both host the same section pattern [supported by BotSettings.Files.cs but the spec doesn't name Product vs Service] |
| A2 | The checklist tracks **the (single) onboarding bot**; for a first-run user that is the only/first bot | Checklist | Medium — multi-bot users could see ambiguous facts; the card is normally hidden for them (4/4 latch) but see Open Question 4 |
| A3 | Existing-user auto-flag belongs at the end of `Manager.LoadBots()` (the first place bot count is known post-load) | Runtime State Inventory | Low — any post-load site works; this is the earliest clean one |
| A4 | `isIncoming == false` is an acceptable proxy for "bot reply" (cannot distinguish bot vs owner) | Pitfall 5 | Low — matches the spec's demonstrative intent; documented |

**Note:** The CONTEXT/spec claim "Carousel paging = existing `SnappyFlickScrollRect`" is **contradicted by the code** (Pitfall 1). This is not an assumption — it is a verified correction the planner must reconcile with the locked decision.

## Open Questions (RESOLVED)

**Resolution (2026-07-17 — planning + revision iteration 1). All four decided; ship-as-is where noted.**

- **Q1 — `Bot.OpenSettings` is `private`:** RESOLVED — Plan 11-01 adds public thin wrappers `Bot.OpenSettingsAtProductTab()` and `Bot.OpenSettingsAtGeneralTab()` that call the existing private `OpenSettings()` then select the tab (`OpenProductTab()` / `OpenGeneralTab()`). The Edit-button path is untouched; `OpenSettings` stays private. The checklist «Подключить {channel}» row (11-06 row 2) uses the General-tab entry; the success CTA + row 3 use the Product-tab entry.
- **Q2 — where the interactive success CTA runs:** RESOLVED — Plan 11-04 fires `ShowInteractiveSuccessMoment` from `CreateBotFromForm` AFTER Step 6 (bot card exists), exactly once, on the final channel's own panel + per-channel field set. The moment reactivates the auth-page hierarchy that hosts the nested success panel and defers `authPage.SetActive(false)` to dismissal (fixes the nested-panel-never-renders hazard).
- **Q3 — gate scope (auto-open only, or also explicit CTA taps):** RESOLVED — Plan 11-03 gates ONLY at the shared `BotsPage.RefreshEmptyState()` auto-open chokepoint. Every zero-bot path (including the Chats empty-state «Создать бота» CTA) routes through it via `SwitchTab(Bots) → RefreshEmptyState`, so no separate `StartNewBot` / `EmptyStateView` gate is needed.
- **Q4 — checklist suppression for existing users mid-setup:** RESOLVED — **ship as-is** (acceptable). The «Первые шаги» card derives every row from real facts and hides permanently at 4/4 (`OnboardingChecklistDone`). An existing user with a bot but no files/reply seeing the remaining helpful steps matches the spec's intent; no "fresh-install" gate is added. (Note: the existing-user carousel auto-flag in 11-03 keys on live `BotsParent.childCount`, independent of this checklist decision.)

1. **`Bot.OpenSettings` is `private`** — the success-panel «Загрузить прайс-лист» CTA and the checklist rows both need to open a *specific* bot's settings programmatically, but `OpenSettings` (Bot.cs:100) is private and only wired to the card's Edit button.
   - What we know: it sets `Manager.openBot`/`openBotSettings`, matches the paired `BotSettings` by sibling index, refreshes files, and slides in.
   - What's unclear: the cleanest exposure.
   - Recommendation: make `Bot.OpenSettings` public (or add a thin `Bot.OpenSettingsAtProductTab()` that calls it then `Manager.openBotSettings.OpenProductTab()`), or a `Manager` helper `OpenBotSettingsFilesTab(Bot)`. Minimal, additive, no behavior change to the Edit-button path.

2. **Where the interactive success CTA phase runs in `CreateBotFromForm`** (Pitfall 3) — the bot card is created only after both auth steps. Recommendation: run the CTA phase after Step 3–6 (bot exists), or bind the deep-link to `ChatManager.Instance` active bot (already set to `newBot.name`). Needs a concrete task decision.

3. **Gate scope: auto-open only, or also explicit CTA taps?** The spec says "carousel INSTEAD of the AddBotPanel auto-open." A first-run user who taps the Chats empty-state «Создать бота» (an explicit tap, not an auto-open) would, under a strict reading, skip the carousel.
   - Recommendation: gate in the shared chokepoint (`RefreshEmptyState`) for the auto-open; optionally also gate `StartNewBot`/`EmptyStateView.OpenCreateBotFlow` so true first-run always shows the carousel. Decide at planning.

4. **Should the checklist be suppressed for existing users mid-setup?** `OnboardingChecklistDone` only hides the card at 4/4. An existing user with a bot but no files/reply would suddenly see the checklist after this update. The spec doesn't gate it (it may be intended as helpful). Confirm whether the checklist should also respect a "not a fresh install" signal.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Unity Editor | Running builders + EditMode tests | Assumed | 6000.3.9f1 | — (blocking for scene construction) |
| `Tools/run-editor-builder.sh` | Headless builder runs (Editor closed) | ✓ present | — | Run builder from Tools menu with Editor open, then save scene by hand |
| `Tools/run-tests-headless.sh` | EditMode suite (Editor closed) | ✓ present | — | In-Editor test bridge (`Temp/claude/run-tests.trigger`) |

**Notes:** No external services (n8n/Wappi/Supabase) are touched — no service availability matters. The builders require the Unity Editor **closed** for headless runs (project single-instance lock); if the Editor is open, use the Tools-menu build + manual save + immediate commit path (parallel-scene-clobber rule). Verified from `run-editor-builder.sh` lock guard.

## Security Domain

> `security_enforcement` is not set in `.planning/config.json` (treated as enabled). This phase is **client-only UI + non-sensitive PlayerPrefs flags**; most ASVS categories are not applicable. Documented for completeness.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No auth logic added; the existing Wappi/Telegram code flows are unchanged (trust block is copy only) |
| V3 Session Management | no | No sessions; no tokens read/written |
| V4 Access Control | no | No server calls or authorization decisions |
| V5 Input Validation | no | No untrusted input parsed; onboarding flags are app-written ints (0/1) |
| V6 Cryptography | no | No crypto; no secrets touched |
| V9 Data Protection | minimal | New PlayerPrefs flags (`OnboardingSeen`, `OnboardingChecklistDone`, `FirstBotReplySeen`) contain no PII/secrets; wiped by the existing full-wipe path |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Trust-block copy over-promising security (e.g. implying end-to-end guarantees the app can't make) | Repudiation / trust | Use the locked, owner-approved copy verbatim; no claims beyond «официальные Связанные устройства» / «официальный вход Telegram» |
| Regression breaking the real auth flow via scene-index shift | Tampering (integrity of existing flow) | Preserve `GetChild` indices (Pitfall 2); full EditMode suite green (ONB-05) |

## Sources

### Primary (HIGH confidence — read at source this session)
- `Assets/Scripts/Main/Manager.cs` — `Start`/`LoadBots` (240-441), orphan sweep (428-440), shared settings-auth (898-1009), `CreateBotFromForm` (1318-1474), `ShowAuthSuccess`/`ShowWhatsappAuth`/`ShowTelegramAuth` (1598-1724), success call sites (2098, 2609)
- `Assets/Scripts/Main/BotsPage.cs` — gate chokepoint (`RefreshEmptyState`/`StartNewBot`, 24-55)
- `Assets/Scripts/Main/AddBotPanel.cs` — `Instance`/`Open`/`Close` (whole file)
- `Assets/Scripts/Main/Bot.cs` — `OpenSettings` (100), profile-id fields + `UnauthedProfileSentinel` (61-73)
- `Assets/Scripts/Main/BotSettings.cs` — tab API `OpenProductTab`/`SetActiveTab` (403-437), `OnEnable`/`RefreshUploadedFiles`
- `Assets/Scripts/Main/BotSettings.Auth.cs` — connect-channel flow (`CheckWhatsappAuthorization`, `ShowWhatsappAuthFromSettings`, 66-155)
- `Assets/Scripts/Main/BotSettings.Files.cs` — `UploadedFilesStore` consumption; Product/Service file sections (whole file)
- `Assets/Scripts/Main/UploadedFilesStore.cs` — `Load`/keys (whole file)
- `Assets/Scripts/Main/SnappyFlickScrollRect.cs` — pager reality check (whole file)
- `Assets/Scripts/Main/BottomTabManager.cs` — `WhatsAppTabIndex`/`BotsTabIndex`/`SwitchTab`/`Start` (60-188)
- `Assets/Scripts/UI/MessageViewModel.cs` — `isIncoming` (5-50)
- `Assets/Scripts/Main/ChatManager.cs` — event surface (66-135, fromMe sites 698/794)
- `Assets/Editor/NavRestructureBuilder.cs` — builder pattern, helpers, `ReorderScreens`, font GUIDs, tokens (whole file)
- `Tools/run-editor-builder.sh` — headless builder runner + sentinel contract (whole file)
- `Assets/Tests/Editor/Chat/ChannelSwitcherModelTests.cs`, `ChatRowSwipePolicyTests.cs` — test conventions (no asmdef, NUnit, pure classes)
- Project docs: `CLAUDE.md`, `.claude/skills/{unity-ui-builder,bot-persistence,mobile-app-ui-design}/SKILL.md`, `.claude/rules/{ui-scripts,editor-scripts,unity-general}.md`
- `docs/superpowers/specs/2026-07-17-first-run-onboarding-design.md` (approved spec), `11-CONTEXT.md` (locked decisions)

### Secondary / Tertiary
- None — no web/Context7 lookups were needed; this phase adds no libraries and the domain is fully internal.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries; all existing types/APIs read at source.
- Architecture / integration points: HIGH — every gate, deep-link, builder helper, and event verified in code.
- Pitfalls: HIGH — the three material corrections (pager, GetChild indices, success lifecycle) are each confirmed by reading the relevant method bodies.
- Open questions: flagged honestly (private `OpenSettings`, success sequencing, gate scope, checklist suppression) — these are design decisions for the planner, not knowledge gaps.

**Research date:** 2026-07-17
**Valid until:** ~2026-08-16 (30 days) — stable internal surfaces; re-verify only if `Manager.cs` auth/create flow, `BotsPage`, or `NavRestructureBuilder` change before planning.
