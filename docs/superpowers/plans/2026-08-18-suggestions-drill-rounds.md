# Suggestions Drill Rounds + Free-Form Titles — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The «Вместе» panel fills all 4 cards usefully on concrete questions, and after a pick every next round drills strictly into the picked direction (varying length/tone/format), with the picked card's title replacing the «ПРЕДЛОЖЕНИЯ» header.

**Architecture:** Approach B from the spec (`docs/superpowers/specs/2026-08-18-suggestions-drill-rounds-design.md`): each suggestion becomes `{text, label, move}` — `move` keeps the closed 6-enum internally (niche prompts, pickStats, strict validation; repeats allowed), `label` becomes a free-form RU display title. The request wire contract does NOT change. Client threads a per-round header through the round stack; the server prompt gains explore-vs-drill mode rules.

**Tech Stack:** Unity 6 C# (EditMode NUnit tests), n8n canonical workflow JSON (`Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`), Python probe harness.

## Global Constraints

- **Frozen request contract:** never touch `SuggestRepliesRequestDto`, `BuildPayloadJson`'s emitted keys, or anything `SuggestRepliesPayloadTests` pins. Response side only.
- **Do NOT modify `Assets/Editor/SuggestionsPanelBuilder.cs`** — it carries uncommitted parallel-session work. Scene wiring goes through the new one-shot wirer only. Never run destructive rebuilders.
- **Marker blocks stay byte-identical** in the Assemble node: `PANEL-PROMPTS-BEGIN/END` and `PANEL-NICHE-PUSH-BEGIN/END`, plus the «РЕЛЕВАНТНОСТЬ (ГЛАВНОЕ)» anchor (the parity gate asserts it).
- **RU strings verbatim:** every Russian string in this plan is exact copy — do not paraphrase, re-punctuate, or "fix" ё/quotes.
- **Unity test runs:** Editor closed → `Tools/run-tests-headless.sh '<regex>'`; Editor open → mcp-unity `run_tests` with the EXACT test-class filter (substring filters silently match nothing → false 0/0 green). Always judge on the reported `total` — a 0-test run is a failure, not a pass.
- **New .cs files:** Unity must import them before commit (Editor open: `Assets/Refresh` menu; closed: any headless test run imports). Stage each `.cs` together with its generated `.meta`.
- **Commit `Assets/Scenes/Main.unity` IMMEDIATELY after the wirer runs** (parallel sessions clobber uncommitted scene edits).
- **n8n:** dev instance only (`http://localhost:5678`); prod is dormant and untouched. Canonical JSON is the single source of truth; deploy via `build-suggest-replies.py --update`.

---

### Task 1: `move` rides the response wire — SuggestionMoves seam + DTO + mapper

**Files:**
- Create: `Assets/Scripts/Chat/SuggestionMoves.cs`
- Create: `Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs`
- Modify: `Assets/Scripts/Chat/SuggestRepliesDtos.cs` (class `SuggestReplyDto`, ~line 60)
- Modify: `Assets/Scripts/Chat/SuggestionItem.cs`
- Modify: `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` (`MoveLabels` ~line 163, `BuildPickStats` ~line 170, `MapResponse` select ~line 317)
- Test: `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs` (append 2 tests)

**Interfaces:**
- Produces: `SuggestionMoves.All : string[]` (6 RU moves), `SuggestionMoves.IsMove(string) : bool` (exact, case-sensitive), `SuggestReplyDto.move : string`, `SuggestionItem.move : string` (null tolerated), `MapResponse` copies `move` through.

- [ ] **Step 1: Write the failing tests**

Append to `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs` (inside the class, at the end):

```csharp
    // --- v1.3 drill redesign: the internal move field rides the mapper ---

    [Test]
    public void MapResponse_CarriesTheMoveField()
    {
        string json = "{\"v\":1,\"requestSeq\":7,\"error\":\"\",\"abstain\":false," +
            "\"suggestions\":[{\"text\":\"Букет 25 роз — 25000 тг\",\"label\":\"Цена\",\"move\":\"Ответ\"}]}";
        var r = N8nSuggestionsProvider.MapResponse(json, 7);
        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.AreEqual("Ответ", r.items[0].move);
        Assert.AreEqual("Цена", r.items[0].intentLabel);
    }

    [Test]
    public void MapResponse_ToleratesALegacyServerWithoutMove()
    {
        string json = "{\"v\":1,\"requestSeq\":7,\"error\":\"\",\"abstain\":false," +
            "\"suggestions\":[{\"text\":\"Здравствуйте!\",\"label\":\"Ответ\"}]}";
        var r = N8nSuggestionsProvider.MapResponse(json, 7);
        Assert.AreEqual(SuggestionStatus.Ok, r.status);
        Assert.IsNull(r.items[0].move);
    }
```

Create `Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs`:

```csharp
using NUnit.Framework;

// EditMode coverage for the 2026-08-18 drill-rounds redesign seams. Grows across the
// rollout tasks: SuggestionMoves here, ComposeHeaderTitle (panel) and
// ResolvePickStatsMove (controller) appended by their own tasks.
public class SuggestionDrillRoundsTests
{
    [Test]
    public void IsMove_AcceptsAllSixMoves()
    {
        Assert.AreEqual(6, SuggestionMoves.All.Length);
        foreach (string move in SuggestionMoves.All)
            Assert.IsTrue(SuggestionMoves.IsMove(move), move);
    }

    [Test]
    public void IsMove_RejectsNullEmptyFreeFormAndWrongCase()
    {
        Assert.IsFalse(SuggestionMoves.IsMove(null));
        Assert.IsFalse(SuggestionMoves.IsMove(""));
        Assert.IsFalse(SuggestionMoves.IsMove("Цена"));      // free-form title, not a move
        Assert.IsFalse(SuggestionMoves.IsMove("ответ"));     // case-sensitive: server enum is exact
    }
}
```

- [ ] **Step 2: Verify the red state**

New symbols don't exist yet, so the failure manifests as a compile error.
Editor open: mcp-unity `recompile_scripts` → expect errors `CS0246: SuggestionMoves not found` and `'SuggestionItem' does not contain a definition for 'move'`.
Editor closed: `Tools/run-tests-headless.sh 'SuggestionDrillRoundsTests'` → expect the run to abort on compilation errors naming the same symbols.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Chat/SuggestionMoves.cs`:

```csharp
/// <summary>
/// The closed move taxonomy of the Suggest Replies contract — since the 2026-08-18 drill
/// redesign an INTERNAL classification (the response's <c>move</c> field), no longer the
/// display label. Shared by pickStats preference learning (PlayerPrefs key suffixes) and
/// the pick-resolution fallback; values mirror the server Validate enum verbatim — do NOT
/// localize, reorder, or add entries without changing the workflow first.
/// </summary>
public static class SuggestionMoves
{
    public static readonly string[] All =
        { "Ответ", "Уточнить", "Вариант", "К заказу", "Отложить", "Отказ" };

