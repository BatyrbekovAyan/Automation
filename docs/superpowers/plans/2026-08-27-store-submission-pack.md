# Store Submission Pack — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the app submittable to App Store / Google Play before Block 2: legal links + auto-renew disclosure at the paywall's purchase point, hostable RU legal documents, and a console-side submission kit.

**Architecture:** One new pure seam (`LegalLinks`) + one new `PaywallRows` function carry every new string; `PaywallController` renders from them; the scene gets ONE additive node (`BottomBar/LegalRow`) via a new additive entry in `PaywallBuilder` (Task-18 `AddPurchaseButton` precedent — the full destructive `Build` is never re-run). Documents are static deliverables under `docs/legal/` and `docs/store/`.

**Tech Stack:** Unity 6 C# (EditMode NUnit tests), uGUI + TMP + ThemedColor, hand-written HTML for legal pages.

## Global Constraints

- RU-only UI; every user-facing string lives in a pure seam, never typed in the scene (CLAUDE.md «Conventions»).
- New copy seams must be covered by `FontGlyphCoverageTests` (`SeamStrings()` type array at `Assets/Tests/Editor/FontGlyphCoverageTests.cs:295`).
- Apple Guideline 2.3.10: the iOS binary must not mention Google Play → the auto-renew disclosure takes the store name from a platform flag, never both at once.
- Scene is source of truth: additive builder only; re-stamp `PaywallController` refs via `SerializedObject`; save scene + commit immediately after the builder runs (parallel-scene-clobber rule).
- `LegalLinks.TermsUrl`/`PrivacyUrl` ship EMPTY until the owner provides the domain; empty ⇒ `LegalRow` hidden at runtime. Filling them is a submission blocker tracked in the checklist.
- Owner inputs still pending: final app name (docs use working «Automation», occurrences minimized) and the domain.

---

### Task 1: `LegalLinks` seam + `FinePrintText` + tests

**Files:**
- Create: `Assets/Scripts/Billing/LegalLinks.cs`
- Modify: `Assets/Scripts/Billing/PaywallRows.cs` (after `RestoreLabel`, line ~51)
- Create: `Assets/Tests/Editor/Billing/LegalLinksTests.cs`
- Modify: `Assets/Tests/Editor/Billing/PaywallRowsTests.cs` (append tests)
- Modify: `Assets/Tests/Editor/FontGlyphCoverageTests.cs` (type array line 295 + explicit yields)

**Interfaces:**
- Produces: `static class LegalLinks { const string TermsUrl; const string PrivacyUrl; const string TermsLabel = "Условия использования"; const string PrivacyLabel = "Политика конфиденциальности"; const string Separator = "·"; static bool HasUrls; }`
- Produces: `PaywallRows.FinePrintAutoRenewIos` / `FinePrintAutoRenewAndroid` consts + `static string FinePrintText(bool isTrialOffer, bool iosStore)`.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/Editor/Billing/LegalLinksTests.cs`:

```csharp
using NUnit.Framework;

/// <summary>
/// Pins the paywall legal-links seam (store submission pack). The URLs are empty until
/// the owner's domain lands; the shape tests keep whatever value arrives honest.
/// </summary>
public class LegalLinksTests
{
    [Test]
    public void Labels_are_nonempty_ru()
    {
        Assert.AreEqual("Условия использования", LegalLinks.TermsLabel);
        Assert.AreEqual("Политика конфиденциальности", LegalLinks.PrivacyLabel);
        Assert.AreEqual("·", LegalLinks.Separator);
    }

    [Test]
    public void Urls_are_empty_or_https_without_spaces()
    {
        foreach (var url in new[] { LegalLinks.TermsUrl, LegalLinks.PrivacyUrl })
        {
            if (string.IsNullOrEmpty(url)) continue;
            StringAssert.StartsWith("https://", url);
            Assert.IsFalse(url.Contains(" "), $"URL carries a space: '{url}'");
        }
    }

