---
phase: 10-message-batching-debounce
fixed_at: 2026-07-27T00:00:00Z
review_path: .planning/phases/10-message-batching-debounce/10-REVIEW.md
iteration: 2
findings_in_scope: 11
fixed: 8
skipped: 3
status: partial
---

# Phase 10: Code Review Fix Report

**Fixed at:** 2026-07-27
**Source review:** `.planning/phases/10-message-batching-debounce/10-REVIEW.md` (0 Critical / 4 Warning / 7 Info)
**Iteration:** 2 — this report is CUMULATIVE and covers both passes.

**Summary (all 11 findings):**
- Findings in scope: 11 (`--all`: Critical + Warning + Info)
- Fixed: 8 — WR-01, WR-03, WR-04 (iteration 1) · IN-01, IN-02, IN-03, IN-06, IN-07 (iteration 2)
- Skipped, recorded as an explicit disposition: 3 — WR-02 (accepted v1 scope), IN-04 (accepted, locked ordering), IN-05 (deferred to the prod pass)

| Pass | Scope | Fixed | Skipped (recorded) |
|------|-------|-------|--------------------|
| Iteration 1 (`critical_warning`) | WR-01…WR-04 | WR-01, WR-03, WR-04 | WR-02 |
| Iteration 2 (`all`) | IN-01…IN-07 | IN-01, IN-02, IN-03, IN-06, IN-07 | IN-04, IN-05 |

> ## ⚠️ OWNER ACTION REQUIRED: REDEPLOY BOTH BOT TEMPLATES — AGAIN
> **The owner redeployed after iteration 1; iteration 2 changed the templates once more, so ONE
> further redeploy is needed.** Only IN-03 touched the JSON this pass, and template changes were
> deliberately batched into that single finding so exactly one more redeploy closes the gap.
>
> **The delta since the last redeploy is one line per template** — a `webhookId` on `Debounce Wait`:
> - `4wYitz5ek30SVNlT-WhatsApp_Bot.json` → `fb6e991f-9349-5bbc-8440-e22f4963bc65`
> - `4VN3gsFaC2HUYmcc-Telegram_Bot.json` → `056ec078-d878-5a32-b484-1fa38548e122`
>
> **Nothing was deployed by this pass** — no live n8n/Wappi API call, no `secrets.json` read, no
> workflow activated. Until the redeploy the committed JSON and the live dev copies differ by that
> one key.
>
> **Redeploy:** import/patch both templates from `Tools/n8n/workflows/`, then re-run
> `python3 Tools/n8n/verify-message-batching.py --dir <re-export dir>` as the post-import go/no-go.
> The canonical exports deliberately carry PROD (`bagkz.app.n8n.cloud`) URLs while the live dev
> copies use `localhost` — that asymmetry is by design; do not "fix" it during the import.
>
> **Re-check burden is LOW this time.** `webhookId` is inert at the current 8s window (n8n resumes a
> sub-65s Wait in memory and never registers the resume webhook), so no reply-path behavior changes.
> A single burst-coalesce smoke run per channel is enough; the iteration-1 re-check list below is
> already satisfied by the redeploy the owner has done.

## Fixed Issues

### IN-01: Clock mismatch — `WaitForSecondsRealtime` loop driving a `Time.time` gate

**File modified:** `Assets/Scripts/Chat/SuggestionsController.cs`
**Commit:** `c927b47`
**Applied fix:** both gate call sites now pass `Time.realtimeSinceStartup` — the `Poke` in `HandleLive` and the `ShouldFire` in `DebounceLoop`. The two MUST move together (a mixed pair would be a real bug, not a robustness nit); they were changed in the same commit and no third call site exists (`grep` confirms `_debounce.` appears only at the two clock sites plus the five `Cancel()` sites, which take no clock).

The file had changed since the review (burst accumulation + the reply-boundary `Cancel`), so the current code was read rather than trusting the review's line numbers — the finding still applied verbatim, just at lines 235/261 instead of 211/229.

