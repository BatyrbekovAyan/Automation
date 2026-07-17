---
phase: 08-device-uat-milestone-closeout
plan: 15
subsystem: chat
tags: [media-download, wappi, tapi, diagnostics, retry, serial-queue, telegram, whatsapp, video]

# Dependency graph
requires:
  - phase: 05-device-uat-milestone-closeout (05-06 capture-gated media)
    provides: "The strictly-serial message/media/download-by-id queue (DownloadMediaRoutine / DrainMediaDownloadQueue) both channels share"
  - phase: 08-device-uat-milestone-closeout (08-11)
    provides: "Prior ChatManager.cs edits — this plan waves after 08-11 to avoid a shared-file collision"
provides:
  - "Pure MediaDownloadFailure classifier (network/timeout, HTTP error, no-link-in-response, parse-error) + 256-char capped, single-line log formatter"
  - "All three silent DownloadMediaRoutine failure exits now log one capped, actionable line (id + HTTP status + reason + snippet) before onFailure — device-diagnosable"
  - "One serial-safe transient retry (network/timeout or HTTP 5xx) inside DownloadMediaRoutine; the strictly-serial media queue is preserved (no new concurrent request)"
affects: [08-16 device re-verify (D11 failure-log capture + server-side diagnosis)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure UnityEngine-free classifier seam for a coroutine failure path (mirrors ChatRowSwipePolicy / ChatIdFormat): the coroutine keeps only the yields, the decision + formatting are unit-tested in isolation"
    - "Size-capped, single-line, no-file-write diagnostic logging (T-08-15-01): never the full response body — the opposite of the pre-existing response.txt dumps (IN-03)"
    - "Inline bounded retry INSIDE a serial-queue worker coroutine: re-issue a fresh `using` request in a for-loop (worker stays blocked) rather than StartCoroutine — preserves strict seriality under Wappi's concurrent-response crossing bug"

key-files:
  created:
    - Assets/Scripts/Chat/MediaDownloadFailure.cs
    - Assets/Tests/Editor/Chat/MediaDownloadFailureTests.cs
  modified:
    - Assets/Scripts/Main/ChatManager.cs

key-decisions:
  - "Instrument FIRST: the D11 root cause (why SOME videos/GIFs/notes never download) is unknown and the owner suspects a Wappi/tapi server cause — the capped logs make the 08-16 device pass produce actionable evidence (expired s3 link? empty 2xx? HTTP status? media type?) before any speculative server-side handler is built."
  - "Retry ONLY transient kinds (NetworkOrTimeout, or HttpError >= 500). NoLinkInResponse / HTTP 4xx / ParseError surface immediately — they are the likely server-side cause; a retry would just hang the serial queue without fixing them."
  - "Retry stays INLINE in DownloadMediaRoutine (the drain worker blocks on it) — NEVER a new StartCoroutine — because Wappi cross-serves concurrent /media/download requests (memory wappi-media-download-crossing). Seriality is the invariant."
  - "Capped Snippet (256 chars, single-line, null-safe) — deliberately NOT the pre-existing full-payload response.txt dumps (08-REVIEW IN-03); no File.WriteAllText added anywhere in the download path."

patterns-established:
  - "MediaDownloadFailure.Classify/Snippet/FormatLog is the single formatting seam for every download failure exit — future exits log through it, never ad-hoc."

requirements-completed: []

# Metrics
duration: ~15min
completed: 2026-07-17
---

# Phase 8 Plan 15: D11 Media-Download Instrumentation + Serial-Safe Retry Summary

**Every message/media/download failure now logs one capped, actionable line (id + HTTP status + classified reason + a 256-char single-line body snippet) via a new pure `MediaDownloadFailure` seam, and a transient failure (network/timeout or HTTP 5xx) gets ONE serial-safe inline retry — the strictly-serial Wappi media queue is preserved, no full-payload logging, no file write.**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-07-17T08:02:00Z (approx, after 08-14 completed at 08:00:03Z)
- **Completed:** 2026-07-17T08:16:12Z
- **Tasks:** 2 (Task 1 TDD: instrument; Task 2: retry)
- **Files modified:** 3 (1 modified, 2 created)

## Accomplishments
- New pure, UnityEngine-free `MediaDownloadFailure` classifier + capped log formatter (`Classify` / `Snippet` / `FormatLog`, `MaxSnippet = 256`), fully unit-tested (10 tests).
- All THREE previously-silent `DownloadMediaRoutine` failure exits — HTTP `!Success`, empty 2xx body (no `file_link`/`file_b64`), and JSON parse throw — now emit a device-visible `[MediaDownload] FAIL id=… http=… kind=… body=…` line before `onFailure`, so the 08-16 device pass can show WHY specific videos/GIFs/notes fail.
- One bounded, serial-safe retry: a TRANSIENT failure (NetworkOrTimeout, or HttpError with `responseCode >= 500`) re-issues a fresh `using` request ONCE after a 1.5s backoff, INLINE on the drain worker (no new `StartCoroutine`), so Wappi's concurrent-response crossing bug is never re-introduced.
- Full EditMode suite green at **1121/1121** (baseline 1111 + 10 new) on a freshly recompiled runtime assembly, twice (after Task 1 and after Task 2).

## Instrumentation added (per plan `<output>`)

`MediaDownloadFailure` (pure): `enum MediaDownloadFailureKind { NetworkOrTimeout, HttpError, NoLinkInResponse, ParseError }`.
- `Classify(bool resultIsSuccess, long httpStatus, bool hasFileLink, bool hasFileB64)` — `!success && http==0 ⇒ NetworkOrTimeout`; `!success ⇒ HttpError`; `success && no link/b64 ⇒ NoLinkInResponse`.
- `Snippet(body)` — first `MaxSnippet` (256) chars, CR/LF/tab collapsed to spaces (single line), null-safe. **Never the full body** (T-08-15-01).
- `FormatLog(id, http, kind, snippet)` — `"[MediaDownload] FAIL id=… http=… kind=… body=…"`.

Wired at the three `DownloadMediaRoutine` exits (each `Debug.LogWarning(FormatLog(…, Snippet(www.downloadHandler?.text)))` before `onFailure`): (1) transport/HTTP `!Success` → `Classify(false, responseCode, …)`; (2) empty 2xx → `Classify(true, responseCode, false, false)` ⇒ `NoLinkInResponse`; (3) parse catch → `ParseError`. No `File.WriteAllText` added; the pre-existing `response.txt` dumps in `SyncAllChats` (IN-03) were left untouched.

## Retry policy (per plan `<output>`)

- At most **2 attempts** total (`const int maxAttempts = 2`, `for` loop). Retry fires ONLY when `kind == NetworkOrTimeout` OR (`kind == HttpError && responseCode >= 500`) AND a retry remains.
- Retry is **inline** — a fresh `using UnityWebRequest` per attempt, re-applying `.timeout = 30` (per `.claude/rules/networking.md`), with a `yield return new WaitForSeconds(1.5f)` backoff that runs BETWEEN disposed requests (never holding one in flight). The drain worker stays blocked on this coroutine, so the queue stays strictly serial — **no new `StartCoroutine`**.
- PERMANENT failures (HTTP 4xx, `NoLinkInResponse`, `ParseError`) surface immediately — no retry — with the capped log, because they are the likely server-side cause the owner suspects and a retry can't fix them.
- `onFailure` is invoked **exactly once** per exhausted request (the retry path does not call it). The retry's `WaitForSeconds` is killed by `SetActiveBot`/`SetActiveChannel`'s `StopAllCoroutines`; `ClearMediaDownloadQueue` (already wired in `BotState.cs`/`Channel.cs` right after it) resets the worker on switch. The existing `onFailure` consumer (`MessageItemView` → `ShowVisualDownloadButton` / `HandleFinalFailure`) renders the tap-to-retry error card, never an infinite spinner (the `timeout = 30` guards each attempt).

## Task Commits

Each task was committed atomically (TDD RED → GREEN for Task 1):

1. **Task 1 (RED): failing classifier tests** — `4739e48` (test)
2. **Task 1 (GREEN): instrument the three failure exits** — `69f2f37` (feat)
3. **Task 2: one serial-safe transient retry** — `9e4e614` (fix)
4. **New-file Unity metas** — `d665e3b` (chore)

**Plan metadata:** final docs commit (this SUMMARY + STATE + ROADMAP).

## Files Created/Modified
- `Assets/Scripts/Chat/MediaDownloadFailure.cs` (created) — pure classifier + capped log formatter.
- `Assets/Tests/Editor/Chat/MediaDownloadFailureTests.cs` (created) — 10 EditMode tests (Classify kinds, Snippet cap/strip/null-safe, FormatLog contents + body cap).
- `Assets/Scripts/Main/ChatManager.cs` (modified) — `DownloadMediaRoutine` restructured: capped logging at all three exits (Task 1) + a bounded 2-attempt transient-only inline retry loop (Task 2). The serial queue (`DrainMediaDownloadQueue` / `ClearMediaDownloadQueue` / `_mediaDownloadQueue`) is unchanged.

## Decisions Made
See `key-decisions` frontmatter — instrument-first (evidence before speculative server handler); transient-only retry; inline (never concurrent) retry to preserve seriality; capped no-file-write logging.

## Deviations from Plan

None — plan executed exactly as written. Both tasks landed as specified; the only structural liberty was moving the 2xx-body parse `try/catch` so it no longer wraps the `onSuccess`/`onFailure` callbacks (it wraps ONLY `JObject.Parse`), which is both cleaner and required to keep every `yield` outside a try-with-catch. Behaviour is unchanged: a parse throw still classifies `ParseError` → `onFailure`.

## Issues Encountered
- **Unity TDD nuance (documented, not a defect):** a brand-new type referenced by a test does not produce a clean runtime RED in Unity — the whole `Assembly-CSharp-Editor` fails to compile, and the bridge would report `CompilationFailed`, not a test failure. So the RED gate (`4739e48`) commits the tests with the type deliberately absent (guaranteed-failing by construction), and the first bridge run was executed at GREEN (Task 1), where all 10 new tests passed (1111 → 1121). This preserves the TDD gate sequence (test commit precedes feat commit) within Unity's compile model.

## TDD Gate Compliance
- RED gate present: `4739e48` `test(08-15): add failing tests …`.
- GREEN gate present after it: `69f2f37` `feat(08-15): instrument …`.
- No REFACTOR commit needed (the classifier was clean on first pass).

## Verification
- **Task 1 acceptance greps (all pass):** `Classify` signature present; `MaxSnippet = 256`; `MediaDownloadFailure.cs` has NO `using UnityEngine` (pure); `grep -c MediaDownloadFailure ChatManager.cs` = 8 (≥3, all exits); no new `WriteAllText` inside `DownloadMediaRoutine`; `[Test]` count = 10 (≥5).
- **Task 2 acceptance greps (all pass):** `const int maxAttempts = 2` (retry ≤ once); transient-only guard (`NetworkOrTimeout` / `HttpError && httpStatus >= 500`); `StartCoroutine` count inside the method = **0** (no new concurrent download); `WaitForSeconds` backoff present; 4 distinct terminal `onFailure` paths, each followed by `yield break` (no double-invoke); fresh per-attempt `using` request.
- **EditMode suite:** **1121/1121 passed, 0 failed** via the in-Editor `ClaudeTestBridge` (Unity Editor open — headless runner correctly refused under the project lock). Both runs FRESH: Task 1 run `Assembly-CSharp.dll` 12:54:04 → 13:10:51, `editorAssemblyWrittenUtc` 07:23:54Z → 08:10:52Z, `finishedAt` 4704.02 → 5713.83, total 1111 → 1121; Task 2 run `Assembly-CSharp.dll` 13:10:51 → 13:14:42, `finishedAt` 5713.83 → 5942.34 (runtime-only edit, so `editorAssemblyWrittenUtc` held at 08:10:52Z — the runtime-DLL mtime is the correct freshness gate).
- **Threat register:** T-08-15-01 (info disclosure) mitigated — `Snippet` caps at 256, single-line, no file write; response.txt dumps untouched. T-08-15-02 (concurrent-request crossing) mitigated — inline retry, 0 new `StartCoroutine`, `ClearMediaDownloadQueue` still cancels on switch. T-08-15-03 (unbounded retry DoS) mitigated — at most one retry, transient-only, `timeout = 30` + 1.5s backoff, permanent failures surface immediately.

## Known Stubs
None — the classifier wires real diagnostics into a real failure path; no placeholder/empty data introduced.

## EXACT device capture ask for 08-16 (per plan `<output>`)
On the failing build, reproduce a media download that never completes — a **video, a GIF, and a video-note (кружок)**, on both WhatsApp and Telegram if possible — then open **logcat** (Android) / device console (iOS) and copy every line matching:

```
[MediaDownload] FAIL id=… http=… kind=… body=…
```

For each failure, record the `kind` and `http`:
- `kind=NoLinkInResponse http=200` ⇒ Wappi returned a 200 with an EMPTY body (no `file_link`/`file_b64`) — strong signal of a **server-side cause** (file the Wappi/tapi ticket; the `body=` snippet shows what came back).
- `kind=HttpError http=4xx/5xx` ⇒ the endpoint rejected/erred (status tells which); 5xx auto-retried once (look for the `— transient, retrying once` line).
- `kind=NetworkOrTimeout http=0` ⇒ the 30s timeout or a connection drop (auto-retried once).
- `kind=ParseError` ⇒ the 200 body was not the expected JSON (the `body=` snippet shows the malformed head).

Also confirm the bubble shows the **tap-to-retry card**, not a dead spinner. The server-side diagnosis (and any targeted follow-up, e.g. re-request-by-id on an expired s3 link) is deliberately deferred to a post-08-16 follow-up once this evidence exists — no speculative handler was built here.

## Next Phase Readiness
- D11 instrumentation + conservative resilience are code-complete and suite-green. The device evidence + server-side diagnosis ride the **08-16** consolidated re-verify checkpoint. No blockers.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-15-SUMMARY.md`
- FOUND: `Assets/Scripts/Chat/MediaDownloadFailure.cs`
- FOUND: `Assets/Tests/Editor/Chat/MediaDownloadFailureTests.cs`
- FOUND: `Assets/Scripts/Main/ChatManager.cs` (modified)
- FOUND commit: `4739e48` (test RED), `69f2f37` (feat GREEN), `9e4e614` (fix retry), `d665e3b` (chore metas)
- No file deletions introduced by any commit.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