    /// <summary>Exact, case-sensitive membership — the server enum is exact.</summary>
    public static bool IsMove(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < All.Length; i++)
            if (All[i] == value) return true;
        return false;
    }
}
```

In `Assets/Scripts/Chat/SuggestRepliesDtos.cs`, replace the `SuggestReplyDto` class (keep its summary line style):

```csharp
/// <summary>One suggestion in the response envelope: server sends {text,label,move}.
/// <c>move</c> is v1.3-additive (drill redesign 2026-08-18) — the internal 6-enum move;
/// a legacy server omits it, so null must be tolerated end-to-end.</summary>
[System.Serializable]
public class SuggestReplyDto
{
    public string text;
    public string label;
    public string move;   // v1.3 additive — internal move taxonomy; null/"" from a legacy server
}
```

In `Assets/Scripts/Chat/SuggestionItem.cs`, replace the class body:

```csharp
public class SuggestionItem
{
    public string text;
    public string intentLabel;
    public string move;   // internal 6-enum move (v1.3); null from a legacy server — never displayed
}
```

In `Assets/Scripts/Chat/N8nSuggestionsProvider.cs`:

1. Delete the private `MoveLabels` array (lines ~163–165, including its comment) and change `BuildPickStats`'s loop header from `foreach (string label in MoveLabels)` to `foreach (string label in SuggestionMoves.All)`. Update the comment above `BuildPickStats` to end with: `Counts the closed move taxonomy (SuggestionMoves) — free-form titles never mint keys.`
2. In `MapResponse`, change the select line to carry the move:

```csharp
            .Select(s => new SuggestionItem { text = s.text, intentLabel = s.label, move = s.move })   // {text,label,move} -> item
```

- [ ] **Step 4: Run the task's tests**

Editor closed: `Tools/run-tests-headless.sh 'SuggestRepliesMapTests|SuggestionDrillRoundsTests'`
Editor open: mcp-unity `run_tests` with class filter `SuggestRepliesMapTests`, then `SuggestionDrillRoundsTests`.
Expected: PASS, total ≥ 15 across the two classes, failed 0.

- [ ] **Step 5: Import + commit**

Ensure `.meta` files exist for both new files (headless run generated them; Editor path: `Assets/Refresh`). Then:

```bash
git add Assets/Scripts/Chat/SuggestionMoves.cs Assets/Scripts/Chat/SuggestionMoves.cs.meta \
        Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs.meta \
        Assets/Scripts/Chat/SuggestRepliesDtos.cs Assets/Scripts/Chat/SuggestionItem.cs \
        Assets/Scripts/Chat/N8nSuggestionsProvider.cs Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs
git commit -m "feat(suggestions): carry the internal move field through the reply wire contract"
```

---

### Task 2: Round stack remembers each round's header

**Files:**
- Modify: `Assets/Scripts/Chat/SuggestionRoundStack.cs`
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs` (call sites only: `HandleCardTapped` ~line 297, `HandleBack` ~line 311; new `_currentHeader` field beside `_currentSteer` ~line 72)
- Test: `Assets/Tests/Editor/Chat/SuggestionRoundStackTests.cs` (full replacement below)

**Interfaces:**
- Consumes: nothing new.
- Produces: `SuggestionRoundStack.Push(SuggestionResult result, string steer, string header)`, `SuggestionRoundStack.TryPop(out SuggestionResult result, out string steer, out string header)`, controller field `private string _currentHeader` (null = default header). Task 4 wires the behavior; this task only keeps everything compiling with the header threaded through.

- [ ] **Step 1: Write the failing tests — replace `SuggestionRoundStackTests.cs` entirely**

```csharp
using System.Collections.Generic;
using NUnit.Framework;

// EditMode coverage for SuggestionRoundStack — the pure history behind the suggestions
// back button (flow decision 2026-08-11; header added by the 2026-08-18 drill redesign:
// each round remembers the display title it was shown under, so ‹ restores cards AND
// header with no LLM call). Pins LIFO order, the null-render no-op, and the depth cap.
public class SuggestionRoundStackTests
{
    private static SuggestionResult Set(string text)
        => new SuggestionResult
        {
            status = SuggestionStatus.Ok,
            requestSeq = 1,
            items = new List<SuggestionItem> { new SuggestionItem { text = text, intentLabel = "Ответ" } }
        };

    [Test]
    public void Empty_CannotGoBack()
    {
        var stack = new SuggestionRoundStack();
        Assert.IsFalse(stack.CanGoBack);
        Assert.IsFalse(stack.TryPop(out _, out _, out _));
    }

    [Test]
    public void PushNullResult_IsNoOp()
    {
        // A pick can land while nothing is rendered (skeleton) — there is no round to return to.
        var stack = new SuggestionRoundStack();
        stack.Push(null, "направление", "ЦЕНА");
        Assert.IsFalse(stack.CanGoBack);
    }

    [Test]
    public void PushThenPop_RestoresResultSteerAndHeader()
    {
        var stack = new SuggestionRoundStack();
        var round1 = Set("раунд 1");
        stack.Push(round1, null, null);   // round 1: fresh set under the default header
        Assert.IsTrue(stack.CanGoBack);
        Assert.IsTrue(stack.TryPop(out var result, out var steer, out var header));
        Assert.AreSame(round1, result);
        Assert.IsNull(steer);
        Assert.IsNull(header);
        Assert.IsFalse(stack.CanGoBack);
    }

    [Test]
    public void Pop_IsLifo_DeeperRoundsComeBackFirst()
    {
        var stack = new SuggestionRoundStack();
        stack.Push(Set("раунд 1"), null, null);
        stack.Push(Set("раунд 2"), "направление А", "Цена");
        Assert.IsTrue(stack.TryPop(out var second, out var secondSteer, out var secondHeader));
        Assert.AreEqual("раунд 2", second.items[0].text);
        Assert.AreEqual("направление А", secondSteer);
        Assert.AreEqual("Цена", secondHeader);
        Assert.IsTrue(stack.TryPop(out var first, out var firstSteer, out var firstHeader));
        Assert.AreEqual("раунд 1", first.items[0].text);
        Assert.IsNull(firstSteer);
        Assert.IsNull(firstHeader);
    }

    [Test]
    public void DepthCap_DropsTheOldestRound()
    {
        var stack = new SuggestionRoundStack();
        for (int i = 1; i <= SuggestionRoundStack.MaxDepth + 1; i++)
            stack.Push(Set("раунд " + i), "s" + i, "h" + i);
        Assert.AreEqual(SuggestionRoundStack.MaxDepth, stack.Count);
        // Pop everything — the deepest restorable round is 2 (round 1 was dropped).
        SuggestionResult last = null;
        while (stack.TryPop(out var r, out _, out _)) last = r;
        Assert.AreEqual("раунд 2", last.items[0].text);
    }

    [Test]
    public void Clear_DropsEverything()
    {
        var stack = new SuggestionRoundStack();
        stack.Push(Set("раунд 1"), null, null);
        stack.Push(Set("раунд 2"), "x", "y");
        stack.Clear();
        Assert.IsFalse(stack.CanGoBack);
        Assert.AreEqual(0, stack.Count);
    }
}
```