    [Test]
    public void HasUrls_requires_both()
    {
        // Both constants are either filled together or empty together — a half-filled
        // pair would render one dead link, which HasUrls exists to prevent.
        Assert.AreEqual(
            !string.IsNullOrEmpty(LegalLinks.TermsUrl) && !string.IsNullOrEmpty(LegalLinks.PrivacyUrl),
            LegalLinks.HasUrls);
        Assert.AreEqual(string.IsNullOrEmpty(LegalLinks.TermsUrl),
                        string.IsNullOrEmpty(LegalLinks.PrivacyUrl),
                        "TermsUrl and PrivacyUrl must be filled together");
    }
}
```

Append to `PaywallRowsTests.cs` (inside the class, after the existing fine-print-free tests):

```csharp
    // ── Fine print (store submission pack) ───────────────────────────────────

    [Test]
    public void FinePrint_trial_state_keeps_no_card_promise()
    {
        Assert.AreEqual("Без карты · Отмена в любой момент", PaywallRows.FinePrintText(true, true));
        Assert.AreEqual("Без карты · Отмена в любой момент", PaywallRows.FinePrintText(true, false));
    }

    [Test]
    public void FinePrint_subscribe_state_discloses_auto_renew_per_store()
    {
        Assert.AreEqual("Продлевается автоматически · отмена в настройках App Store",
            PaywallRows.FinePrintText(false, true));
        Assert.AreEqual("Продлевается автоматически · отмена в настройках Google Play",
            PaywallRows.FinePrintText(false, false));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Editor closed: `Tools/run-tests-headless.sh "LegalLinksTests|PaywallRowsTests"` → expect compile error (`LegalLinks` undefined). Editor open: drop empty `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`.

- [ ] **Step 3: Implement**

`Assets/Scripts/Billing/LegalLinks.cs`:

```csharp
/// <summary>
/// The paywall's legal links (store submission pack, spec 2026-08-27). Apple Guideline
/// 3.1.2 requires functional Privacy Policy + Terms of Use links next to the purchase
/// point; these constants are the ONE place the hosted URLs live.
///
/// Both URLs stay empty until the owner's domain is known — PaywallController hides the
/// whole LegalRow while <see cref="HasUrls"/> is false, so the app never renders a dead
/// link. Filling them (together) is a submission blocker tracked in
/// docs/store/submission-checklist.md.
/// </summary>
public static class LegalLinks
{
    public const string TermsUrl = "";
    public const string PrivacyUrl = "";

    public const string TermsLabel = "Условия использования";
    public const string PrivacyLabel = "Политика конфиденциальности";
    public const string Separator = "·";

    public static bool HasUrls =>
        !string.IsNullOrEmpty(TermsUrl) && !string.IsNullOrEmpty(PrivacyUrl);
}
```

`PaywallRows.cs` — directly under `public const string RestoreLabel = …`:

```csharp
    /// <summary>
    /// Auto-renew disclosure for the subscribe state (Apple Guideline 3.1.2: renewal terms
    /// must be clear at the purchase point; price + period already sit on the tier cards).
    /// Split per store because 2.3.10 forbids mentioning Google Play inside the iOS binary.
    /// The trial state keeps the original «Без карты» line — that CTA buys nothing.
    /// </summary>
    public const string FinePrintAutoRenewIos =
        "Продлевается автоматически · отмена в настройках App Store";
    public const string FinePrintAutoRenewAndroid =
        "Продлевается автоматически · отмена в настройках Google Play";

    public static string FinePrintText(bool isTrialOffer, bool iosStore)
        => isTrialOffer ? FinePrint
         : iosStore ? FinePrintAutoRenewIos
         : FinePrintAutoRenewAndroid;
```

`FontGlyphCoverageTests.cs`: add `typeof(LegalLinks)` to the type array on line 295 (covers the label consts and both new `PaywallRows` consts automatically via the existing reflection over `PaywallRows`); no explicit yields needed since `FinePrintText` only returns those consts.

- [ ] **Step 4: Run tests to verify they pass**

Same command; gate on `total > 0` and 0 failures. `FontGlyphCoverageTests` must also be in the filter of the final full run (Task 6).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Billing/LegalLinks.cs Assets/Scripts/Billing/LegalLinks.cs.meta Assets/Scripts/Billing/PaywallRows.cs Assets/Tests/Editor/Billing/LegalLinksTests.cs Assets/Tests/Editor/Billing/LegalLinksTests.cs.meta Assets/Tests/Editor/Billing/PaywallRowsTests.cs Assets/Tests/Editor/FontGlyphCoverageTests.cs
git commit -m "feat(billing): LegalLinks seam + per-store auto-renew fine print (store submission pack)"
```

*(.meta files appear after an Editor refresh — Unity new-file import rule; verify they exist before committing.)*

---

### Task 2: `PaywallController` renders the legal row

**Files:**
- Modify: `Assets/Scripts/Billing/PaywallController.cs` (fields after `restoreLabel:81`, wiring in `EnsureInit:220`, render in `Render:476`)

**Interfaces:**
- Consumes: `LegalLinks`, `PaywallRows.FinePrintText(bool,bool)` (Task 1).
- Produces: serialized fields `legalRow` (GameObject), `termsButton`/`privacyButton` (Button), `termsLabel`/`privacyLabel` (TextMeshProUGUI) — Task 3's builder stamps them by these exact names.

- [ ] **Step 1: Add fields** (after `restoreLabel`)

```csharp
    // Legal links row (store submission pack): hidden entirely until LegalLinks carries
    // real URLs, so a build made before the domain exists never shows a dead link.
    [SerializeField] private GameObject legalRow;
    [SerializeField] private Button termsButton;
    [SerializeField] private Button privacyButton;
    [SerializeField] private TextMeshProUGUI termsLabel;
    [SerializeField] private TextMeshProUGUI privacyLabel;
```

- [ ] **Step 2: Wire clicks in `EnsureInit`** (after the `restoreButton` line)

```csharp
        if (termsButton != null) termsButton.onClick.AddListener(() => OpenLegal(LegalLinks.TermsUrl));
        if (privacyButton != null) privacyButton.onClick.AddListener(() => OpenLegal(LegalLinks.PrivacyUrl));
```

and add next to `OnRestoreClicked`:

```csharp
    private static void OpenLegal(string url)
    {
        if (string.IsNullOrEmpty(url)) return;   // row is hidden in this state; belt-and-braces
        Application.OpenURL(url);
    }
```

- [ ] **Step 3: Render** — replace the fine-print line in `Render()` and append row state:

```csharp
        if (finePrint != null)
            finePrint.text = !string.IsNullOrEmpty(_notice) ? _notice
                : PaywallRows.FinePrintText(IsTrialOffer,
                    Application.platform == RuntimePlatform.IPhonePlayer);

        if (legalRow != null) legalRow.SetActive(LegalLinks.HasUrls);
        if (termsLabel != null) termsLabel.text = LegalLinks.TermsLabel;
        if (privacyLabel != null) privacyLabel.text = LegalLinks.PrivacyLabel;
```

- [ ] **Step 4: Compile check** (headless test run compiles everything; or Editor refresh) — no new EditMode test: the controller is a render-only MonoBehaviour reading pinned seams, same testing posture as the rest of the file.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Billing/PaywallController.cs
git commit -m "feat(billing): paywall renders legal links row + state-correct fine print"
```

---

### Task 3: Additive builder — `Tools/Billing/Add Paywall Legal Row`

**Files:**
- Modify: `Assets/Editor/PaywallBuilder.cs` (new MenuItem after `AddPurchaseButton:133`, new build method after `BuildSecondaryPurchase:652`, const after `SecondaryHeight:43`, scroll-padding bump in `EnsureScrollBottomPadding:668`)

**Interfaces:**
- Consumes: controller field names from Task 2; `LegalLinks` labels/`HasUrls`.
- Produces: scene node `Screen_Paywall/BottomBar/LegalRow` (last row, under Restore) + stamped controller refs; headless entry `PaywallBuilder.AddLegalRowHeadless`.

- [ ] **Step 1: Add const** (after `SecondaryHeight`)

```csharp
    // Legal links row (submission pack) — house touch floor, matching Restore above it.
    private const float LegalRowHeight = 120f;
```

- [ ] **Step 2: Add the additive entry + headless twin** (after `AddPurchaseButton`)

```csharp
    /// <summary>
    /// Additive, idempotent patch adding ONLY BottomBar/LegalRow («Условия использования ·
    /// Политика конфиденциальности», Apple 3.1.2) to the Screen_Paywall already in the open
    /// scene — same contract as <see cref="AddPurchaseButton"/>: never re-runs the
    /// destructive full Build over the owner's hand-tuned scene.
    /// </summary>
    [MenuItem("Tools/Billing/Add Paywall Legal Row")]
    public static void AddLegalRow()
    {
        LoadAssets();
        _roundedToRefresh.Clear();

        var screen = FindInactiveByName("Screen_Paywall");
        if (screen == null)
            throw new System.InvalidOperationException(
                "[PaywallBuilder] Screen_Paywall not found — is Main.unity open? Run Tools/Billing/Build Paywall first.");
        Transform bar = screen.transform.Find("BottomBar");
        if (bar == null)
            throw new System.InvalidOperationException(
                "[PaywallBuilder] Screen_Paywall/BottomBar not found — the scene has drifted from this builder.");

        DestroyAllByName(bar, "LegalRow");
        var row = BuildLegalRow(bar.gameObject,
            out Button terms, out Button privacy,
            out TextMeshProUGUI termsLabel, out TextMeshProUGUI privacyLabel);
        row.transform.SetAsLastSibling();   // below Restore: quietest row, furthest from the CTA

        EnsureBarAutoHeight(bar.gameObject);
        EnsureScrollBottomPadding(screen);

        var controller = screen.GetComponent<PaywallController>();
        if (controller == null)
            throw new System.InvalidOperationException("[PaywallBuilder] Screen_Paywall carries no PaywallController.");
        var so = new SerializedObject(controller);
        so.FindProperty("legalRow").objectReferenceValue = row;
        so.FindProperty("termsButton").objectReferenceValue = terms;
        so.FindProperty("privacyButton").objectReferenceValue = privacy;
        so.FindProperty("termsLabel").objectReferenceValue = termsLabel;
        so.FindProperty("privacyLabel").objectReferenceValue = privacyLabel;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);

        // Scene mirrors runtime: hidden while the URL constants are empty.
        row.SetActive(LegalLinks.HasUrls);

        Selection.activeGameObject = row;
        EditorSceneManager.MarkSceneDirty(screen.scene);
        Debug.Log("[PaywallBuilder] LegalRow added to Screen_Paywall/BottomBar + controller re-stamped. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . -executeMethod PaywallBuilder.AddLegalRowHeadless -quit
    public static void AddLegalRowHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        AddLegalRow();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PaywallBuilder] Headless AddLegalRow + save complete.");
    }
```

- [ ] **Step 3: Add the row construction** (after `BuildSecondaryPurchase`)

```csharp
    /// <summary>
    /// «Условия использования · Политика конфиденциальности» — two full-height link buttons
    /// around a separator dot, deliberately the quietest thing in the bar (InkTertiary 30
    /// regular + underline): utility links, not actions. Each button's targetGraphic is its
    /// LABEL (the hit Image is alpha-0 — ColorTint on it would show nothing), Restore's own
    /// pattern one row up.
    /// </summary>
    private static GameObject BuildLegalRow(GameObject bar, out Button terms, out Button privacy,
        out TextMeshProUGUI termsLabel, out TextMeshProUGUI privacyLabel)
    {
        var row = NewChild(bar, "LegalRow", out _);
        SetPreferredHeight(row, LegalRowHeight);
        var group = row.AddComponent<HorizontalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleCenter;
        group.spacing = 20f;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = true;

        terms = BuildLegalLink(row, "Terms", LegalLinks.TermsLabel, out termsLabel);

        var dotGo = NewChild(row, "Dot", out _);
        var dot = AddText(dotGo, LegalLinks.Separator, 30f, _regular, ThemeRole.InkTertiary);
        dot.alignment = TextAlignmentOptions.Center;

        privacy = BuildLegalLink(row, "Privacy", LegalLinks.PrivacyLabel, out privacyLabel);
        return row;
    }

    private static Button BuildLegalLink(GameObject row, string name, string text,
        out TextMeshProUGUI label)
    {
        var go = NewChild(row, name, out _);
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = go.AddComponent<Button>();
        var labelGo = NewChild(go, "Label", out var labelRt);
        StretchFill(labelRt);
        label = AddText(labelGo, text, 30f, _regular, ThemeRole.InkTertiary);
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Underline;
        button.targetGraphic = label;
        button.transition = Selectable.Transition.ColorTint;
        return go.GetComponent<Button>();
    }
```

Note: the links' width comes from the labels via `childControlWidth = true` (TMP reports preferred width); the alpha-0 hit images stretch to the row's full 120u height via `childForceExpandHeight`, so the touch target is the full row band around each label.

- [ ] **Step 4: Bump scroll clearance** — in `EnsureScrollBottomPadding` change:

```csharp
        int wanted = (int)(BottomBarHeightMax + LegalRowHeight + BarSpacing + 48f);
```

- [ ] **Step 5: Run the builder** (Editor open → menu/`mcp-unity execute_menu_item`; closed → the headless command above), then verify by artifact: `grep -c "LegalRow" Assets/Scenes/Main.unity` ≥ 1 and controller refs non-null in the scene YAML.

- [ ] **Step 6: Commit code + scene immediately**

```bash
git add Assets/Editor/PaywallBuilder.cs Assets/Scenes/Main.unity
git commit -m "feat(billing): additive LegalRow builder + scene (paywall legal links)"
```

---

### Task 4: Legal documents (RU, hostable as-is)

**Files:**
- Create: `docs/legal/privacy.html`, `docs/legal/terms.html`

Content contract (both: self-contained HTML, inline CSS, mobile-first, RU; app name «Automation» appears once in the title and once in the preamble definition «(далее — „Приложение")»; operator contact `synergyexpertgroup@gmail.com`; operator legal-entity line marked for the owner to fill before hosting):

- **privacy.html:** данные и потоки как в спеке §4 (Wappi транспорт, n8n+OpenAI генерация/классификация, Supabase хранение, RevenueCat анонимный ID; нет рекламы/трекинга/продажи данных); права пользователя (удаление в приложении «Удалить все данные» + по e-mail); ответственность пользователя за согласие его клиентов; дата вступления.
- **terms.html:** подписки через сторы + автопродление/отмена + триал 5 дней; неофициальные интеграции WhatsApp/Telegram через Wappi и риск ограничений аккаунта; допустимое использование (запрет спама); отказ от гарантий; изменения условий; контакт.

- [ ] Write both files, re-read for internal consistency (no feature promised that the launched app won't have), commit:

```bash
git add docs/legal/
git commit -m "docs(store): RU privacy policy + terms drafts for hosting"
```

---

### Task 5: Store submission kit

**Files:**
- Create: `docs/store/submission-checklist.md`, `docs/store/app-review-notes.md`, `docs/store/demo-video-script.md`

Content contract:

- **submission-checklist.md:** красные блокеры (домен вписан в `LegalLinks` + билдер перезапущен; имя финальное; юр-страницы живые; скриншоты; демо-видео); App Store Connect по шагам — privacy labels с готовыми ответами (Data Used: User Content [messages, photos/docs] — app functionality, not linked to identity, no tracking; Purchases — RevenueCat anonymous id), Privacy Policy URL, subscription group + 7 SKU сверка (заведены Блоком 1), Terms (custom URL), review notes + видео-приложение, manual release; Play Console по шагам — Data Safety те же ответы, IARC, **личный аккаунт после ноября 2023 → обязательный closed test (≈12+ тестеров, 14 дней) — проверить и стартовать немедленно**; Apple Small Business Program (после активации аккаунта; спека Блока 1 §10.3); напоминание Guideline 2.3.10 (в iOS-сборке и метаданных не упоминать Android/Google Play); PrivacyInfo.xcprivacy — сверить манифесты Unity 6 / RevenueCat / yasirkula-плагинов при первом Xcode-экспорте.
- **app-review-notes.md:** EN; what the app is (AI auto-replies for the owner's OWN WhatsApp/Telegram via connected profiles), why the reviewer cannot self-auth (needs a live WhatsApp number + second device → see demo video), how to exercise trial/paywall/sandbox purchase/restore without auth, note that the 5-day trial is app-level (no payment solicited outside IAP; subscriptions are standard auto-renewable IAP).
- **demo-video-script.md:** 90-сек сценарий: создание бота → QR-auth → входящее сообщение клиента → автоответ → «Вместе» → Сводка → пейволл/покупка.

- [ ] Write all three, commit:

```bash
git add docs/store/
git commit -m "docs(store): submission checklist + Apple review notes + demo script"
```

---

### Task 6: Full verification

- [ ] Run the full EditMode suite (bridge if Editor open, else `Tools/run-tests-headless.sh` with no filter); gate on `total > 0`, 0 failures — `FontGlyphCoverageTests` sweeps the new copy against SF Pro here.
- [ ] Verify the scene artifact: `LegalRow` node present, `legalRow`/`termsButton`/`privacyButton`/`termsLabel`/`privacyLabel` stamped (grep the scene YAML by the controller's fileID block).
- [ ] Commit anything remaining; report: what the owner must supply (domain → fill `LegalLinks` + rerun `Add Paywall Legal Row`; final name; hosting upload; console actions from the checklist).
