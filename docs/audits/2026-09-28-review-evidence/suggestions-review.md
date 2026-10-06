# Independent R12–R16 review (2026-09-28)

Read-only review of `dbbeb0b1..a3a788ce`; final report at `841d6cb7` also read. The latter changes no reviewed runtime code/canonical workflow. No production, staging or paid model calls; no unit/Unity suite duplicated. Untracked `cmp-0925-*` results were not used.

## Status

| Item | Assessment |
|---|---|
| R12 | Original counterexamples addressed: relevant items/variant levels are prioritised under the unchanged budget; full short descriptions preserved; cuts flagged and explained to model. Tests cover real Prep/Assemble and Auto expression. Client's 600-character clamp remains, now honestly flagged; full parity beyond this/budget is not claimed. Subject selection still depends on R13 below. |
| R13 | **Partial.** Newest-first budgets and token ranking fix original ordinary examples; full burst plus truncated history still loses the newest message. Reproduced in actual Prep for WA and TG. |
| R14 | **Partial, accurately labelled so by final-report.md.** Original evaluator defects fixed, honest h3 rescore and repeat runs exist, several answers improved. Price conflict and panel language/relevance remain. Treating server comparison as outside this task and locked rules/no generator as owner blockers does not satisfy the requested work. |
| R15 | Implemented with relevant unit and recorded staging evidence. Published 3200 beats draft 999; inactive draft is explicitly labelled; unreadable published prompt fails closed. No defect found in reviewed change. Device layout remains unverified. |
| R16 | **Partial.** Cross-bot/reopen sequence and per-bot verdict isolation work. Deletion/rebinding only protects LastTestOk, not screen/history/busy. |

## Confirmed findings

### P2 — R13: a long older burst fragment can evict the newest question

Location: `Tools/n8n/apply-suggest-v2.py:64` (newest-first packing assumes `runParts` chronological); canonical Suggest Replies `Prep` preceding the generated block contains per-line merge. Client premise is concrete: `N8nSuggestionsProvider.cs:290` sends the full `lastIncomingText`, while `:318` clamps each history message to 500 chars; `SuggestionsController.AppendBurst` preserves full fragment text.

Input: two consecutive client turns, older 617-char text starting `OLD54321`, newest `А NEW12345 есть?`. `messages` has the older turn clamped to 500 and the newest intact; `lastIncomingText` contains the two full lines in order. The merge's exact-string Set does not recognise the full old line as its clipped copy, appends it **after** the already-present newest message, and skips the newest as a duplicate. Newest-first packing now keeps the old line and breaks at 500. Both WA and TG actual Prep output `exactTokens=["OLD54321"]`; queryText/trailingText contain no NEW12345 or newest question. This also makes `lastClientMessage` and catalog relevance refer to the wrong question.

Fix the overlap/chronology merge before budgeting and cover real client-clamped history with an unclamped accumulated burst. Baseline also fails this input: this is an unclosed R13 case, not evidence of a newly introduced regression. Current tests' burst lines are 260 characters, so they do not exercise the asymmetric clamp.

### P3 — R16: deletion/rebinding does not invalidate screen writes

Location: `Assets/Scripts/Main/BotReadiness.cs:160-164`. `sameTarget` controls only `writable` (the stored verdict). `ApplyToScreen` is solely `ticket.Seq == currentSeq`. `BotTestSheet.Hide` (`:91-94`) does not bump the sequence. `OnResult` (`:169-194`) consequently clears busy, updates reply/meta, clears field, and appends history for a deleted or retargeted bot if no subsequent Show/send occurred.

Executed the **verbatim real policy** with Unity's bundled C# compiler/Mono (minimal input DTO stubs, no Unity/PlayerPrefs data). Same current sequence + deleted bot => `ApplyToScreen=True, Verdict=null`; same current sequence + changed workflow => the same. A newer screen sequence correctly returns false. Existing `DeletedOrRetargetedBot_GetsNoVerdictFromALateAnswer` asserts only LastTestOk, leaving this branch green. Gate screen application by live matching target and closed-screen validity too; retain the separate legitimate per-bot verdict policy. Add screen/history/busy assertions after deletion/rebinding and a callback after Hide.

### R14 completion gap — requested price/language/relevance fixes remain available engineering work

The final report correctly says “Частично исправлено”; no false all-complete claim is alleged. `R14-prompts.md:25-31,96-100` matches saved answers:

- `ap_source_conflict`: 0/8 after (and before), e.g. `Фильтр ... — 3900 тг` plus stock caveat while catalog says 3200. Original R14 explicitly requires fixing price conflicts, so backend reconciliation is within its authorised scope, even if prompt-only edits are insufficient.
- `pn_emotional_reaction`: 0/3; the response supplies four friendly-business cards to the personal reaction. Existing Assemble abstention rule explicitly includes this example, and the later thanks rule contradicts its scope.
- `pn_kz_question`: 0/3; four entirely Russian cards on a Kazakh question, despite existing rule that the whole card uses the customer's language.

The loaded sketch skill locks visual cards/chassis/interactions, not a right to answer Kazakh in Russian. The user's instruction preserves approved *policy* and disallows changing test expectations; it does not forbid repairing the implementation to comply with that policy. The missing generator is an implementation gap (create the source and regenerate), not a missing user decision. The original request explicitly includes these residuals. Actual ambiguous stock wording/business-thanks policy can be kept separate.

## Independent evaluation verification

`python3 Tools/n8n/eval/test_checks.py`: 21 methods, OK, **5 expected failures**. Table classes contain **127 rows**. Mandatory positive/negative cases produce the requested verdicts (free delivery false claim fails, negation passes, stock disclaimer does not resolve price).

Offline recomputation of saved output with the current actual h3 check/check_panel agrees exactly:

| File | Runs | pass / flaky / fail scenarios |
|---|---:|---:|
| historical before-v1.1, rescored | 111 | 52 / 3 / 8 |
| historical after-v1.1, rescored | 111 | 53 / 6 / 4 |
| r14p-before-v1.1 | 189 | 52 / 3 / 8 |
| r14p-final-v1.1 | 189 | 59 / 1 / 3 |
| r14p-before-x5-auto | 70 | 6 / 5 / 3 |
| r14p-final-x5-auto | 70 | 7 / 6 / 1 |
| r14p-final-x5-panel-flowers | 15 | 3 / 0 / 0 |

Historical after changes exactly 3 h2 verdicts: two price-conflict outputs and one free-delivery claim. Historical panel results (20 each) have no saved status, so are carried, not independently recomputed; recent results have full status/body and all recompute. Original files unmodified.

These counts measure h3, not complete answer correctness. `lang_kz` (`run-eval.py:508`) merely requires two Kazakh characters; “8/8 exact SKU KZ” includes Russian factual sentences. Known checker gaps remain explicit: `Доставка — 0 тг.` passes the free-delivery check; correct `Есть расхождение по цене, точную цену подтвердит владелец` fails because `_CONFLICT_STRONG` misses `расхожд`. Reports disclose these limits; improvement cannot be described as complete language/claim correctness.

## Evidence

- `suggestions-review.py`: reruns saved-answer checks and real Prep burst probes without network.
- `suggestions-results.json`: exact counts, probe output, selected saved responses (no production data).
- `suggestions-policy.cs`: verbatim policy under test plus minimal synthetic DTOs.
- `suggestions-policy-output.txt`: compiled policy observations. Compile with Unity bundled Mono `csc.exe`, then run output executable with bundled `mono`; no Unity Editor needed.