- [ ] **Step 2: Verify the red state**

Recompile (mcp-unity `recompile_scripts` or headless run) → expect `No overload for method 'Push' takes 3 arguments` / `'TryPop'` argument-count errors.

- [ ] **Step 3: Implement**

In `Assets/Scripts/Chat/SuggestionRoundStack.cs`, replace the tuple list and both methods:

```csharp
    /// <summary>Retained rounds. Real sessions go 2–4 deep; the cap only bounds memory —
    /// overflow drops the OLDEST round, so back still walks the recent path.</summary>
    public const int MaxDepth = 8;

    private readonly List<(SuggestionResult result, string steer, string header)> _rounds = new();

    public int Count => _rounds.Count;
    public bool CanGoBack => _rounds.Count > 0;

    /// <summary>Record the round being left: its cards, the steer that PRODUCED it, and the
    /// display header it was shown under (null = the default «ПРЕДЛОЖЕНИЯ» overline). A null
    /// <paramref name="result"/> is a no-op — a pick that lands while nothing is rendered
    /// has no round to return to.</summary>
    public void Push(SuggestionResult result, string steer, string header)
    {
        if (result == null) return;
        if (_rounds.Count == MaxDepth) _rounds.RemoveAt(0);
        _rounds.Add((result, steer, header));
    }

    /// <summary>LIFO restore of the most recent round, the steer that produced it (null =
    /// fresh set — a refresh after back re-rolls the right direction) and its header.</summary>
    public bool TryPop(out SuggestionResult result, out string steer, out string header)
    {
        result = null;
        steer = null;
        header = null;
        if (_rounds.Count == 0) return false;
        (result, steer, header) = _rounds[_rounds.Count - 1];
        _rounds.RemoveAt(_rounds.Count - 1);
        return true;
    }
```

In `Assets/Scripts/Chat/SuggestionsController.cs` — three mechanical edits so the project compiles (behavior lands in Task 4):

1. Below `private string _currentSteer;` add:

```csharp
    private string _currentHeader;   // display title of the round ON SCREEN (null = default «ПРЕДЛОЖЕНИЯ»)
```

2. In `HandleCardTapped`, change the push line to:

```csharp
        _rounds.Push(_currentRendered, _currentSteer, _currentHeader);
```

3. In `HandleBack`, change the pop + restore block to:

```csharp
        if (!_rounds.TryPop(out SuggestionResult previous, out string previousSteer, out string previousHeader)) return;
        _requestSeq++;
        _currentSteer = previousSteer;
        _currentHeader = previousHeader;
        _currentRendered = previous;
```

- [ ] **Step 4: Run the task's tests**

`Tools/run-tests-headless.sh 'SuggestionRoundStackTests'` (or mcp-unity `run_tests`, class filter `SuggestionRoundStackTests`).
Expected: PASS, total 6, failed 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionRoundStack.cs Assets/Scripts/Chat/SuggestionsController.cs \
        Assets/Tests/Editor/Chat/SuggestionRoundStackTests.cs
git commit -m "feat(suggestions): round stack remembers each round's header"
```

---

### Task 3: Panel header API — drill rounds retitle the «ПРЕДЛОЖЕНИЯ» overline

**Files:**
- Modify: `Assets/Scripts/UI/SuggestionsPanel.cs` (serialized fields block ~line 33, new members after `SetBackVisible` ~line 201)
- Test: `Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs` (append 3 tests)

**Interfaces:**
- Produces: `SuggestionsPanel.DefaultHeaderTitle : const string` (`"ПРЕДЛОЖЕНИЯ"`), `SuggestionsPanel.SetHeaderTitle(string)` (null/empty → default), `SuggestionsPanel.ComposeHeaderTitle(string) : string` (pure, testable), serialized field `headerTitle` (wired in Task 6 — until then `SetHeaderTitle` no-ops safely on null).

- [ ] **Step 1: Write the failing tests — append to `SuggestionDrillRoundsTests.cs`**

```csharp
    // --- ComposeHeaderTitle (panel header, pure) ---

    [Test]
    public void ComposeHeaderTitle_NullOrBlank_IsTheDefaultOverline()
    {
        Assert.AreEqual(SuggestionsPanel.DefaultHeaderTitle, SuggestionsPanel.ComposeHeaderTitle(null));
        Assert.AreEqual(SuggestionsPanel.DefaultHeaderTitle, SuggestionsPanel.ComposeHeaderTitle("   "));
    }

    [Test]
    public void ComposeHeaderTitle_UppercasesCyrillicAndTrims()
    {
        Assert.AreEqual("ЦЕНА", SuggestionsPanel.ComposeHeaderTitle(" Цена "));
        Assert.AreEqual("СО СКИДКОЙ", SuggestionsPanel.ComposeHeaderTitle("Со скидкой"));
    }

    [Test]
    public void ComposeHeaderTitle_SlicesARoguePayload()
    {
        string composed = SuggestionsPanel.ComposeHeaderTitle(new string('ы', 40));
        Assert.AreEqual(26, composed.Length);
        StringAssert.EndsWith("…", composed);
    }
```

- [ ] **Step 2: Verify the red state**

Recompile → expect `'SuggestionsPanel' does not contain a definition for 'ComposeHeaderTitle'` (+ `DefaultHeaderTitle`).

- [ ] **Step 3: Implement**

In `Assets/Scripts/UI/SuggestionsPanel.cs`:

1. Add `using TMPro;` to the usings.
2. In the serialized-fields block, after the `bottomFade` field, add:

```csharp
    [SerializeField] private TextMeshProUGUI headerTitle;  // «ПРЕДЛОЖЕНИЯ» overline; drill rounds retitle it (wired by Tools/Suggestions/Wire Header Title)
```

3. After `SetBackVisible`, add:

```csharp
    /// <summary>The header overline's rest text — round 1 and every fresh round.</summary>
    public const string DefaultHeaderTitle = "ПРЕДЛОЖЕНИЯ";

    // Validate clamps titles to 24 server-side; the slice only guards a rogue payload.
    private const int HeaderTitleMaxChars = 26;

    /// <summary>Round header (drill flow 2026-08-18): null/empty restores the default
    /// overline; a drill round shows the picked card's title. Uppercased HERE because the
    /// scene TMP carries no uppercase FontStyle — the composed string IS the display string.</summary>
    public void SetHeaderTitle(string title)
    {
        if (headerTitle != null) headerTitle.text = ComposeHeaderTitle(title);
    }

    /// <summary>Pure composition seam for <see cref="SetHeaderTitle"/> — EditMode-tested.</summary>
    public static string ComposeHeaderTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return DefaultHeaderTitle;
        string value = title.Trim().ToUpperInvariant();
        return value.Length <= HeaderTitleMaxChars
            ? value
            : value.Substring(0, HeaderTitleMaxChars - 1) + "…";
    }
