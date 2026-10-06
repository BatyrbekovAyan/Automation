# Independent read-only review — R4–R10

Review date: 2026-09-28. Product source compared from dbbeb0b1 through a3a788ce; subsequent HEAD 841d6cb7 changes inspected in final-report.md/release-package.md. Those later commits do not change the app/canonical pipeline reviewed here. No product files edited; no production calls or messages sent; focused runs use synthetic Code-node inputs and local Postgres session temporary tables. Main reviewer runs aggregate suites and Unity, not duplicated here.

## Matrix

| Requirement | Result | Evidence / remaining boundary |
|---|---|---|
| R4 stale/replayed Auto | Original defect fixed; clock-based acceptance incomplete | Applied-only CTE and client Reassert preserve prior decision version; stale/repeated/bot-wide requests do not clear new pauses under ordered clocks. Counterexample with phone clock five minutes ahead: a queued decision preceding a handoff closes it on first delivery. Final report mentions only slow-clock safe refusal, not this forward-clock unsafe case. |
| R5 boot queue | Original lazy-start defect fixed; reconciliation still depends on visible chats UI; device unverified | Runtime bootstrap, boot send gate, pending notification and stale-result version checks exist. A server-newer '*' adoption is persisted only to the queue when ReplyModeToggleBinder is disabled; BotNReplyMode remains old. Read-only source finding; no new Unity runtime test executed by this reviewer. |
| R6 ordering | Original B→A distinct-time case fixed; same-second current-message criterion incomplete | Actual canonical SQL/JS interleave shows A sent and B superseded when B is claimed between A's final snapshot and lease. Existing test explicitly calls lease acceptance an 'accepted residual'; own reproduction also exercises resulting send and loss of B. |
| R7 memory | Partial; not closed | Wide delete narrowed and ordinary send_failed cleaned. Exact-text/min(id) is still not event ownership: reverse completion with identical text deletes newer delivered memory, leaving the stale draft after an owner row. send_unknown remains ordinary assistant memory with no uncertainty annotation, contrary to the followup criterion (explicitly admitted engineering decision in final report). |
| R8 activation | Core fail-closed semi seed fixed; cleanup/retry boundary remains | Positive marker gates semi activation, retries SQL, returns explicit failure and attempts delete. Delete itself is continueRegularOutput with no durable recovery/binding: failed delete leads to terminal failure without clone id; retry can leave a second inactive orphan. Graph-only synthetic failure evidence, no real failed HTTP deletion. |
| R9 lost sends | Implemented; no further defect found in scoped review | Every-2-minute notifier converts >10-minute send_pending to send_unknown, no transport/reclaim path, execution identity retained. Depends on active notifier and configured execution timeout ≤300s; final report accurately names notifier activation dependency. No production/device validation performed. |
| R10 reconcile | Cross-chat original case fixed; not sufficient attempt/profile proof | Canonical JS rejects named foreign chat and missing-trigger/ambiguous windows. It ignores explicitly foreign profile_id; one such row alongside current trigger is adopted as sent. Sole unmarked manual reply also accepted (final report admits this). |

## Strongest actionable findings

### P2 R7 — text equality still deletes another generation's delivered memory

`Tools/n8n/apply-history-handoff.py:168–170` (and analogous Record Send cleanup at 147–149) selects the first unmarked same-text AI row after a chat watermark. An older slow generation O and newer faster N can both answer “Запрос передан менеджеру.” N writes and sends first, owner_phone writes an update, O writes last, then O is superseded. Record Cancel deletes N's delivered row, retains O's draft after the owner update. Both channels reproduced; JSON includes IDs and before/after ordering. Existing same-text regression only writes O before N and checks content count, so cannot catch reversed completion or wrong row identity. Bind memory to execution/event, or commit assistant memory when delivery is confirmed.

### P2 R7 — unknown transport result is represented as a confirmed assistant turn