Rationale kept in a comment: `Time.time` is `maximumDeltaTime`-capped, so a frame hitch or an app resume silently stretches the window, and it stops entirely at `timeScale == 0`; the loop already ticks on `WaitForSecondsRealtime`, and `ChatManager`'s poll idiom is `Time.realtimeSinceStartup` throughout.

### IN-02: Test literal `2.4f` silently coupled to `WindowSeconds == 2.5`

**File modified:** `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs`
**Commit:** `3141f3d`
**Applied fix:** `Assert.IsFalse(gate.ShouldFire(0.2f + Window - 0.1f), …)`, exactly as the review suggested, matching the other asserts in the file that already derive from `Window`.

Worth noting the substitution also makes the assert *stronger*, not merely tunable: the last poke is at `0.2f`, so the new probe time is exactly `Window + 0.1f` — the deadline the *second-to-last* poke would have had. It therefore proves the third poke genuinely RESET the window, which the old `2.4f` (below every deadline in play) never did.

### IN-03: `Debounce Wait` was the only Wait node without a `webhookId`

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`
**Commit:** `4755dc6` — **this is the change that forces the redeploy flagged above.**
**Applied fix:** the migration now emits `"webhookId": nid("Debounce Wait-webhook")`, i.e. the same stable uuid5 idiom already used for every node id, so it is byte-stable across re-runs and distinct per template. The `DEBOUNCE_SECONDS` comment now states outright that raising it past 65s flips n8n to webhook-resume + DB offload, which is *why* the key is there.

Confirmed against the real templates before writing: all four pre-existing Wait nodes in both files (`Reading Pause`, `Typing Pause`, `Pause Before Reading`, `Listening Pause`) carry a `webhookId`, and — a quirk worth recording — they all share the SAME one (`99b49c83-…`), an artifact of UI duplication. Per-template uuid5 values were emitted instead of copying that shared id; distinct ids are the normal case for independently-created nodes and strictly safer than a deliberate collision.

**New verifier assert:** `Debounce Wait has no valid webhookId (breaks webhook-resume if the window is tuned >= 65s)` — presence + UUID *shape* via a new `is_uuid()` helper, deliberately NOT an exact-value match. The same verifier gates a prod RE-EXPORT through `--dir`, and an importer that re-mints the id must not read as a no-go.

### IN-06: `apply-message-batching.py` — dead parameter and unguarded lookups

**File modified:** `Tools/n8n/apply-message-batching.py` (script only — **zero** JSON change)
**Commit:** `980363c`
**Applied fix:** dropped the never-used `type_suffix` parameter (`find(nodes, name)` is now positional, matching all four call sites) and routed the pre-existing-node lookups through a new `require(wf, name)` helper that raises `AssertionError(f"{wf['id']}: '{name}' node not found (renamed in the n8n UI?)")`.

Two deliberate deviations from the review's suggested patch:
- It guards **three** lookups, not two. The review's own line reference (`:107`) included `sx, sy = find(nodes, name="Suppressed?")["position"]`, which has the identical defect, so `Mark Read`, `Suppressed?` and `Text` are all covered.
- It raises `AssertionError` explicitly rather than using a bare `assert`, which `python -O` strips. Same effect, same named message, mirrors the verifier's `node()` helper.

`managed()` still uses raw `find()` — it is the one caller that legitimately wants `None` (upsert-or-append).

### IN-07: Unused serialized field `_mockLatencySeconds` (pre-existing)

**File modified:** `Assets/Scripts/Chat/SuggestionsController.cs`
**Commit:** `f1d7a12`
**Applied fix:** field declaration deleted, replaced by a one-line note saying why (`MockSuggestionsProvider` has not been constructed here since the Phase-2 swap, and it owns its own latency default).

Repo-wide grep first, as instructed: the only references were the declaration itself, `Assets/Scenes/Main.unity:11614`, and five `Assets/_Recovery/*.unity` copies. `MockSuggestionsProvider` is constructed nowhere in runtime code — only in `MockSuggestionsProviderTests.cs`, which passes its own latency.

**Known, harmless leftover:** `Main.unity` still carries a stale `_mockLatencySeconds: 1` YAML line for this component. Unity ignores a serialized value with no matching field and drops it the next time the scene is saved. **The scene was NOT opened or edited** (parallel-session scene-clobber hazard) — this is deliberate, not an oversight.

### WR-01: Empty `combinedText` ("") defeats the Text node's `??` fallback *(iteration 1)*

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, both bot templates
**Commit:** `686e7bf`
**Applied fix:** `combinedText = parts.length > 0 ? parts.join('\n') : null;` in `LATEST_COMBINE_JS`, exactly as the review suggested, plus a comment stating why (`??` is nullish-only). New verifier assert: *"Latest+Combine joins an empty run to '' (defeats the Text node ?? fallback)"*.
**Evidence:** a node harness driving the generated JS confirmed the review's scenario — a `fromMe` bot reply newest in the window now yields `combinedText=null` (was `""`), so the Text node's `?? $json.body.messages[0].body` fallback engages.

### WR-03: `Fetch Recent` un-retried on the hot path + crossed-response exposure *(iteration 1)*

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, both bot templates
**Commit:** `201e209`
**Applied fix (a) — retry:** node-level `retryOnFail: true, maxTries: 3, waitBetweenTries: 1000` on `Fetch Recent`, matching the existing `Delete Orphan Profiles` idiom (that sweep uses `waitBetweenTries: 2000`; the review specified 1000 for the hot path). Three new verifier asserts.

**Applied fix (b) — crossing, conservative version:** `Latest+Combine` filters the fetched rows down to the requested chat *before* the is-latest/combine computation, so foreign rows from a crossed response can drive neither decision. Field names were confirmed in the codebase, not guessed (`wh.body.messages[0].chatId` on the webhook side; `chatId` per row per `RawMessage.cs:10` and `CrossChatResponseGuard`). Mirrors `CrossChatResponseGuard`'s conservatism: a row with **no** `chatId` is KEPT, so a shape that ever omits it degrades to exactly today's behavior.

**Degenerate case, explicitly chosen:** a fully crossed payload filters to empty → `newestIncoming` undefined → `abort = true` → the fragment dead-ends with no reply. That is the SAME outcome the pre-fix code produced for a crossed response (its `newestIncoming.id !== triggeringId` also aborted), so abort semantics are unchanged and a duplicate-reply storm is impossible.

**Evidence:** a node harness ran burst-winner, burst-loser, single-message, media-latest, bot-reply-bounded, no-`chatId` and empty-fetch cases — all identical to pre-fix. An A/B against the pre-fix JS showed the one real delta besides retry: a foreign row time-ordered *between* two of this chat's fragments used to be concatenated into the prompt and is now dropped — this also closes a cross-chat text-leak path. `foreignFetched` rides on the node output as the crossing-observability flag the review asked for.

### WR-04: Missing explicit `pairedItem` on the spliced Code node *(iteration 1)*

**Files modified:** `Tools/n8n/apply-message-batching.py`, `Tools/n8n/verify-message-batching.py`, both bot templates
**Commit:** `b6a7c22`
**Applied fix:** `return [{ json: { ...wh, abort, combinedText, foreignFetched }, pairedItem: { item: 0 } }];`, matching the orchestrators' `Vertical Prompt` idiom. New verifier assert.
**Note:** the first attempt tripped the pre-existing `"$('Webhook').item" not in js` assert — an explanatory comment contained that literal substring. Rather than weaken a correct, deliberately strict assert, the comment was reworded. Nothing was committed in the failing state.

## Skipped Issues (each recorded as an explicit, durable disposition)

### WR-02: Mixed-type bursts silently drop the earlier fragment *(iteration 1)*

**File:** `Tools/n8n/apply-message-batching.py` (abort/combine logic)
**Status:** skipped — **accepted v1 scope**, recorded (not code-fixed). Record commit `2bcc1e3`.
**Reason:** Phase 10's locked scope is text-only combine (`10-CONTEXT.md` line 25: *"Combine boundary rule (v1): trailing run of consecutive incoming TEXT messages … Known v1 limitation"*). The review's own first option is "record it explicitly". Its contained alternative — prepending the window's pending text to a media winner — is a behavioral redesign of the reply path that would invalidate the already-passed runData matrix and device UAT and needs its own live e2e; not a review-fix-pass change.
**Action taken:** a clearly-marked note in the `<deferred>` section of `10-CONTEXT.md` spelling out all three interleavings (voice→text, text→voice, text→image), recording that dropped content reaches neither the AI Agent nor Chat Memory, stating plainly that it is NOT a regression for the 10-04 owner pass, and flagging the voice→text / text→image cases for the future media-handling e2e matrix. The locked decisions were not rewritten.

### IN-04: Suppression flag is read pre-wait — up to 8s stale at generation time

**File:** both bot templates (`Read Reply Mode` → `Suppressed?` → `Debounce Wait` ordering)
**Status:** skipped — **accepted; the ordering is a LOCKED design property, not a defect.** Record commit `1e12e94`.
**Reason (verified against the artifacts, not just accepted on instruction):** gate-BEFORE-debounce is the mitigation for threat **T-10-01-03** in `10-SECURITY.md` — *"suppression-bypass if debounce spliced before the gate"*, closed on the evidence `connections["Suppressed?"]["main"] == [[], [{"node":"Debounce Wait",…}]]` in both templates. `verify-message-batching.py` asserts that exact edge (`main[0] == []` dead-end, `main[1]` → `Debounce Wait`), and **T-10-04-02** carries the behavioral confirmation from UAT scenario 5. Moving the read after `Is Latest?` would invalidate that verified evidence and the closed security audit. The review itself scopes the race as "one-message-deep; likely acceptable".
**Action taken:** a note in `10-CONTEXT.md` `<deferred>`, marked *"do not 'fix' this ordering"*, citing T-10-01-03 / T-10-04-02 / the verifier assert, and recording that the only safe future shape is an ADDITIONAL re-read on the winner branch — never a moved gate.

### IN-05: Telegram-only — aborted fragments are never marked read

**File:** `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` (`Mark Read`: single `message_id`, no `mark_all`)
**Status:** skipped — **deferred to the prod pass.** Record commit `60fb569`.
**Reason:** cosmetic (read receipts on losing fragments only; no content is lost — the combine still answers the whole text run). It was investigated for a trivially safe fix and there is none, on three independent pieces of repo evidence:
1. `ChatManager.cs` (~:2348, the Mark Read URL builder) states outright that **tapi's `mark/read` documents no `mark_all` param** and that its bulk lever is `mark_all` on `messages/get` instead.
2. That lever is **still open question #13** in `Tools/tapi/SHAPES.md`, deliberately never probed because it MUTATES the owner's real read state (`Tools/tapi/samples/INDEX.json`: *"13: mark_all read mutation — mutating, not run here"*). Implementing on it would be guessing at an API shape.
3. Hanging it off `Fetch Recent` is **forbidden by two existing verifiers** — batching Pitfall 5 (`verify-message-batching.py`: no `mark_all` on `Fetch Recent`) and `verify-telegram-parity.py`'s "Mark Read still has mark_all" assert — because marking read during the wait defeats the deliberate downstream humanizer `Mark Read`.

**Action taken:** the above recorded in `10-CONTEXT.md` `<deferred>`, with the concrete prerequisite named: a live tapi probe of SHAPES #13 must come first, scheduled with the prod pass.

## ⚠️ Unverified — pending the orchestrator's Unity suite run

The Unity test bridge was **not** used and `Temp/claude/run-tests.trigger` was **not** touched (single shared channel; a parallel session may hold the Editor). Two commits change C# and are therefore **unverified by the EditMode suite**:

| Commit | File | What to watch |
|--------|------|---------------|
| `c927b47` (IN-01) | `Assets/Scripts/Chat/SuggestionsController.cs` | Runtime-only change; `IncomingDebounceGate` takes an INJECTED clock, so no existing test observes `Time.*`. Expected impact on the suite: **none**. |
| `3141f3d` (IN-02) | `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs` | The only assert that could move. `ThreeRapidPokes_CoalesceToOneFire` probes `0.2f + Window - 0.1f` = 2.6 against a deadline of `0.2f + Window` = 2.7 → still inside the window → `IsFalse` holds. |
| `f1d7a12` (IN-07) | `Assets/Scripts/Chat/SuggestionsController.cs` | Field deletion only; nothing referenced it. |

Static checking that WAS done on all three: `mcs --parse` (syntax-only, Mono) exits 0 on `SuggestionsController.cs`, `IncomingDebounceGateTests.cs` and `IncomingDebounceGate.cs`. The check was proven to bite (an injected `void X( }` on a scratch copy → `error CS1525`, exit 1; scratch deleted). This is a parser, **not** a type-checker or a test run — the suite is still the real gate.

## Verification Performed (iteration 2)

- **`verify-message-batching.py` exits 0** on both committed templates after every commit (`ALL BATCHING ASSERTS PASSED`), including the final state.
- **`verify-telegram-parity.py` also still exits 0** — checked because IN-05's investigation surfaced that it owns a competing `mark_all` invariant on the same node.
- **New assert proven to bite:** the `webhookId` assert was run against a mutated scratch copy twice — key removed, and key set to `"not-a-uuid"` — each exited 1 with its own named reason; the unmutated control copy exited 0 first. All scratch copies deleted (scratchpad confirmed empty).
- **`require()` guards proven to bite:** each of `Mark Read`, `Suppressed?`, `Text` was renamed in an in-memory deep copy and `splice()` re-run — all three raised the named `AssertionError` instead of a raw `TypeError`; an unmutated control still spliced cleanly. No file was written during this check.
- **Materialization proven:** IN-03's `webhookId` was read back out of BOTH committed templates by JSON parse (`fb6e991f-…` / `056ec078-…`), matching the independently computed uuid5 values — never a Python-only change.
- **JSON-neutrality proven for IN-06:** `shasum` on both templates before/after re-running the migration is identical, so the script cleanup provably changed no output.
- **Idempotency proven:** re-running `apply-message-batching.py` immediately after a run leaves both templates byte-identical — checked after IN-03, after IN-06, and once more at the end.
- **Syntax:** both Python scripts pass `ast.parse`; the three C# files pass `mcs --parse`.
- **Scope hygiene:** only deliberately-changed paths staged, never `git add -A/-u/.`. `git diff --stat 8ec17b0..HEAD` lists exactly 7 files — no scene, no prefab, no `Temp/`, and none of the unrelated untracked working-tree files. No live API call, no `secrets.json` read, no deploy, no activation.

## Commit Index

| Finding | Commit | Type |
|---------|--------|------|
| Enabling refactor (upsert managed nodes) | `104ead8` | iteration 1 |
| WR-01 | `686e7bf` | fix |
| WR-03 | `201e209` | fix |
| WR-04 | `b6a7c22` | fix |
| WR-02 | `2bcc1e3` | accepted, recorded |
| README (upsert ownership + asserts) | `9bd7142` | docs |
| IN-01 | `c927b47` | fix (C#, suite-pending) |
| IN-02 | `3141f3d` | fix (C#, suite-pending) |
| IN-03 | `4755dc6` | fix — **forces the redeploy** |
| IN-06 | `980363c` | fix (script only) |
| IN-07 | `f1d7a12` | fix (C#, suite-pending) |
| IN-04 | `1e12e94` | accepted, recorded |
| IN-05 | `60fb569` | deferred, recorded |

---

_Fixed: 2026-07-27_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 2 (cumulative over iterations 1–2)_