```

- [ ] **Step 4: Run the task's tests**

`Tools/run-tests-headless.sh 'SuggestionDrillRoundsTests'` (or mcp-unity `run_tests`, class filter `SuggestionDrillRoundsTests`).
Expected: PASS, total 5 (2 from Task 1 + 3 new), failed 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/SuggestionsPanel.cs Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs
git commit -m "feat(suggestions): panel header API — drill rounds retitle the «ПРЕДЛОЖЕНИЯ» overline"
```

---

### Task 4: Controller — pick retitles the header, pickStats counts moves, cache keeps only fresh rounds

**Files:**
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs` (`HandleCardTapped` ~line 291, `HandleBack` ~line 311, `RecordPick` ~line 325, `StartFreshRound` ~line 341, `IssueRequest` ~line 251, `OnResult` ~line 272)
- Test: `Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs` (append 3 tests)

**Interfaces:**
- Consumes: `SuggestionMoves.IsMove` (Task 1), `SuggestionRoundStack.Push/TryPop` triples (Task 2), `SuggestionsPanel.SetHeaderTitle` (Task 3), `SuggestionItem.move` (Task 1).
- Produces: `SuggestionsController.ResolvePickStatsMove(SuggestionItem) : string` (public static, pure).

- [ ] **Step 1: Write the failing tests — append to `SuggestionDrillRoundsTests.cs`**

```csharp
    // --- ResolvePickStatsMove (preference learning under free-form titles) ---

    [Test]
    public void ResolvePickStats_PrefersTheMoveField()
    {
        var picked = new SuggestionItem { text = "т", intentLabel = "Коротко", move = "Ответ" };
        Assert.AreEqual("Ответ", SuggestionsController.ResolvePickStatsMove(picked));
    }

    [Test]
    public void ResolvePickStats_LegacyServer_FallsBackToAnEnumLabel()
    {
        var picked = new SuggestionItem { text = "т", intentLabel = "К заказу", move = null };
        Assert.AreEqual("К заказу", SuggestionsController.ResolvePickStatsMove(picked));
    }

    [Test]
    public void ResolvePickStats_FreeFormTitleWithoutMove_RecordsNothing()
    {
        var picked = new SuggestionItem { text = "т", intentLabel = "Со скидкой", move = "" };
        Assert.IsNull(SuggestionsController.ResolvePickStatsMove(picked));
        Assert.IsNull(SuggestionsController.ResolvePickStatsMove(null));
    }
```

- [ ] **Step 2: Verify the red state**

Recompile → expect `'SuggestionsController' does not contain a definition for 'ResolvePickStatsMove'`.

- [ ] **Step 3: Implement — five edits in `SuggestionsController.cs`**

1. Replace `HandleCardTapped` entirely:

```csharp
    private void HandleCardTapped(string replyText)
    {
        if (_bottomPanel != null && _bottomPanel.inputField != null)
            StartCoroutine(WriteComposerRoutine(_bottomPanel.inputField, replyText));
        // Rounds flow: record the round being left (cards + steer + header) so ‹ restores it
        // locally, remember the new direction for refresh re-rolls, retitle the header to the
        // picked card's title (drill flow 2026-08-18), and count the pick's MOVE.
        SuggestionItem picked = FindRenderedItem(replyText);
        _rounds.Push(_currentRendered, _currentSteer, _currentHeader);
        _currentSteer = replyText;
        if (picked != null) _currentHeader = picked.intentLabel;
        if (_panel != null) _panel.SetHeaderTitle(_currentHeader);
        RecordPick(picked);
        UpdateBackUi();
        IssueRequest(steerTowardText: replyText, lastIncomingText: null);   // next round drills into the pick (INT-04/D-01)
        // NEVER auto-send — only the existing composer Send button delivers a message (D-03).
        // The sheet stays open on a pick; it hides on the OUTGOING echo (flow decision 2026-08-11).
    }

    // The tap event carries only the text; texts within one set are distinct by generation.
    private SuggestionItem FindRenderedItem(string replyText)
    {
        if (_currentRendered?.items == null) return null;
        foreach (var item in _currentRendered.items)
            if (item != null && item.text == replyText) return item;
        return null;
    }
```

2. In `HandleBack` (already popping the triple since Task 2), extend the render block so the header restores with the cards:

```csharp
        if (_panel != null)
        {
            _panel.Render(previous);
            _panel.SetHeaderTitle(previousHeader);
        }
        UpdateBackUi();
```

3. Replace `RecordPick` entirely:

```csharp
    // Preference learning v1 under the drill redesign: count the picked card's internal MOVE
    // (server field since 2026-08-18); a legacy server without `move` still counts when the
    // display label IS one of the 6 moves (the pre-redesign contract). Free-form titles never
    // mint PlayerPrefs keys — the counter namespace must stay the closed taxonomy.
    private void RecordPick(SuggestionItem picked)
    {
        if (ChatManager.Instance == null) return;
        string botName = ChatManager.Instance.CurrentBotId;
        string move = ResolvePickStatsMove(picked);
        if (string.IsNullOrEmpty(botName) || move == null) return;
        string key = botName + "SuggestPick" + move;
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence)
    }

    /// <summary>Pure pick-stats resolution: the item's move when valid, else its label if
    /// that label IS a move (legacy server), else null (record nothing). EditMode-tested.</summary>
    public static string ResolvePickStatsMove(SuggestionItem picked)
    {
        if (picked == null) return null;
        if (SuggestionMoves.IsMove(picked.move)) return picked.move;
        if (SuggestionMoves.IsMove(picked.intentLabel)) return picked.intentLabel;
        return null;
    }
```

4. Replace `StartFreshRound`:

```csharp
    // A fresh round 1: new incoming, chat/bot switch, explicit toggle-on, answered run.
    private void StartFreshRound()
    {
        _rounds.Clear();
        _currentSteer = null;
        _currentRendered = null;   // a pick on a cache-restored set must not push the PREVIOUS chat's round
        _currentHeader = null;
        if (_panel != null) _panel.SetHeaderTitle(null);   // back to the default «ПРЕДЛОЖЕНИЯ»
        UpdateBackUi();
    }
```

5. Cache stores fresh rounds only. In `IssueRequest`, after `string tailKey = CurrentTailKey();` change the request line to thread the flag:

```csharp
        bool freshSet = steerTowardText == null;   // only round-1 sets are cache-worthy
        _provider.Request(req, result => OnResult(seq, chatId, tailKey, freshSet, result));
```

   And in `OnResult`, change the signature and the store guard:

```csharp
    private void OnResult(long seq, string capturedChatId, string capturedTailKey, bool freshSet, SuggestionResult result)
```

```csharp
        // F9 verify-at-store, narrowed by the drill flow (2026-08-18): only FRESH sets are
        // cached — a re-opened chat must render a round-1 set under the default header, never
        // a mid-drill set whose steer/back context is gone. Tail drift still degrades to a
        // cache miss, never to stale cards.
        if (freshSet && capturedTailKey != null && capturedTailKey == CurrentTailKey())
            _cache.Store(capturedChatId, capturedTailKey, result);