`Tools/n8n/apply-history-handoff.py:144–149` only changes memory for send_failed. A socket-hang-up with no echo produces ledger send_unknown but leaves `type=ai, additional_kwargs={}` “Доставка завтра в 10:00.” in the same memory Chat Memory reads. There is no delivery-status mark/filter; the model is told this was said even if the transport never accepted it. This is explicitly documented, not concealed, but directly fails the followup's “send_unknown не выдаётся за подтверждённую реплику”. Keep uncertain attempts separately or surface uncertainty to memory readers.

### P2 R6 — equal-second newer event cannot revoke older send permission

`Tools/n8n/apply-history-handoff.py:66–69` leaves generation unchanged for equal timestamps. Actual interleave: A(100) passes final snapshot, newer B(100, “Нет, отмените заказ.”) claims, A is still allowed by lease and sends “Ваш заказ принят.”; B sees that outgoing row and is superseded. Evidence for both WA/TG: A=sent, B=superseded. Existing `test_reply_order.py` explicitly pins only the permissive lease as accepted residual; this is an acceptance gap, not an undisclosed bug. Use a shared latest-event identity/order across final snapshot and lease; changing > to >= alone would reintroduce reordered-event loss.

### P2 R5 — persist server-adopted bot mode independently of a visible header

`Assets/Scripts/Chat/ReplyModeSyncService.cs:213–217` confirms the queue and broadcasts adoption but does not update the actual bot preference. The ONLY '*' consumer is `ReplyModeToggleBinder.HandleServerNewer` (114–117), subscribed in OnEnable (234) and removed in OnDisable (246). `SuggestionsController.cs:506` explicitly ignores '*'; Bot.cs listens to the binder's OnReplyModeChanged, not OnServerNewer. If owner switches to Bots/Profile during a pending request, a newer server mode settles the queue but no longer updates `BotNReplyMode`; chip can become fully opaque displaying the stale choice, and reopening the header only reads that stale preference. Scene has one binder, under Screen_Messanger; scene ancestry saved in pipeline-r5-scene.json. Correction to early investigation: Chats IS the default launch tab, so a passive default startup has the listener; the gap requires navigating away/disabled chats UI before adoption. Source-confirmed, not device/runtime-reproduced here. Persistence belongs in an always-alive owner, with UI listening for repaint.

### P2 R10 — reject rows explicitly naming another profile

`Tools/n8n/apply-reply-pipeline-v2.py:167–177` checks chatId only. With the real trigger present, a sole matching candidate with `profile_id='OTHER_PROFILE'` is accepted as sent and its foreign id recorded, both templates. Profile id is explicit contradiction and should be rejected before candidate matching; no transport identity is inferred here. Related requirement gap: sole manual same-text row without from_where=phone is accepted. New final report openly admits that manual case; insufficient evidence still needs to remain unknown to meet R10. The synthetic foreign-profile case establishes missing guard, not a claim this precise crossed response was observed in production.

## Lower-priority / qualified boundaries

- R4 (`apply-reply-mode-v2.py:89,97–99`): first delivery of command from fast phone can close a handoff created after the real owner action; synthetic five-minute-forward version reproduced. Versions serve both monotonic ordering and wall-clock cutoff; neither version validation nor the queue bounds forward skew. Report as remaining clock assumption, not an exploit claim.
- R8 (`apply-reply-mode-v2.py:228–238`): failed cleanup is swallowed and clone identity omitted. Test driver supplies a failed Delete response; graph reaches Respond Reply Mode Not Saved with no later cleanup/bind. Core safety remains fail-closed (clone never activated); residual is orphan/retry recovery, P3. No proof of a live duplicate activation claimed.

## Reproduction and limitations

Run `python3 docs/audits/2026-09-28-review-evidence/pipeline-counterexamples.py` using existing staging Postgres and local n8n modules. Results captured in pipeline-counterexamples.json. The driver imports canonical workflows and executes actual SQL/Code bodies; generated AI output and transport snapshots/responses are synthetic. It does not simulate a real LLM, mobile lifecycle, HTTP retries or provider timing. Existing shared harness uses session-temp LIKE tables (public table definitions only); their serial defaults can advance local sequences but no persistent staging rows are changed. No production readback or device pass performed by this reviewer. Full aggregate PASS totals belong to main review, not this file.
