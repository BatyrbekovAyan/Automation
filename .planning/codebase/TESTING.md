# Testing Patterns

**Analysis Date:** 2026-06-23

## Test Framework

**Runner:**
- NUnit 3 framework for EditMode tests
- Location: `Assets/Tests/Editor/Chat/`
- Assembly: `Assembly-CSharp-Editor` (no asmdef — tests compile into Unity's predefined editor assembly)
- Platform: EditMode only (no PlayMode tests detected)

**Assertion Library:**
- NUnit assertions via `using NUnit.Framework;`
- Common asserts: `Assert.IsTrue()`, `Assert.IsFalse()`, `Assert.AreEqual()`, `Assert.LessOrEqual()`, `StringAssert.EndsWith()`

**Run Commands:**
```bash
# Option 1: Editor closed (headless, cold start)
Tools/run-tests-headless.sh                 # Run all EditMode tests
Tools/run-tests-headless.sh "Chat\.Audio"   # Filter regex (full test name)

# Option 2: Editor open (in-Editor bridge)
echo "" > Temp/claude/run-tests.trigger     # Create trigger file
# Read results from Temp/claude/test-summary.json

# Option 3: Unity MCP (if available)
# Claude Code CLI can call mcp__mcp-unity__run_tests (after server restart)
# Requires EXACT class-name filter, may timeout if Editor unfocused
```

**Output Artifacts:**

*Headless mode (Tools/run-tests-headless.sh):*
- `Tools/test-output/results.xml` — NUnit3 result file (source of truth for test outcomes)
- `Tools/test-output/editor.log` — Full Unity editor log
- `Tools/test-output/headless-summary.json` — Compact machine-readable summary:
  ```json
  {
    "status": "completed",
    "source": "headless",
    "overall": "Passed",
    "total": 439,
    "passed": 439,
    "failed": 0,
    "skipped": 0,
    "inconclusive": 0,
    "green": true,
    "unityExit": 0
  }
  ```

*In-Editor bridge mode (ClaudeTestBridge):*
- `Temp/claude/test-summary.json` — Status, counts, failure list
- `Temp/claude/test-results.xml` — Full NUnit3 XML

**Exit Codes (headless mode):**
- `0` — All tests green (result=Passed, failed=0, inconclusive=0)
- `1` — Run completed but NOT green (failures/inconclusive) — real test result
- `2` — Could not run (Editor open, Unity binary missing, compile error, no results)

## Test File Organization

**Location:**
- All tests: `Assets/Tests/Editor/Chat/`
- No subdirectories; flat structure with 55 test files

**File Count:**
- 55 test classes (one class per file)
- ~439 total test methods across all files
- Examples: `ReplyParserTests.cs`, `OutboxStoreTests.cs`, `ChatViewModelReactionTests.cs`, `ReactionEmojiCatalogTests.cs`

**Naming:**
- Pattern: `[Subject]Tests.cs` (e.g., `ReplyParserTests.cs` tests the `ReplyParser` class)
- Class name: `public class [Subject]Tests { }`
- Method names: `public void [Scenario]_[Expected]()` or `public void [Condition]_[Result]()`

**Example (from ReplyParserTests.cs):**
```csharp
public class ReplyParserTests
{
    [Test]
    public void NullRaw_ReturnsNone()
        => Assert.IsTrue(ReplyParser.Resolve(null, _ => null, StubParse).IsEmpty);

    [Test]
    public void CacheHit_UsesCachedContent()
    {
        var cached = new MessageViewModel { /* ... */ };
        var preview = ReplyParser.Resolve(Reply("Q1"), id => cached, StubParse);
        Assert.AreEqual("Q1", preview.messageId);
    }
}
```

## Test Structure

**Suite Organization:**
```csharp
using NUnit.Framework;

public class [Subject]Tests
{
    // Setup/teardown methods
    [SetUp]
    public void SetUp()
    {
        // Initialize test state
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up (file I/O, temp directories)
    }

    // Helper factories
    private [Type] Make[Type](...) { }

    // Test methods
    [Test]
    public void [Scenario]_[Expected]()
    {
        // Arrange
        var data = Make[Type]();

        // Act
        var result = SomeClass.Method(data);

        // Assert
        Assert.AreEqual(expected, result);
    }
}
```

**Patterns:**

**Setup/Teardown (from OutboxStoreTests.cs):**
```csharp
[SetUp]
public void SetUp()
{
    _tempRoot = Path.Combine(Path.GetTempPath(), "OutboxStoreTests_" + System.Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
}

[TearDown]
public void TearDown()
{
    if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
}
```

**Arrange-Act-Assert (from OutboxStoreTests.cs):**
```csharp
[Test]
public void Add_PersistsToDisk()
{
    // Arrange
    var store = MakeStore();
    var entry = MakeEntry();

    // Act
    store.Add(entry);
    var store2 = MakeStore();  // New instance against same root

    // Assert
    var entries = store2.GetFor(entry.chatId);
    Assert.AreEqual(1, entries.Count);
    Assert.AreEqual("t1", entries[0].tempId);
}
```

**Expression-Bodied Tests (from ReplyParserTests.cs):**
```csharp
[Test]
public void NullRaw_ReturnsNone()
    => Assert.IsTrue(ReplyParser.Resolve(null, _ => null, StubParse).IsEmpty);

[Test]
public void SnippetFor_ShortTextUnchanged()
    => Assert.AreEqual("hello world", ReplyParser.SnippetFor(MessageType.Chat, "hello world"));
```

## Mocking

**Framework:** No explicit mocking library (Moq, NSubstitute) detected; tests use hand-rolled fakes.

**Patterns:**

**Stub Functions (from ReplyParserTests.cs):**
```csharp
private static MessageType StubParse(string t)
{
    switch (t)
    {
        case "image": return MessageType.Image;
        case "video": return MessageType.Video;
        case "ptt":   return MessageType.Voice;
        case "audio": return MessageType.Audio;
        case "document": return MessageType.Document;
        case "chat":  return MessageType.Chat;
        default:       return MessageType.Unknown;
    }
}
```

**Inline Lambdas (from ReplyParserTests.cs):**
```csharp
// Null resolver doesn't throw
[Test]
public void NullResolver_DoesNotThrow_FallsToSnapshot()
    => Assert.AreEqual("snap", ReplyParser.Resolve(Reply("Q4", body: "snap"), null, StubParse).text);

// Cache resolver: return specific value
[Test]
public void CacheHit_UsesCachedContent()
{
    var cached = new MessageViewModel { messageId = "Q1", /* ... */ };
    var preview = ReplyParser.Resolve(Reply("Q1"), id => id == "Q1" ? cached : null, StubParse);
    Assert.AreEqual("Q1", preview.messageId);
}
```

**Builder Factories (from ReplyParserTests.cs):**
```csharp
private static RawMessage Reply(string quotedId, string type = "chat", string body = "orig", string caption = null)
{
    var snap = new JObject { ["id"] = quotedId, ["type"] = type, ["body"] = body };
    if (caption != null) snap["caption"] = caption;
    return new RawMessage { type = "chat", isReply = true, replyMessage = snap };
}

private OutboxStore.OutboxEntry MakeEntry(string tempId = "t1", string chatId = "+15551@c.us")
    => new OutboxStore.OutboxEntry
    {
        tempId = tempId,
        chatId = chatId,
        text = "hi",
        timestamp = 12345,
        attemptCount = 1,
        profileId = "profileX"
    };
```

**What to Mock:**
- External file I/O (use temp directories with cleanup in TearDown)
- Callbacks and async continuations (pass lambdas or test doubles)
- Date/time (inject `Func<DateTime>` or similar)
- Data lookups (pass resolver functions that return test data)

**What NOT to Mock:**
- Business logic under test (always exercise real code)
- Serialization (test with real JSON/data parsing)
- String manipulation and validation (no mocking needed)

## Fixtures and Factories

**Test Data:**

From `ReplyParserTests.cs`:
```csharp
private static RawMessage Reply(string quotedId, string type = "chat", string body = "orig", string caption = null)
{
    // Factory for reply messages with optional parameters
}

private OutboxStore.OutboxEntry MakeEntry(string tempId = "t1", string chatId = "+15551@c.us")
{
    // Factory for outbox entries with sensible defaults
}
```

From `ChatViewModelReactionTests.cs`:
```csharp
var vm = new ChatViewModel("c1", "Title", "", "old", 100);  // Direct construction
```

**Location:**
- Factories as private static methods within the test class
- Shared test data (JSON, complex objects) as local class members or inline construction
- No separate fixture files detected; all data created in SetUp or test-local

**Temp Directories (from OutboxStoreTests.cs):**
```csharp
[SetUp]
public void SetUp()
{
    _tempRoot = Path.Combine(Path.GetTempPath(), "OutboxStoreTests_" + System.Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
}

[TearDown]
public void TearDown()
{
    if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
}
```

## Coverage

**Requirements:** No coverage enforcement detected (no target percentage in codebase)

**View Coverage:**
```bash
# Not implemented; headless runner does not generate coverage reports
# Manual review: test file count (55) and test method count (~439) suggest broad coverage of chat/message logic
```

**Observed Coverage:**
- Chat system: heavily tested (ReplyParserTests, ReactionTests, ChatHistoryCacheTests, etc.)
- Message formatting: heavily tested (DeliveryTickFormatter, ChatPreviewFormatter, etc.)
- Media/thumbnails: tested (VideoThumbFilesTests, ThumbnailKeyResolver, MediaUrlIdentityTests)
- UI ViewModels: tested (ChatViewModelReactionTests, MessageViewModel implied via other tests)
- Networking: Not directly tested (API calls are in Manager/ChatManager, no test files for them)
- Bot persistence: Not directly tested (PlayerPrefs operations are in Bot.cs, no dedicated tests)

## Test Types

**Unit Tests (dominant):**
- Scope: Individual functions/classes in isolation
- Examples:
  - `ReplyParserTests` — reply message parsing and quote resolution
  - `OutboxStoreTests` — local message store persistence
  - `ReactionEmojiCatalogTests` — emoji parsing and lookup
  - `ScrollFabMathTests` — scroll behavior calculations
- Approach: Arrange-act-assert with minimal fixtures; mocks for external dependencies

**Integration Tests:**
- Scope: Not explicitly separated from unit tests; some tests exercise multiple components
- Examples:
  - `ChatViewModelReactionTests` — ChatViewModel + reaction system
  - `ChatHistoryCacheForeignStripTests` — ChatHistoryCache + message filtering
- Approach: Same NUnit framework, no separate designation

**E2E Tests:**
- Not used — no PlayMode tests or full app flow tests detected
- Manual testing on device (iOS/Android) is the E2E verification path

## Common Patterns

**Async Testing (N/A):**
- No async/await in tests (all code is synchronous logic)
- No special handling needed for coroutines in EditMode tests

**Error Testing (from ReplyParserTests.cs):**
```csharp
[Test]
public void NullRaw_ReturnsNone()
    => Assert.IsTrue(ReplyParser.Resolve(null, _ => null, StubParse).IsEmpty);

[Test]
public void NullResolver_DoesNotThrow_FallsToSnapshot()
    => Assert.AreEqual("snap", ReplyParser.Resolve(Reply("Q4", body: "snap"), null, StubParse).text);

[Test]
public void QuoteEchoesBody_EmptyQuote_False()
    => Assert.IsFalse(ReplyParser.QuoteEchoesBody("hello", ""));
```

**Boundary Testing (from ReplyParserTests.cs):**
```csharp
[Test]
public void SnippetFor_CapsPathologicalLength()
{
    string huge = new string('x', 500);
    string snip = ReplyParser.SnippetFor(MessageType.Chat, huge);
    Assert.LessOrEqual(snip.Length, 161, "snippet should be capped so TMP never measures a multi-thousand-px line");
    StringAssert.EndsWith("…", snip);
}

[Test]
public void ShortReplyToLongOriginal_False()
{
    string quoted = ReplyParser.SnippetFor(MessageType.Chat, new string('z', 400));
    Assert.IsFalse(ReplyParser.QuoteEchoesBody("ok", quoted));
}
```

**Multiple Assertions (common pattern):**
```csharp
[Test]
public void SetReactionPreview_RefreshesRow_WithoutReordering()
{
    var vm = new ChatViewModel("c1", "Title", "", "old", 100);
    bool updated = false, reordered = false;
    vm.OnUpdated += _ => updated = true;
    vm.OnLastMessageChanged += _ => reordered = true;

    vm.SetReactionPreview("❤️", true, "Hello", "chat");

    Assert.IsTrue(updated, "should refresh the row");
    Assert.IsFalse(reordered, "must not reorder the chat list");
    Assert.AreEqual("❤️", vm.LastMessage);
    Assert.AreEqual("reaction", vm.LastMessageType);
    Assert.IsTrue(vm.IsLastMessageMine);
    Assert.AreEqual("Hello", vm.ReactionTargetText);
}
```

## ClaudeTestBridge (In-Editor Test Bridge)

**Purpose:**
- Allows running EditMode tests from terminal (Claude Code) without clicking the Unity GUI
- Handshake via trigger file: `Temp/claude/run-tests.trigger` → `Temp/claude/test-summary.json`

**Workflow:**

1. Terminal writes trigger file:
   ```bash
   # Run all tests
   echo "" > Temp/claude/run-tests.trigger

   # Run filtered tests
   echo "Chat\.Audio" > Temp/claude/run-tests.trigger
   ```

2. Editor polls ~2x/second (when focused) for the trigger file
3. Bridge validates fresh compilation (runs `AssetDatabase.Refresh()` before execution)
4. TestRunnerApi executes the suite and writes results:
   ```json
   {
     "status": "completed",
     "overall": "Passed",
     "passed": 439,
     "failed": 0,
     "total": 439,
     "durationSeconds": 15.234,
     "editorAssemblyWrittenUtc": "2026-06-23T10:30:45.1234567Z",
     "failures": []
   }
   ```

**Stale Assembly Guard:**
- Bridge tracks Assembly-CSharp-Editor.dll mtime (`editorAssemblyWrittenUtc`)
- If trigger is observed before compilation finishes, refresh is re-issued and trigger stays armed
- Only consumed after all compilation and domain reloads are complete
- Prevents silently testing stale code

**Limitations:**
- Requires Editor open and focused (defer if unfocused until focus returns)
- Polling interval ~2 seconds, so results take a moment to appear
- Cannot run if another process has the project lock (Editor running elsewhere)

## Headless Test Script (Tools/run-tests-headless.sh)

**Purpose:**
- Run EditMode tests with Unity launched COLD in batch mode (no Editor open)
- Fully hands-off; no window focus needed
- Useful for CI/CD and overnight test runs

**Key Features:**

1. **Lock Guard:** Refuses if Editor has project open (prevents "another instance running" error)
   ```bash
   GUI_PROC="$(pgrep -fl 'Unity.app/Contents/MacOS/Unity' | grep -iF -- "-projectPath ${PROJECT}" | grep -viE -- '-batchmode|assetimportworker')"
   if [ -n "${GUI_PROC}" ]; then
     echo "ERROR: Unity Editor is open on this project — refusing to launch a headless run."
     exit 2
   fi
   ```

2. **Version Detection:** Reads `ProjectSettings/ProjectVersion.txt` to find matching Unity binary
   ```bash
   VERSION="$(grep -m1 '^m_EditorVersion:' "${PROJECT}/ProjectSettings/ProjectVersion.txt" | awk '{print $2}')"
   UNITY="/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity"
   ```

3. **Output to Tools/test-output/ (NOT Temp/):** Temp is wiped on launch
   ```bash
   OUT_DIR="${PROJECT}/Tools/test-output"
   ```

4. **Optional -testFilter regex:** Pass as `$1` to scope tests
   ```bash
   Tools/run-tests-headless.sh "Chat\.Audio"
   ```

5. **XML Parsing as Source of Truth:** Checks `<test-run>` element for final result, not Unity exit code (which masks many errors)
   ```bash
   GREEN="$(xmllint --xpath 'boolean(/test-run[@result="Passed" and @failed="0" and @inconclusive="0"])' "${RESULTS}")"
   ```

**Exit Codes:**
- `0` — All green
- `1` — Run completed but failures/inconclusive
- `2` — Could not run

---

*Testing analysis: 2026-06-23*