```

- [ ] **Step 4: Run the task's tests**

`Tools/run-tests-headless.sh 'SuggestionDrillRoundsTests'` (or mcp-unity `run_tests`, class filter `SuggestionDrillRoundsTests`).
Expected: PASS, total 8, failed 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionsController.cs Assets/Tests/Editor/Chat/SuggestionDrillRoundsTests.cs
git commit -m "feat(suggestions): pick retitles the header, pickStats counts moves, cache keeps fresh rounds only"
```

---

### Task 5: Mock provider parity — moves + variation titles

**Files:**
- Modify: `Assets/Scripts/Chat/MockSuggestionsProvider.cs` (label consts ~line 21, `BuildFreshSet`/`BuildSteeredSet`/`Item` ~lines 91–114)
- Test: `Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs`

**Interfaces:**
- Consumes: `SuggestionItem.move` (Task 1), `SuggestionMoves.IsMove` (Task 1).
- Produces: nothing new — editor-parity data only.

- [ ] **Step 1: Update the tests first**

In `MockSuggestionsProviderTests.cs`:

1. Replace the `Labels` set and its comment:

```csharp
    // Fresh-set topic titles + steered-set variation titles (drill redesign 2026-08-18:
    // labels are free-form display titles; the closed taxonomy moved to item.move).
    private static readonly HashSet<string> Labels = new HashSet<string>
    {
        "Приветствие", "Цена", "Наличие", "Запись",                       // fresh (explore) topics
        "Со следующим шагом", "С вопросом", "Коротко", "Вежливый отказ"   // steered (drill) variations
    };
```

2. Replace `EveryItem_HasIntentLabelFromTheRussianSet` with a both-sets version plus a move check:

```csharp
    [Test]
    public void EveryItem_HasATitleFromTheRussianSet_AndAValidMove()
    {
        var all = new List<SuggestionItem>();
        all.AddRange(_provider.BuildResult(Req()).items);
        all.AddRange(_provider.BuildResult(Req(steer: "любой выбранный текст")).items);
        foreach (var item in all)
        {
            Assert.IsTrue(Labels.Contains(item.intentLabel), $"Unexpected title: {item.intentLabel}");
            Assert.IsTrue(SuggestionMoves.IsMove(item.move), $"Invalid move: {item.move}");
        }
    }
```

- [ ] **Step 2: Verify the red state**

Recompile/run `MockSuggestionsProviderTests` → expect failures: `Item` has no move yet (`Invalid move: ` assertion) — or a compile error if the test file referenced a helper first. Either red is fine; note which.

- [ ] **Step 3: Implement**

In `MockSuggestionsProvider.cs`, replace the two set builders and `Item` (drop the now-unused `LabelStock`? No — it stays used in the fresh set; only `LabelDecline` remains used in the steered set):

```csharp
    // Fresh, unsteered ranked set — best-first; item[0] is the recommended lead (PANEL-03).
    // item[3] is the deliberately long reply (>120 chars) for the PANEL-06 truncation demo.
    private static List<SuggestionItem> BuildFreshSet() => new List<SuggestionItem>
    {
        Item("Здравствуйте! Спасибо за обращение. Чем могу помочь?", LabelGreeting, "Ответ"),
        Item("Стоимость зависит от объёма заказа. Подскажите, что именно вас интересует?", LabelPrice, "Уточнить"),
        Item("Да, товар есть в наличии. Могу оформить для вас прямо сейчас.", LabelStock, "Ответ"),
        Item("Конечно, давайте подберём удобное для вас время. У нас есть свободные слоты на этой неделе " +
             "в первой половине дня и ближе к вечеру — подскажите, какой день вам подходит, и я сразу " +
             "забронирую запись на ваше имя.", LabelBooking, "К заказу"),
    };

    // Steered DRILL round (2026-08-18): variation titles, moves may repeat — mirrors the live
    // contract so the editor demo exercises the same shapes the server now emits.
    private static List<SuggestionItem> BuildSteeredSet(string steerTowardText) => new List<SuggestionItem>
    {
        Item("Отлично, тогда уточню детали по вашему запросу и сразу всё подготовлю.", "Со следующим шагом", "К заказу"),
        Item("Могу предложить пару вариантов под ваш бюджет — какой ориентир по цене вам комфортен?", "С вопросом", "Уточнить"),
        Item("Уже проверяю наличие на складе, буквально минуту.", "Коротко", "Отложить"),
        Item("К сожалению, сейчас это направление мы не обслуживаем, но буду рад помочь с другими вопросами.", LabelDecline, "Отказ"),
    };

    private static SuggestionItem Item(string text, string intentLabel, string move)
        => new SuggestionItem { text = text, intentLabel = intentLabel, move = move };
```

Also update the label-consts comment block (~line 21): the five consts stay, but `LabelStock`/`LabelPrice`/`LabelGreeting`/`LabelBooking` now read as fresh-set topics; adjust the comment to `// RU display titles (drill redesign 2026-08-18: fresh topics; steered variations are inline below).`

- [ ] **Step 4: Run the task's tests**

`Tools/run-tests-headless.sh 'MockSuggestionsProviderTests'` (or mcp-unity `run_tests`, class filter `MockSuggestionsProviderTests`).
Expected: PASS, total 8, failed 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/MockSuggestionsProvider.cs Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs
git commit -m "test(suggestions): mock provider emits moves + variation titles"
```

---

### Task 6: Scene wiring — one-shot additive header-title wirer

**Files:**
- Create: `Assets/Editor/SuggestionsHeaderTitleWirer.cs`
- Modify (via the wirer, not by hand): `Assets/Scenes/Main.unity`

**Interfaces:**
- Consumes: serialized field name `headerTitle` on `SuggestionsPanel` (Task 3), `SuggestionsPanel.DefaultHeaderTitle`.

- [ ] **Step 1: Create the wirer**

`Assets/Editor/SuggestionsHeaderTitleWirer.cs`:

```csharp
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot additive wirer (drill redesign 2026-08-18): assigns SuggestionsPanel.headerTitle
/// to the existing «ПРЕДЛОЖЕНИЯ» overline TMP via SerializedObject. Additive on purpose —
/// SuggestionsPanelBuilder carries uncommitted parallel work, so the scene is wired WITHOUT
/// a rebuild; fold the same stamping into the builder's BuildHeader once that work lands.
/// Idempotent: re-running re-assigns the same reference. Edit Mode only.
/// </summary>
public static class SuggestionsHeaderTitleWirer
{
    [MenuItem("Tools/Suggestions/Wire Header Title")]
    public static void Run()
    {
        var panels = Object.FindObjectsByType<SuggestionsPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (panels.Length == 0) { Debug.LogError("[HeaderTitleWirer] No SuggestionsPanel in the open scene."); return; }
        SuggestionsPanel panel = panels[0];

        TextMeshProUGUI title = FindHeaderTitle(panel);
        if (title == null)
        {
            Debug.LogError("[HeaderTitleWirer] No 'Title' TMP reading «ПРЕДЛОЖЕНИЯ» under the panel — is it built?");
            return;
        }

        var so = new SerializedObject(panel);
        SerializedProperty prop = so.FindProperty("headerTitle");
        if (prop == null) { Debug.LogError("[HeaderTitleWirer] SuggestionsPanel has no 'headerTitle' field — recompile first."); return; }
        prop.objectReferenceValue = title;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[HeaderTitleWirer] headerTitle -> {Path(title.transform)} (scene saved)");
    }

    private static TextMeshProUGUI FindHeaderTitle(SuggestionsPanel panel)
    {
        foreach (var tmp in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (tmp.name == "Title" && tmp.text == SuggestionsPanel.DefaultHeaderTitle) return tmp;
        return null;
    }

    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
```

- [ ] **Step 2: Import + run the wirer**

Editor open (required for scene mutation): `Assets/Refresh` (imports the new file, generates its `.meta`), then run the menu item `Tools/Suggestions/Wire Header Title` (mcp-unity `execute_menu_item` works unfocused). If no Editor is available, stop and hand this single step to the owner — do not script the scene by hand.
Expected console line: `[HeaderTitleWirer] headerTitle -> .../Header/Title (scene saved)`.

- [ ] **Step 3: Verify the wiring landed in the scene file**

```bash
grep -n "headerTitle" Assets/Scenes/Main.unity
```

Expected: one line `headerTitle: {fileID: <nonzero>}` (a `{fileID: 0}` means the assignment failed — re-check the console error).

- [ ] **Step 4: Commit IMMEDIATELY (scene + wirer together)**

```bash
git add Assets/Editor/SuggestionsHeaderTitleWirer.cs Assets/Editor/SuggestionsHeaderTitleWirer.cs.meta Assets/Scenes/Main.unity
git commit -m "feat(suggestions): wire the header title TMP into SuggestionsPanel (additive wirer)"
```

---

### Task 7: Full EditMode suite — green gate before the server work

- [ ] **Step 1: Run the whole suite**

Editor closed: `Tools/run-tests-headless.sh` (no filter). Editor open: drop `Temp/claude/run-tests.trigger` and read `Temp/claude/test-summary.json` (Editor must be focused), or mcp-unity `run_tests` unfiltered.
Expected: total ≥ 1820 (was 1813 before this feature; Tasks 1–5 added ~11), failed 0. A `total` of 0 is a false green — rerun with the correct mode.

- [ ] **Step 2: No commit** — verification only. If anything fails, fix within the owning task's files and amend that task's commit story with a follow-up commit (never `--amend` a pushed commit).

---

### Task 8: Canonical workflow edit — move/title split in prompt, schema, validators

**Files:**
- Modify: `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` (via the edit script below — never hand-edit the JSON)
- Scratch: `<scratchpad>/edit_suggest_replies.py` (not committed)

**Interfaces:**
- Produces: response items `{text, label, move}` per the spec; `move` ∈ 6-enum (repeats allowed), `label` free-form ≤24 distinct-casefold.

- [ ] **Step 1: Baseline probe run (only if dev n8n is up)**

```bash
curl -s -o /dev/null -w '%{http_code}' http://localhost:5678/healthz
```

If `200`: `python3 Tools/n8n/probe-suggest-replies.py` → expected exit 0 (this is the OLD contract baseline; keep the output for comparison). If the instance is down, note it and continue — the canonical edit is offline; ask the owner to start dev n8n before Task 9.

- [ ] **Step 2: Write the edit script to the scratchpad and run it**

Save as `edit_suggest_replies.py` in the session scratchpad, run with `python3 <scratchpad>/edit_suggest_replies.py` from the repo root. Every anchor is asserted to appear exactly once — a failed anchor means the canonical drifted; STOP and re-read the node instead of forcing.

```python
#!/usr/bin/env python3
"""One-shot canonical edit: drill rounds + move/title split (spec 2026-08-18)."""
import json, sys

PATH = 'Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json'
wf = json.load(open(PATH, encoding='utf-8'))

def node(name):
    for n in wf['nodes']:
        if n['name'] == name:
            return n
    sys.exit(f'node {name!r} not found')

def swap(nd, key, old, new):
    code = nd['parameters'][key]
    hits = code.count(old)
    if hits != 1:
        sys.exit(f"{nd['name']}/{key}: anchor found x{hits}, expected x1:\n{old[:140]}")
    nd['parameters'][key] = code.replace(old, new)

asm = node('Assemble')

# 1) Moves become the internal taxonomy; repeats allowed.
swap(asm, 'jsCode',
  "L.push('ХОДЫ (закрытый список меток — используй РОВНО эти значения, на русском, без кавычек):');",
  "L.push('ХОДЫ (внутренняя классификация карточки — поле move, РОВНО эти значения, на русском, без кавычек; ходы в наборе МОГУТ повторяться):');")

# 2) Title rules — inserted right after the move list.
swap(asm, 'jsCode',
  "L.push('- Отказ — вежливый отказ, сохраняющий клиента.');",
  "L.push('- Отказ — вежливый отказ, сохраняющий клиента.');\n"
  "L.push('ЗАГОЛОВКИ: label — короткий заголовок карточки для владельца: 1–3 слова, до 18 символов, по-русски, без кавычек, эмодзи и точки. Без НАПРАВЛЕНИЯ label называет ТЕМУ карточки («Цена», «Наличие», «Сроки»); с НАПРАВЛЕНИЕМ — ЧЕМ карточка отличается («Коротко», «Теплее», «С вопросом»). Все label в наборе РАЗНЫЕ. Названия ходов заголовками не делай.');")

# 3) РЕЖИМ replaces the count/ranking paragraph.
swap(asm, 'jsCode',
  "L.push('Выведи от 1 до 4 вариантов — столько, сколько ДЕЙСТВИТЕЛЬНО полезны владельцу; не добивай список ради количества (содержательный вопрос обычно заслуживает 4, тривиальная реплика — 1–2). Все метки РАЗНЫЕ (без повторов). Ранжируй по уместности: карточка 1 — тот ответ, который ты сам бы отправил. Если для уверенного «Ответ» не хватает данных из блока ДАННЫЕ — первой ставь «Уточнить» или «Отложить», а не догадку.');",
  "L.push('РЕЖИМ НАБОРА: если НАПРАВЛЕНИЕ не задано — сначала определи, сколько СУЩЕСТВЕННО разных полезных направлений ответа есть у последнего сообщения клиента. Два и больше — по одной карточке на направление (2–4 карточки, не добивай ради количества). Ровно одно (конкретный вопрос, простая реплика) — дай 4 ВАРИАНТА одного ответа: разная длина, тон, формат, следующий шаг. Ранжируй по уместности: карточка 1 — тот ответ, который ты сам бы отправил. Если для уверенного «Ответ» не хватает данных из блока ДАННЫЕ — первой ставь карточку с ходом «Уточнить» или «Отложить», а не догадку.');")

# 4) Drill block.
swap(asm, 'jsCode',
  "L.push('НАПРАВЛЕНИЕ: Владелец выбрал направление: «' + String(p.steerTowardText) + '». Дай 4 варианта, развивающие его: точнее/теплее/короче + логичный следующий шаг. Не повторяй выбранный текст дословно — карточка 1 должна быть заметно улучшенной его версией. Метки всё так же из списка, все разные.');",
  "L.push('НАПРАВЛЕНИЕ: Владелец выбрал направление: «' + String(p.steerTowardText) + '». ВСЕ карточки строго внутри этого направления — тему не расширяй и к другим вариантам ответа не возвращайся. Дай 4 варианта, каждый осознанно отличается по одной из осей: длина / тон / формат / следующий шаг; label называет отличие. Карточка 1 — заметно улучшенная версия выбранного текста, не дословный повтор. Ходы могут повторяться.');")

# 5) Trivial messages → 4 variants.
swap(asm, 'jsCode',
  "L.push('ТРИВИАЛЬНЫЕ СООБЩЕНИЯ: на «спасибо»/«ок»/подтверждение достаточно 1–2 коротких карточек (напр. Ответ «Пожалуйста, обращайтесь!» и, если сделка в процессе, мягкий «К заказу»). Это ответ клиенту бизнеса, НЕ повод для пустого массива — воздержание только для сообщений не по адресу бизнеса.');",
  "L.push('ТРИВИАЛЬНЫЕ СООБЩЕНИЯ: на «спасибо»/«ок»/подтверждение дай 4 коротких варианта разного тона и длины (тёплый / деловой / с продолжением диалога / со следующим шагом, если сделка в процессе). Это ответ клиенту бизнеса, НЕ повод для пустого массива — воздержание только для сообщений не по адресу бизнеса.');")

# 6) Output shape.
swap(asm, 'jsCode',
  "L.push('ВЫВОД: строго JSON по схеме — объект с массивом suggestions из 0–4 объектов {text, label}. Никакого текста вне JSON.');",
  "L.push('ВЫВОД: строго JSON по схеме — объект с массивом suggestions из 0–4 объектов {text, label, move}. Никакого текста вне JSON.');")

# 7) pickStats wording: метки → ходы.
swap(asm, 'jsCode', "' — метка:количество). Учитывай", "' — ход:количество). Учитывай")

# 8) Validate + Validate 2: full replacement, identical twins.
NEW_VALIDATE = """const ENUM = ['Ответ','Уточнить','Вариант','К заказу','Отложить','Отказ'];
const a = $('Assemble').first().json;
let items = [];
let parsedOk = false;
try {
  const content = $json.choices && $json.choices[0] && $json.choices[0].message && $json.choices[0].message.content;
  if (content) { const parsed = JSON.parse(content); if (parsed && Array.isArray(parsed.suggestions)) { items = parsed.suggestions; parsedOk = true; } }
} catch (e) { items = []; }
if (!Array.isArray(items)) items = [];
// Deliberate zero-card envelope. parsedOk guards it: a parse FAILURE must stay a
// retryable violation, never a silent quiet state.
const abstain = parsedOk && items.length === 0;
items = items.map(x => ({
  text: String((x && x.text) || '').replace(/[*_`#>]/g, '').trim().slice(0, 300),
  label: String((x && x.label) || '').replace(/[*_`#>]/g, '').trim().slice(0, 24),
  move: String((x && x.move) || '').trim()
}));
// Drill contract (2026-08-18): moves may REPEAT (4 variants of one move is the point);
// titles are display-unique — compared lowercased because the client renders them uppercase.
const titleKeys = items.map(i => i.label.toLowerCase());
const distinct = new Set(titleKeys).size === items.length;
const allValid = items.every(i => ENUM.indexOf(i.move) !== -1 && i.text.length > 0 && i.label.length > 0);
const ok = abstain || (parsedOk && items.length >= 1 && items.length <= 4 && allValid && distinct);
let violation = '';
if (!ok) {
  if (!parsedOk) violation = 'ответ не распознан';
  else if (items.length > 4) violation = 'больше 4 вариантов, получено ' + items.length;
  else if (!allValid) violation = 'ход вне списка, пустой текст или пустой заголовок';
  else violation = 'заголовки повторяются — все label должны быть разными';
}
return { json: { ok, abstain, items, violation, requestSeq: a.requestSeq, invalid: a.invalid } };"""
node('Validate')['parameters']['jsCode'] = NEW_VALIDATE
node('Validate 2')['parameters']['jsCode'] = NEW_VALIDATE

# 9) LLM schemas: move gets the enum, label loses it (strict mode owns no maxLength —
# the Validate clamp is the length gate).
OLD_ITEMS = '"required":["text","label"],"properties":{"text":{"type":"string"},"label":{"type":"string","enum":["Ответ","Уточнить","Вариант","К заказу","Отложить","Отказ"]}}'
NEW_ITEMS = '"required":["text","label","move"],"properties":{"text":{"type":"string"},"label":{"type":"string"},"move":{"type":"string","enum":["Ответ","Уточнить","Вариант","К заказу","Отложить","Отказ"]}}'
for name in ('LLM', 'LLM Retry'):
    swap(node(name), 'jsonBody', OLD_ITEMS, NEW_ITEMS)

# 10) Retry correction message.
swap(node('LLM Retry'), 'jsonBody',
  "Прошлый ответ нарушил правила: ' + $json.violation + '. Верни от 1 до 4 объектов в массиве suggestions — или ПУСТОЙ массив, если сообщение не требует ответа от бизнеса. Метки строго из списка: Ответ, Уточнить, Вариант, К заказу, Отложить, Отказ. Все метки разные. Каждый text непустой, без markdown, до 220 символов.",
  "Прошлый ответ нарушил правила: ' + $json.violation + '. Верни от 1 до 4 объектов {text, label, move} в массиве suggestions — или ПУСТОЙ массив, если сообщение не требует ответа от бизнеса. move строго из списка: Ответ, Уточнить, Вариант, К заказу, Отложить, Отказ (повторы допустимы). label — короткий русский заголовок до 18 символов, все label разные, названия ходов не использовать. Каждый text непустой, без markdown, до 220 символов.")

with open(PATH, 'w', encoding='utf-8') as f:
    json.dump(wf, f, indent=2, ensure_ascii=False)
    f.write('\n')
print('canonical updated: 10 edits applied')
```

Expected output: `canonical updated: 10 edits applied` (any `anchor found x0/x2` message = STOP and investigate).

- [ ] **Step 3: Run the offline gates**

```bash
node Tools/n8n/verify-panel-prompts.js
```

Expected: passes (niche blocks intact in the composed prompt).

```bash
python3 Tools/n8n/verify-telegram-parity.py
```

Expected: `ALL PARITY ASSERTS PASSED` (the «РЕЛЕВАНТНОСТЬ (ГЛАВНОЕ)» anchor and RAG structure are untouched).

```bash
git diff --stat Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
```

Eyeball the full diff too: ONLY `jsCode` of Assemble/Validate/Validate 2 and `jsonBody` of LLM/LLM Retry changed; the PANEL_PROMPTS map block shows zero diff lines.

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
git commit -m "feat(n8n): Suggest Replies drill rounds — move/title split in prompt, schema, validators"
```

---

### Task 9: Deploy to dev + probe matrix for the new contract

**Files:**
- Modify: `Tools/n8n/probe-suggest-replies.py`

**Interfaces:**
- Consumes: the deployed new contract (Task 8).

- [ ] **Step 1: Deploy the canonical to dev**

Requires dev n8n at `http://localhost:5678` (ask the owner to start it if down):

```bash
python3 Tools/n8n/build-suggest-replies.py --update 9PTyYcelRQI7bGDb
```

Expected: `workflow updated: id=9PTyYcelRQI7bGDb` … `activated`.

- [ ] **Step 2: Update the probe harness**

Edits to `Tools/n8n/probe-suggest-replies.py`:

1. Add near the top (after `URL = ...`):

```python
MOVES = ("Ответ", "Уточнить", "Вариант", "К заказу", "Отложить", "Отказ")
```

2. Both `card1_clarifies` checks (probes `P_autoparts_intake` and `D_photo_no_text`) become:

```python
        ("card1_clarifies", lambda c: c[0].get("move") == "Уточнить"),
```

3. Replace the `F_trivial_thanks_small_set` probe entirely (the expectation inverts by design — drill fills 4 variants):

```python
    ("F_trivial_thanks_variants", base("education", "SmartKids", "• Английский, группа (мес) — 20000 тг",
        [m("client", "сколько стоит английский для ребенка?"),
         m("business", "Группа — 20000 тг/мес."),
         m("client", "спасибо")]), [
        ("four_variant_cards", lambda c: len(c) == 4),
        ("distinct_titles", lambda c: len({x["label"].lower() for x in c}) == len(c)),
    ]),
```

4. Add a drill probe right after `H_steer_recluster`:

```python
    ("H2_drill_within_direction", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "нужен букет жене на годовщину, что посоветуете?")], steer=STEER), [
        ("four_cards", lambda c: len(c) == 4),
        ("titles_not_move_names", lambda c: not any(x["label"] in MOVES for x in c)),
        ("stays_on_the_roses_offer", lambda c: sum(
            1 for x in c if re.search(r"роз|букет|годовщин|доставк", x["text"].lower())) >= 3),
    ]),
```

5. In `main()`, after the existing `if not 1 <= len(cards) <= 4 ...` STRUCT block, add two new STRUCT checks:

```python
        bad_move = [c for c in cards if c.get("move") not in MOVES]
        if bad_move:
            print(f"  !! STRUCT-FAIL: move outside the enum: {[c.get('move') for c in bad_move]}")
            struct_fails += 1
            continue
        titles = [str(c.get("label", "")).strip() for c in cards]
        if any(not t or len(t) > 24 for t in titles) or len({t.lower() for t in titles}) != len(titles):
            print(f"  !! STRUCT-FAIL: labels must be non-empty, <=24 chars, distinct: {titles}")
            struct_fails += 1
            continue
```

6. Both card print lines (PROBES and ABSTAIN loops) become:

```python
            print(f"  [{c.get('label', '?')}/{c.get('move', '?')}] {c.get('text', '')}")
```

7. Docstring: append one line to the probe-matrix sentence: `Drill redesign (2026-08-18): H2 drills within a steer, F expects 4 trivial variants, STRUCT gates move∈enum + distinct short titles.`

- [ ] **Step 3: Run the probes**

```bash
python3 Tools/n8n/probe-suggest-replies.py
```

Expected: exit 0, `struct_fails=0`. Heuristic WARNs are sampling noise — re-run once and READ the printed cards before treating any as a regression. If a WARN is systematic (e.g. titles echo move names every run), tune ONLY the Assemble wording via a new anchored swap (repeat Task 8 steps 2–4 with the single extra edit, then redeploy and re-probe).

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/probe-suggest-replies.py
git commit -m "test(n8n): probe matrix covers drill rounds + free-form titles"
```

---

### Task 10: Docs + device-pass handoff

**Files:**
- Modify: `CLAUDE.md` (the `/webhook/SuggestReplies` bullet)
- Modify: `/Users/ayan/.claude/projects/-Users-ayan-Projects-Automation/memory/project_live_suggestions_rollout.md` (+ its `MEMORY.md` index line)

- [ ] **Step 1: Update the CLAUDE.md SuggestReplies bullet**

In the `/webhook/SuggestReplies` bullet, replace the sentence `Returns 1–4 cards {text,label} over the closed 6-move RU enum, or an abstain envelope for non-business messages.` with:

```
Returns 1–4 cards {text,label,move} (drill redesign 2026-08-18: `move` = the closed 6-move RU enum, INTERNAL only, repeats allowed; `label` = free-form RU title ≤24, distinct-casefold per round), or an `abstain` envelope for non-business messages. Round model: fresh request → the model counts genuinely distinct directions (≥2 → one topic-titled card each; exactly 1 → four variants of the one answer, incl. «спасибо» → 4 tone/length variants); `steerTowardText` present → DRILL: 4 cards strictly inside the picked direction, titles name the variation axis. Client: picked card's title becomes the panel header (uppercase, ‹ restores it via the round-stack triple), pickStats counts `move` under the unchanged keys, only fresh sets are cached.
```

- [ ] **Step 2: Update the memory file**

Append to `project_live_suggestions_rollout.md` body (and reflect the hook in `MEMORY.md`'s line for it): a dated line recording — drill redesign shipped on dev 2026-08-18 (move/title split, explore-vs-drill, header retitle, fresh-only cache), device pass pending; the Validate distinct rule now applies to TITLES (casefold), moves repeat by design; never re-add a label enum to the LLM schema (strict mode owns no maxLength — Validate clamps 24).

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: record the suggestions drill-rounds redesign"
```

(memory files live outside the repo — no git action for them)

- [ ] **Step 4: Hand the device-pass checklist to the owner**

Post this checklist in the session (do not claim done — it's the owner's pass):

1. Открой чат с «Вместе»: конкретный вопрос о цене → 4 карточки ПО ТЕМЕ (не 1 полезная + 3 левых).
2. Тапни карточку с темой (напр. «Цена») → шапка панели становится «ЦЕНА», 4 новые карточки — варианты внутри направления (короче/теплее/с вопросом...).
3. ‹ возвращает предыдущие карточки И прежнюю шапку («ПРЕДЛОЖЕНИЯ» на 1-м раунде).
4. Клиентское «спасибо» → 4 варианта разного тона/длины.
5. Не-бизнес сообщение → тихое «Нет предложений» (abstain не сломан).
6. Новое входящее сообщение → шапка сбрасывается на «ПРЕДЛОЖЕНИЯ», раунды с нуля.

---

## Execution notes

- Tasks 1–5 are pure code+tests and safe Editor-closed. Task 6 needs the Editor. Tasks 8–9 need dev n8n up (owner starts it).
- If the суite total ever reads 0/0 — the filter missed; fix the filter, never accept the green.
- Any Assemble wording tune after probes = new anchored swap + re-run BOTH offline gates + redeploy; never hand-edit the canonical JSON.
