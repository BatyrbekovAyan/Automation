# «Личные и рабочие чаты»: отложенные мелкие замечания

Мелкие замечания ревью, которые в этой ветке не исправлялись: каждое ревью признало их «может подождать». Список — для будущей
уборки; до выкладки исправлять их не нужно. Решения о том, что отложить, — в `rulings.md` (4.8, 5.25, 6.15 и рядом). Известные
ограничения, описанные в CLAUDE.md («Known, open» (a)–(i)), здесь не повторяются.

Как читать:
- Раздел 1 — список отложенного из рабочего журнала, строки 1–106. Ссылки на файлы и строки в нём — на момент записи; строки могли
  сдвинуться.
- Разделы 2 и 3 — разбор этого списка финальным ревью ветки (сервер и приложение): вердикт по каждому номеру и новые мелкие
  замечания ревьюеров. Тексты ревьюеров — на английском, как они были написаны.

Что закрыто после разбора:
- были неверны или уже исправлены к моменту разбора: 5, 6, 10, 36, 51, 79, 90 и часть 31 про экран «Кто из них клиенты?»;
- исправлены волной исправлений: 20 (удаление — первая ветка Sweep), 21 (список таблиц удаления выводится из линии
  ИИ-сотрудника), 30 и 48 (чат без решения показывается по самому раннему последнему сообщению), 46 (группы не запрашивают решение),
  56 (лечение режима — только в рабочем чате), 61 (окно не обещает «сразу ответит» для голосовых и реакций), 67 (карточка удаления
  закрывает папку целиком), 74 (чек пейволла без личных чатов), 82 (отрицательные проверки Decide Audience), 92a (сравнение «Авто» и
  панели делает свой чат рабочим), 94–95 (фраза CLAUDE.md о журнале; абзац политики — черновик на одобрение владельцу), 97b (три
  пути запуска часов удаления в комментариях), 100 (фраза поиска в логе для проверки на устройстве);
- строки 78, 93, 98 и 99 — записи о ходе работы, не замечания.

Из новых замечаний ревьюеров закрыты: сервер — M1, M2, M6, M8, M9 исправлены, M10 описан в CLAUDE.md (Known (h)); приложение — m-3,
m-4, m-5 исправлены. Открыты: сервер — M3, M4, M5, M7; приложение — m-1, m-2. Повторные ревью волны исправлений добавили три
открытых пункта:
- `deploy-canonical.py` выкладывает обязательный, но выключенный workflow, который отличается только настройками, а предупреждение
  «INACTIVE although it must run» показывает лишь при следующем запуске;
- текст причины в проверке входа WhatsApp (`verify-history-handoff.py`) говорит о типе чата, хотя запрещает читать `is_bot`;
- `audience_cache.json` хранит id каждого показанного чата до полного удаления данных: кнопка «Очистить историю чатов» его не трогает
  намеренно (ранние времена нужны показу чатов без решения), а профили, заменённые переподключением, остаются в нём до полного
  удаления.

Всё остальное с вердиктом CAN WAIT в разделах 2 и 3 — открыто.

## 1. Список отложенного (из рабочего журнала)

1. Task 1: minor (deferred): empty/missing WhatsApp chatId reads as a person (audience.py:74) — add `id === '' → personal/unknown`.
2. Task 1: minor (deferred): RULE_JS comments say «the Decide Audience Code node»; the Code node is `Decide` inside the «Decide Audience» workflow (audience.py:28, :55, test :6-7).
3. Task 1: minor (deferred): test oracle STEPS/fires is a third copy of the step logic (test :146-165) — add a pointer comment in RULE_JS.
4. Task 1: minor (deferred): fixture loaded at import without a guard (test :34-36).
5. Task 1: minor (deferred): no fixture row pins the exact case of the non-human literals (e.g. chatType "Channel" → personal/unknown); a case-insensitive twin would pass the table — carry «exact, case-sensitive» to Task 10.
6. Task 2: minor (deferred): sql:4–5 / audience.py:37–38 claim every statement over the two tables lives in audience.py — false after Task 6 (bind, DELETE_NEW_TAIL); reword.
7. Task 2: minor (deferred): the reply_events CHECK guard keys on the constraint name only (sql:56–60); add a prod post-check `select count(*) from pg_constraint where conrelid = 'public.reply_events'::regclass and contype = 'c'` (expect 1) to the Task 18 package.
8. Task 2: minor (deferred): OWNER_SET comment says kind/version are the server's after the statement — for not-applied/refused chats it is the pre-statement snapshot (audience.py:253–254); reword like build-set-schedule.py:101.
9. Task 2: minor (deferred): BACKFILL re-implements BODY_TEXT in SQL without a parity check (audience.py:388–391); msg_type, empty type → [media], the 4000/1000 clamps and a time-less message are not pinned.
10. Task 2: minor (deferred): audience.py:368 and test_audience_sql.py:578 cite «ruling R7» — the ledger is gitignored and CLAUDE.md already uses «R7» for reply-memory ownership; drop the tag (the contract is spelled out). Future dispatches: never cite ruling numbers in code/comments.
11. Task 6: minor (deferred): if the new profile's own connection row is the earliest and has no number, it keeps none (no coalesce with the predecessors' known number) — near-impossible (an own pre-bind row has a later moment).
12. Task 6: minor (deferred): with several predecessors the number is compared with the EARLIEST row's, not the newest — a false «number change» voids decisions (safe direction); several predecessors with rows are rare.
13. Task 6: minor (deferred): an own row created before the bind keeps its late moment when no predecessor has a connection row — later moment = more chats read «old» + a 30-min settle after a reconnect (safe direction).
14. Task 6: minor (deferred, final review should weigh): digit-normalising helper copied verbatim — clone_lineage.py:446-448 duplicates audience.py:312-314; import audience's helper (no cycle).
15. Task 6: minor (deferred, final review should weigh — touches the «new client left without an answer» anti-goal): a predecessor WITHOUT a connection row leaves the new profile's pre-bind row at its own late created_at (clone_lineage.py:400-417); chats begun in the old profile's era then read «переписка до подключения» → personal. Fix: when no predecessor row exists, lower to least(own.connected_at, min bot_profiles.created_at over the predecessors). Practically only for bots created by a pre-feature client (prod has none).
16. Task 6: minor (deferred): non-human rank ignores validity — after a number change a moved, voided computed non-human row still replaces a holding owner row (clone_lineage.py:389-395); only a Telegram self-chat id reaches it.
17. Task 6: minor (deferred): the new verify-auth-gate parity rule (:399-405) is never exercised — every N2 mutation trips the earlier stale-condition assert (:398); add a negative that appends a counter to Delete Chat Memory without touching the verdict.
18. Task 2: minor (deferred): OWNER_SET_SQL accepts chatId '*' (the bot-wide reply_mode_flags key) — refuse it in the statement too.
19. Task 8: minor (deferred): the wipe bumps updated_at (audience.py:494); the reconnect merge orders owner rows by updated_at desc (clone_lineage.py:389) — a wiped older owner «personal» could outrank a newer owner «business» on another predecessor; set only wiped_at, or order owner rows by version.
20. Task 8: minor (deferred): canvas position (-680, 760) reads as the head of the V12 clone branch (apply-personal-wipe.py:40, :55-62).
21. Task 8: minor (deferred): CHAT_TABLES hard-coded in verify-personal-chats.py:58 and test_personal_wipe.py:63 — tie to clone_lineage.MOVED_TABLES minus bot_schedule/chat_audience/audience_connections.
22. Task 8: minor (deferred): the verifier does not pin profile+chat scoping of each delete, session equality vs prefix, or the oldest-first order (verify-personal-chats.py:150); the docstring overstates.
23. Task 8: minor (deferred): test copies the verifier's graph helpers verbatim (test_personal_wipe.py:388-406 vs verify-personal-chats.py:67-85) — call check_wipe instead.
24. Task 5: minor (deferred): the Handoffs webhook still lists open requests of chats made personal (rows go after the 2-day wipe; the app's Dashboard filter hides them).
25. Task 5: minor (deferred): Gate Decision comment «Order matters: the chat first» vs the missing_app_user_id check first (apply-suggest-v2.py:714/:718); `failOpen`/`gateFailOpen` now means «the statement did not run» — a refused request shows gateFailOpen: true; fix the comment, rename with apply-learning-a's return-shape pin.
26. Task 10: minor (deferred): a Telegram `user` dialog with no `user` card reads (JsonUtility) as known all-false → an undecided chat can SHOW as business/new while the server says personal/unknown; display + notification only (the panel uses server decisions).
27. Task 10: minor (deferred): IsOwnWhatsappChat re-extracts the stored number's digits per row (ChatAudienceRule.cs:260) — compute once per parse.
28. Task 10: minor (deferred): TheCachedChatList_GivesTheSameFactsAsTheLiveAnswer only proves determinism (ChatDialogAudienceParseTests.cs:529-552); the source guard at :555 is the real pin.
29. Task 10: minor (deferred): subline kind decided by elimination — a new server literal silently becomes Channel (ChatAudienceRule.cs:206, :217); make the kind explicit per literal.
30. Task 10: minor (deferred, known approximation from the plan): the history estimate uses the LAST message only — an old non-contact chat with post-connection activity shows as business/new until the server decides, and is absent from the onboarding selection; record for Task 11 ViewOf docs.
31. Task 11: minor (deferred): a 409 (not_authorized) from `connected` is retried; after a refusal the store briefly holds a placeholder until the list read 2 s later; AllowsAssistant is true while a move to business is pending (as defined); re-pairing the same number after an outside logout requests onboarding again (harmless); Manager.ReplyModeSync.cs touched to open the send gate.
32. Task 11: minor (deferred): Kick()/ReadList(force) in Awake never run (isActiveAndEnabled false before OnEnable) — ChatAudienceSyncService.cs:142-143; the test at ChatAudienceWiringTests.cs:300 pins a no-op; same dead pattern pre-exists in ReplyModeSyncService/ScheduleSyncService; move to Start() or let OpenSendGate start ListLoop.
33. Task 11: minor (deferred): ApplyConnected keeps computed decisions when the moment moves later (ChatAudienceStore.cs:88-95) — drop non-owner decisions when connectedAtMs grows.
34. Task 11: minor (deferred): mirror entries of replaced profiles outlive the bot in audience_cache.json (ChatAudienceSyncService.cs:215-227, :252-265) — prune profiles no bot holds.
35. Task 11: minor (deferred): a refused non-human chat with no server row is cached as an owner decision until the next list read (ChatAudienceStore.cs:124-130).
36. Task 11: minor (deferred): the «capture at pairing start» ruling depends on Unauthorize* clearing the number only after its first yield (BotSettings.Auth.cs:329-345, :549-564) — add a source guard.
37. Task 11: minor (deferred): SendConnected drops a report for good when the token does not list the profile (ChatAudienceSyncService.cs:336-343) — one ForceRefresh first while a bot holds the profile.
38. Task 11: minor (deferred): duplicated Arm/OnSendFailed/IsPending for Desire and Connection (ChatAudienceSyncQueue.cs:369-396); SendConnected 48 lines, SendSet 37; OnActiveBotChanged subscribed in Start/OnDestroy.
39. Task 11: minor (deferred, privacy second line): audience_cache.json is missing from LocalDataWipe.DiskWipeTargets (LocalDataWipe.cs:22-28) — the full wipe relies on ClearAll, whose IO failure is only logged.
40. Task 12: minor (deferred → device pass): grey counter contrast (InkTertiary fill + white text 3.69:1 dark) below 4.5:1 for 32u text (PersonalChatsBuilder.cs:280,292; ChatItemView.cs:599-600).
41. Task 12: minor (deferred): the content-width rounded counter pill under the Viewport's stencil Mask has no RoundedCornerMaskSync (inherited from UnreadBadge) — corners can lag when the width changes.
42. Task 12: minor (deferred): ChannelSwitcherView does not follow per-view-model moves between working and personal (:71-79).
43. Task 12: minor (deferred): no behavioural/source test for ChatListView's filter/lock/row wiring, ChannelSwitcherView.ActiveUnreadTotal or ChatItemView.SetAudienceMarks (spec §8 lists the list filter and counters).
44. Task 12: minor (deferred): BotChatStats builds a full ChatViewModel per cached dialog (:86-91) and ProfileIdOf repeats ChatManager helpers (:95).
45. Task 12: minor (deferred): builder comments overstate «nothing under it wakes while authored» (Nobi materials for Circle/Counter; Divider baked at 1.6875).
46. Task 12: minor (deferred): AwaitsServerVerdict (ChatManager.cs:495-497) also fires for group chats the server never decides → wasted forced list reads; also require ViewOf(...).NonHuman == None.
47. Task 12: minor (deferred): RequestListSoon is a fixed window, not a trailing debounce (ChatAudienceSyncService.cs:533-544) — a late request in the window is read < 3 s after its message.
48. Ruling (fix before merge, final wave): ViewOf's «last message» history estimate flips a pre-connection chat to business on its first post-connection message and keeps it there until a server decision is mirrored — during the 30-min settle window (no decisions) every later message from old acquaintances rings and the chat sits in the working list. Fix: ChatAudienceStore records, per (profile, chat), the EARLIEST LastMessageTime it has ever seen (persisted in audience_cache.json); the estimate becomes «earliest seen < connectedAt» — chats loaded before their first post-connection message stay personal — cost if wrong: noisy first 30 minutes after a connection (no AI impact).
49. Task 13: minor (deferred): ChatRowSwipePolicy.InFolder (:28) treats a null view opposite to PersonalChatsListModel.IsPersonal (:15) — reuse IsPersonal (cannot trigger today: ViewOf never returns null).
50. Task 13: minor (deferred): Actions no longer reads inFolder (:21-22) — half of the 24-row matrix pins a no-op parameter; drop it after Task 14.
51. Task 13: minor (deferred): ChatListView.RequestMove is public but only subscribed inside the class (:323) — make it private.
52. Task 13: minor (deferred): the toast repeats the queue's acceptance checks (ChatListView.cs:328 vs ChatAudienceSyncQueue.cs:143,146) because MakePersonal/MakeBusiness return void — return the accepted flag.
53. Task 13: minor (deferred): stale assertion message ChatRowSwipeWiringTests.cs:51; no test for the delete listener on a Telegram hidden «Удалить» / a WhatsApp non-person (ChatItemView.cs:197); :76 checks activeSelf only.
54. Carry to Task 17 (device pass): a move without a search hides the row at once (no close tween; ChatItemView.cs:594) while delete collapses over 0.2 s; the open threshold doubles to 200u on two-button rows (SwipeToDelete.cs:194); PersonalChatsBuilder.cs:39 diagram says MoveButton is parked at rest (it is authored at x=0, re-placed at runtime).
55. Task 15: minor (deferred, visible glitch): the hidden chip is painted from the forced-false mode, so when it appears mid-chat it can flash a lit «Авто» for 0.22 s depending on handler order (SuggestionsController.cs:365-368, MessageHeaderView.cs:214) — paint the hidden chip in the stored mode (WouldSuggestIgnoringAudience) or let the controller own its activation; the test at PersonalChatSheetsWiringTests.cs:377 checks only the final state.
56. Task 15: minor (deferred): the chat-open reply-mode heal still runs in personal/undecided chats (SuggestionsController.cs:375-376) and re-creates the per-chat reply_mode_flags row the 2-day wipe deletes — guard with OpenChatAllowsAssistant().
57. Task 15: minor (deferred → device pass): the scrim dims the header and the source control is not highlighted (sketch keeps the header undimmed, outlines the pill/name) — PersonalChatSheets.cs:462-470; sheet B gaps (hidden number line leaves 80u; null lockGlyph keeps the 120u indent — :145, :147).
58. Task 15: minor (deferred): « · » composed outside PersonalChatsCopy (:184); a literal invisible U+200B (:46); title dedupe relies on the emoji converter's prefix (:179).
59. Task 15: minor (deferred, flakiness risk): fallback tests read UsageStore.Current and the bot's schedule without resetting them (SuggestionsAudienceGuardTests.cs:175, PersonalChatSheetsWiringTests.cs:348); «no fallback panel in a personal chat» pinned only by a source-order guard (:66).
60. Task 15: minor (deferred): OpenChatFixture.cs:12-13 comment says every GameObject goes to the caller's list — the bot's own one is destroyed via its parent; ChatDialogAudienceParseTests.cs:296-326 still has its own bare-manager + ParseChatsJson copy (needs a channel parameter on BareChatManager).
61. Ruling (client, final wave or Task 14): voice (ptt) and reaction messages are never replayed (Wappi rows carry no audio; file_link only via media/download) — MakeBusinessCopy must show «будет отвечать сам на новые сообщения» (AutoNoFresh), not «сразу ответит», when the last client message is not a replayable type (replayable = chat/text/image/video/document/audio/sticker/location/vcard, the server's REPLAY_TYPES) — cost if wrong: the sheet promises an instant answer that never comes.
62. Task 7: minor (deferred): a failed replay (bot webhook inactive / 404) leaves the trigger message in neither history nor memory (Backfill skips it to avoid a duplicate) — run the replay first and pass the trigger to Backfill only when the replay was not accepted.
63. Task 7: minor (deferred): crossed Wappi pages are only reduced by 300 ms spacing (a crossed page is dropped — no backfill, no replay for that chat); `connected` with no live row → 400, which the app treats as rejected (no retry).
64. Task 7: minor (deferred): multi-chat sets keep several messages/get in flight on the shared Wappi token (BATCH staggers starts only, :93-96) — crossed pages are dropped (no backfill/replay for those chats); use a strictly sequential loop (fit the 120 s cap or raise executionTimeout ≤ 300 s).
65. Task 7: minor (deferred): `restarted` reads prev from the statement snapshot (audience.py:337-338, :350) — a concurrent first write (Create's Bind moving the predecessor row) can move the moment while the answer says false; take the evidence from the conflict arm (`returning … (c.xmax <> 0)`).
66. Task 7: minor (deferred): test_chat_audience.py `_Engine` (:174-188) copies rc_events_chain.py:83-100 verbatim and repeats the executeQuery value rule (:147-154); G9 hand-rolls reachability; G5 says «COMMITTED» but reads on the same session (order only).
67. Task 14: minor (deferred, final wave): the delete card is not fully modal over the folder — the folder takes MessagesPanel's rect (208u below the screen), the card keeps the screen's stretch, so in Browse the Footer, a ~38u sliver of rows and the SwipeBack strip's bottom stay live; a second tap there opens a chat over the invisible-but-open card and the next Back closes the card (PersonalChatsBuilder.Folder.cs:135, :462–473, :444–458; ChatItemView.cs:619) — stretch the card's scrim to the open chat's rect or block that strip while the card is open.
68. Task 14: minor (deferred, final wave): PersonalChatsFolderView.cs:98–99 FindFirstObjectByType(Include) (cached; AddBotPanel/PaywallController precedent) — have ChatListView hold a builder-stamped reference.
69. Task 14: minor (deferred, final wave): a stale own-request marker (its `connected` answer never arrived) swallows one later restarted:true for the same profile when the client also could not see that change itself (declared; the next owed pairing overwrites it, ForgetBot clears it).
70. Task 14: minor (deferred, final wave): ChatListView class summary (:17-19) does not list the folder's RequestOnboardingCheck among the TryShow triggers.
71. Task 14: minor (deferred, final wave): Manager.LastAuthed{Whatsapp,Telegram}Number is the shared auth input's text when the status carries no phone — a typed 10-digit number against a stored 11-digit one reads as a number change (one extra, dismissable screen); the persisted baseline inherits it (the stored {bot}{Ch}Number had the same property) — compare by a normalized suffix or keep only status phones.
72. Task 16: minor (deferred, final wave): BotChatStats.ShownChats keeps the first twin and counts a chat whose later twin is deleted, while ParseChatsJson merges into the later twin and removes on a later deleted twin (BotChatStats.cs:85–94 vs ChatManager.cs:336–404) — walk in order with a chatKey dictionary, or soften the doc.
73. Task 16: minor (deferred, final wave): no FindBotByName check before SetActiveBot in the row's coroutine (BotSettings.PersonalChats.cs:73–82); a destroyed screen compares equal to null under Unity's == — unreachable in practice.
74. Task 16: minor (deferred, final wave): PaywallController.cs:663–675 reads the «Сводка» cache unfiltered — the trial receipt («заказов собрано») still counts orders from personal chats; run it through DashboardAudienceFilter.WithoutPersonal.
75. Task 16: minor (deferred, final wave): DashboardPage.cs:168–171 respawns the open drill-down list on every audience change (resets its scroll), and that branch is unpinned.
76. Task 16: minor (deferred, final wave): BotChatStats.cs:52–53 runs Tally outside the try/catch while the class doc promises «degrade to zeros».
77. Task 16: minor (deferred, final wave): the settings row's GameObject name «PersonalChatsRow» duplicates the chat list's «Личные» row name (PersonalChatsBuilder.Settings.cs:33 vs PersonalChatsBuilder.cs:72) — cosmetic.
78. Status (2026-10-26): every task that does not wait for the live checks is complete — 1, 2, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 16 (HEAD 39584854). Waiting for Task 0 with the owner (§7 checks 1–10 + D1–D6, prod journal read with permission): then 3, 4, 9, then 17 and 18; then the final whole-branch review and its fix wave (the «fix before merge» rulings + the deferred minors). Nothing deployed, nothing pushed; main untouched. Reported to the owner.
79. Ruling (fix before merge, Task 7 + carry to Task 3): Replay Plan drops the whole page when a row names another chatId and hands on only a row whose `from` is the chat — a WA chat whose messages/get rows carry its @lid code (2 of the 4 chats read) therefore gets neither the backfill nor the §4.5 replay (safe: nothing written, nobody answered; but the feature is silently off). Server-only fix: a row belongs to the chat when its chatId is the chat's number OR its code; the code comes from the webhook's whatsapp_chat_id (Decide Audience gets it as an input) and, in ChatAudience, from the chat card (chats/filter by the number's digits, dialog.chat.LID — one WA request on a business switch); the replayed message is built with chatId = from = the decision's chat id (the template's group guard and Read Reply Mode keep their key) — cost if wrong: one more Wappi call when the owner makes a WA chat business.
80. Task 3: minor (deferred, final wave): graph_walk.py:256-259 wraps a top-level DML+RETURNING in `with __gw_dml as (…)`; Postgres refuses that when the statement's own WITH holds a data-modifying CTE, so the walker hands on an error item where n8n returns rows (latent; no current node).
81. Task 3: minor (deferred, final wave): verify-personal-chats.py:1091-1096 emission check matches only literal `kind:`/`reason:`/`source:` keys (misses spreads, Object.assign, quoted keys, property assignment; false positives on ternaries/destructuring) — not exploitable alone.
82. Task 3: minor (deferred, final wave): test_decide_audience.py:983-986 — the two new scratch negatives cannot fail (check (1) rejects any hand edit; they assert only rc != 0 + 'FAIL [decide]'); assert the mutation's own message or drop them.
83. Task 3: minor (deferred, final wave): graph_walk.py:412-416 docstring («without onError the execution ends» — n8n also passes the input on for legacy continueOnFail: true); pre-existing graph_walk.py:273/:327 ignore continueOnFail and route continueErrorOutput's in-runner error item to output 0 instead of the error output (rc_events_chain honours continueOnFail); no workflow under test uses either.
84. Task 4: minor (deferred, final wave): apply-outcome-stamps.py:79-81 — a pass-through of Record Suppressed / Claim Event on the Decide path stamps 'business' (Decide's own outcome) instead of 'other:Record Suppressed' / 'duplicate'; ignore resp.outcome === 'business' in the recorded-state fallback + a case next to B1 G5.
85. Task 4: minor (deferred, final wave): tests/graph_walk.py:169, :501 turns an undefined sub-workflow input into null (n8n assigns undefined), comment :146 says undefined; triggers ≥ v1.2 (all runs of the last node) not modelled — document as limits or model them.
86. Task 4: minor (deferred, final wave): apply-history-handoff.py:407 GATE_POSITIONS uses WhatsApp canvas coordinates for both templates (editor readability only).
87. Task 4: minor (deferred, final wave): apply-history-handoff.py:193 and apply-reply-pipeline-v2.py:106 name the CTE `aud`, the same as audience.py's inner alias (correct only by shadowing) — rename the CTE.
88. Task 4: minor (deferred, final wave): tests/test_reply_audience.py:427 labels the pass-through throw 'getWorkflowInfo: boom' — the pass-through source is getCurrentWorkflowInputData (label only).
89. Ruling (Task 4 risk 5 / concern 4): accepted consequence — nothing from an undecided chat is stored (spec), and when the chat turns business BACKFILL_SQL restores the owner's fromMe messages from Wappi's recent page as owner_phone history + a «Владелец: » memory row, so the owner's opener comes back while the page holds it; self-messages (Handoff Notifier, Channel Watch) go to the owner's own chat, which nothing reads — cost if wrong: an opener older than the page is missing from the AI's memory. Residual for the final wave (Task 2): backfilled app sends are labelled owner_phone.
90. Ruling (Task 9 scope): the Task 7 @lid replay fix moves from the final wave into Task 9 — the mock must reproduce WA rows under @lid for a number-listed chat, so WhatsApp `make_business_fresh_replies_once` cannot pass without it, and a fix belongs with the test that proves it — cost if wrong: Task 9's diff grows by one server change (crChatAudience01) and its offline tests.
91. Ruling (Task 9 minors deferred to the final wave): Minor 4 (deterministic hold instead of sleep in make_personal_cancels_inflight), Minor 6 e2e part (Telegram message.reply in owner_app_send; the «Владелец: » mark on the stand), Minor 7 (seed the group business so after_group exercises the readiness group exclusion on the stand), Minor 8 ids (p / eval run-scoping); Minor 9 accepted (pre-existing ResourceWarnings) — cost if wrong: an occasional false failure of one scenario until then.
92. Final wave (from the Task 9 re-review): (a) eval/results/agreement/cmp-1013-auto-vs-panel.py:83 runs the panel for eval / eval@c.us without the set-audience seed — the panel now refuses, the tool prints «панель молчит» verdicts — seed like run-eval.main; (b) probe-billing Part 9 check() passes when the gate reason is unreadable (a staging run without N8N_API_KEY falls back to the prod key file → 401 → wire-only) and _harness_sql swallows a failed seed — FAIL when a wanted reason is unreadable, loud seed failure, no prod key file for a staging base; (c) e2e.py sr_no_identity_refused (:223-224) and bot_callback_binding (:328) can hand a list to passed += ok — wrap in bool().
93. Ruling (order): 17 → 9b (server, brief task-9b-brief.md) → 9c (client) → final whole-branch review with ONE fix wave → Task 18 (package + rehearsal) → finish. Task 18 moves after the final wave because the wave already holds server-canon fixes (Wipe Personal Chats first in the Sweep, the outcome-stamp label, …) and the rehearsal must run on the final canon; Task 18 keeps its own task review — cost if wrong: the whole-branch reviewer does not see the deploy README.
94. Task 17: re-review (a59ee289b1a2c1124, sonnet) — all findings addressed, no new Critical/Important; new Minors: CLAUDE.md:52 «and in failed executions» unsupported (the Outcome stamp is terminal; a failed run stops before it) → final wave; privacy.html:70 «только отметка» also keeps dialog_counts by design → the owner decides when approving the wording. Out of scope: apply-learning-b.py READINESS_SQL comment uses «personal chats» in the old one-to-one sense (deployed JSON; touch only if Get Usage changes).
95. Final wave additions (from the Task 17 re-review): CLAUDE.md:52 drop «and in failed executions» (the Outcome stamp is terminal; a failed run never reaches it).
96. Task 9b: review (a8230e9f90892ca44, opus) — Approved, spec ✅: differential fuzz of the real node bodies (12,000 + 6,000 runs) — the only behaviour change is accepting this chat own @lid code on WhatsApp; crossing guard, Telegram and every other rule unchanged; Decide changes only toward the safe side; full regeneration byte-identical. Minors → final wave: (1) test_reply_reconcile.py:202-206 the two LID echo cases assert only send_unknown — add want_error «ambiguous echo» / «recorded as owner_app»; (2) verify-reply-pipeline.py:405-406 print the returned tripwire counts; (3) IDENTITY_CLAUSES / TEMPLATE_CHANNEL / mutation tables duplicated in verify-message-batching.py and verify-reply-pipeline.py — one shared table; (4) e2e-audience.py:590 count every send call since the mark, not only sends_to the number. (5) carried to 9c.
97. Final wave (from the Task 9d re-review): (a) rule (i) lets a moved computed row that no longer holds replace a holding own row (clone_lineage.py:489-491) — add «excluded.decided_at >= the merged connected_at» to the computed term + a case; (b) audience.py comments (WIPE_SQL comment :523-526 inside the Sweep query — regenerate with apply-personal-wipe.py; docstring :71; WIPE_DAYS :86-87) name two clock paths, the bind is the third. Accepted residual: rule (i) cures the pre-bind misdecision only where the line has a decision for that chat.
98. PAUSED 2026-10-30 ~02:10 at the owner request («после 9d сделай паузу», weekly limits): Task 9d complete; HEAD 3546abcb, tree clean (only the environmental secrets.json.meta deletion). Stand stopped: staging n8n TERM, the mock ended with the 9d agent (TaskStop), Postgres pg.sh stop; nothing running. RESUME: (1) Task 9c — task-9c-brief.md + task-9c-carries.md, BASE 3546abcb, opus implementer (no stand needed; Unity full run at the end); (2) the final whole-branch review on the most capable model + ONE fix wave (all «Final wave» lines of this ledger); (3) Task 18 — task-18-carries.md (bootstrap our canon first; the price-list branch claude/friendly-elion-21cc7d may land on main first — regenerate, never merge JSON by hand); (4) finishing-a-development-branch. Owner decisions pending at the end: the privacy-line wording (option «…и учёт диалогов для тарифа», the policy version line), residual (a) (a number change keeps old business chats that never write again — wipe after 2 days or keep), the merge order with the price-list branch.
99. Ruling (final review model): opus — the newest Opus is the review default the official security-guidance plugin picks (it ignores the other family, Fable); Fable's tier is not documented anywhere I can read — cost if wrong: a less capable final reviewer than the best available. Deferred-minor/final-wave/parked lines collected for it: final-review-deferred.md (98 lines).
100. Task 9c: minor (deferred, final wave): device pass search phrase «discarded crossed messages/get response» misses GetMessagesRoutine's «crossed messages/get response … (attempt N)» (device-pass :322-323) — search «crossed messages/get response».
101. Task 9c: minor (deferred, final wave): a list read without chat.LID erases a learned code (ChatManager.cs:377/408, ChatViewModel.cs:116) — keep the last code that counted, replace it only with another code that counts.
102. Task 9c: minor (deferred, final wave): the string overload of CrossChatResponseGuard is test-only but its doc describes production use (CrossChatResponseGuard.cs:42-47) — reword or move the tests to ChatIdentity.ByIdAlone and delete it.
103. Task 9c: minor (deferred, final wave): IsForDifferentChat_AnIdentityWithoutACode_AnswersAsTheIdAlone cannot fail (CrossChatResponseGuardTests.cs:135; the overload delegates) — drop or re-aim it.
104. Task 9c: minor (deferred, final wave): ChatIdentity with a null chat id — Names(null) true, AdoptRows would rewrite rows to a null chatId (ChatIdentity.cs:65, :76; unreachable today) — AdoptRows returns 0 on an empty ChatId; the default-identity test name overstates.
105. Task 9c: minor (deferred, final wave): ThePremise_TheCacheStripsARowThatNamesAnotherId leaves the expected ChatHistoryCache strip warning unasserted (ChatPageIdentityTests.cs:337) — LogAssert.Expect.
106. Task 9c: minor (deferred, final wave): ParseChatsJson computes a chatCode for group rows too (ChatManager.cs:377); the server never runs chatIdentity for a group — leave isGroup rows without a chatCode.

## 2. Сервер — разбор финального ревью

### Deferred findings — triage (server half)

Line numbers are those of final-review-deferred.md. App-half lines (26–47, 48, 49–60, 61, 67–77, 100–107) are left to
reviewer B; lines 78, 93, 98 and 99 are status or rulings, not findings.

| Line | Item | Verdict |
|---|---|---|
| 1 | empty WA chatId reads as a person | CAN WAIT. Unreachable on the server: Plan Facts stops on an empty chat id before RULE_JS, and DECIDE_WRITE refuses `''`. Add a table row to pin the C# twin. |
| 2 | «the Decide Audience Code node» wording | CAN WAIT (cosmetic; RULE_JS is in three Code nodes: Plan Facts, Read Card, Decide). |
| 3 | test oracle is a third copy of the steps | CAN WAIT. |
| 4 | fixture loaded at import | CAN WAIT. |
| 5 | no row pins exact case of «Channel» / «Bot» | CAN WAIT. Only «User» is pinned; adding rows protects the C# twin's exactness and is cheap. |
| 6 | «every statement lives in audience.py» | WRONG / already fixed. sql:4-8 and audience.py:53-57 now name both exceptions. |
| 7 | reply_events CHECK keyed on the name | CAN WAIT for code. The original is the inline column check of sql/2026-09-17-reply-events.sql:20, so the name holds. Put the `contype = 'c'` count post-check into the Task 18 package. |
| 8 | OWNER_SET «server's after the statement» comment | CAN WAIT. Still at audience.py:336-337; it is the snapshot's value under concurrency. |
| 9 | BACKFILL re-implements BODY_TEXT | CAN WAIT (drift risk only). |
| 10 | «ruling R7» tags | WRONG / already fixed. No R7 or «ruling» remains in audience.py or test_audience_sql.py. |
| 11 | own earliest row without a number keeps none | CAN WAIT (near-impossible). |
| 12 | several predecessors: compare with the earliest | CAN WAIT (safe direction, rare). |
| 13 | own pre-bind row keeps its late moment | CAN WAIT (safe direction). |
| 14 | `_digits` copied in clone_lineage | CAN WAIT (import audience's; no cycle). |
| 15 | predecessor without a connection row | CAN WAIT. A predecessor with any computed decision always has a connection row (Decide's Ensure). Without one, only chats that never wrote while it was active are affected. |
| 16 | nonhuman rank ignores validity | CAN WAIT (only a Telegram self id after a number change). |
| 17 | verify-auth-gate parity negative never trips | CAN WAIT (test quality). |
| 18 | OWNER_SET accepts `*` | CAN WAIT. Validate refuses it, and a `*` decision row is never read. |
| 19 | wipe bumps updated_at vs merge order | CAN WAIT (needs two predecessors with owner rows for the same chat). |
| 20 | canvas position | Superseded by **I6**: not cosmetic, the position decides whether the wipe runs (ruling progress.md:126). **MUST FIX** in the wave. |
| 21 | CHAT_TABLES hard-coded | CAN WAIT (no gap today), but see **M9**. |
| 22 | verifier does not pin scoping / order | CAN WAIT (real-SQL tests pin the behaviour). |
| 23 | test copies verifier helpers | CAN WAIT. |
| 24 | Handoffs lists open requests of personal chats | CAN WAIT. Known (b); the app filters; the wipe removes them; the Notifier's notices carry no client text. |
| 25 | Gate Decision comment vs order; `failOpen` name | CAN WAIT. The comment is wrong (the code checks the app user id first); CLAUDE.md states the real order. |
| 62 | failed replay leaves the trigger unrecorded | CAN WAIT. |
| 63 | crossed pages; `connected` with no live row → 400 | CAN WAIT. |
| 64 | multi-chat sets overlap on the shared token | CAN WAIT (safe side). Before onboarding sets get large, use a real sequential loop: n8n's HTTP batching only staggers starts. |
| 65 | `restarted` from the snapshot | CAN WAIT. |
| 66 | test_chat_audience `_Engine` copy | CAN WAIT. |
| 79 | Replay Plan @lid (fix before merge) | WRONG / already fixed. a900ebfb + 3beeb24a: Chat Code reads `chat.LID`, and Replay Plan and Decide use `chatIdentity`. |
| 80 | graph_walk wraps DML + RETURNING | CAN WAIT (latent harness limit). |
| 81 | emission check matches literal keys | CAN WAIT. |
| 82 | two scratch negatives cannot fail | CAN WAIT, but fix in the wave: a test that cannot fail misleads. |
| 83 | graph_walk docstring / continueOnFail | CAN WAIT. |
| 84 | stamp says `business` on a pass-through | CAN WAIT (label only). It also falsifies a CLAUDE.md sentence (**M8**). |
| 85 | undefined → null; trigger ≥ v1.2 | CAN WAIT. |
| 86 | GATE_POSITIONS (WhatsApp coordinates in both templates) | CAN WAIT. |
| 87 | CTE `aud` shadows the inner alias | CAN WAIT (correct by scoping; rename for robustness). |
| 88 | test label | CAN WAIT. |
| 89 | backfilled app sends labelled owner_phone | CAN WAIT (both are owner sources; no learning origin is lost). The real mislabel is **M2**. |
| 90 | @lid replay fix moved to Task 9 | Done (verified in code). Not a finding. |
| 91 | Task 9 stand minors | CAN WAIT. |
| 92a | cmp-1013-auto-vs-panel.py has no business seed | CAN WAIT: documented in CLAUDE.md; fix before the tool's next use (it scores refusals as «панель молчит»). |
| 92b | probe-billing Part 9 passes on an unreadable reason; prod key fallback | CAN WAIT (staging-only tool). |
| 92c | e2e.py `bool()` wraps | CAN WAIT. |
| 94 / 95 | CLAUDE.md:52 «and in failed executions»; privacy «только отметка» | CLAUDE.md part: still present, fix in the wave (**M8**). Privacy part: see **I2** (owner wording before publishing). |
| 96 | 9b minors (1)–(4) | CAN WAIT. |
| 97a | moved voided computed row replaces a holding own row | CAN WAIT. Self-healing: the chat reads undecided and is decided again on its next message. |
| 97b | the bind's clock path missing from comments | CAN WAIT, but fix in the wave: WIPE_SQL's comment ships inside the deployed Sweep query (**M8**). |

#### Minor (Nice to Have)

**M1. A Telegram bot chat the owner marked business before the bot ever wrote is answered by the assistant.**
- The gate trusts the stored decision (`apply-history-handoff.py:400`, `BUSINESS_EXPR`) and never applies rule step 1 to
  the live webhook facts. `OWNER_SET_SQL` refuses only by id shape and by a recorded `nonhuman` row (`audience.py:362`).
- Ruling R9 accepted this because the app hides «В рабочие» for bots. That is the right primary line.
- A zero-cost second line: Business? and Decided Business? also require
  `!(M.is_bot === true) && !['bot','channel','chat'].includes(M.chat_type)` on Telegram. This also rules out an
  assistant↔chatbot loop that would burn quota.

**M2. After a wipe, the backfill attributes the assistant's own earlier replies to the owner.**
- Where: `audience.py:464-465, 499, 506`. Every `fromMe` row on the page that has no history row becomes `owner_phone` and
  an ai memory row opening «Владелец: ». Once the wipe has deleted a chat's history, the bot's old API sends that are
  still on Wappi's newest page get stored as the owner's words.
- The memory-marks rule then tells the model to treat named times and places in them as «подтверждённая договорённость».
- Fix: skip, or store as `outgoing_api` without the mark, the rows the transport marks as API sends (`from_where: 'api'`
  or a `task_id`), the same attribution V7 uses.

**M3. A connection row with no recorded number can never detect a number change.**
- Where: `audience.py:397-398`. `_ANOTHER_NUMBER` is NULL when `c.phone` is NULL, so the next report «fills in» the
  number instead of restarting.
- When it happens: Decide's Ensure created the row and the first `connected` report never landed (see the app's deferred
  line 37). Afterwards the old number's computed decisions keep holding on the new account, which is the unsafe
  direction.
- Fix options: let the app pass its own «number changed» verdict, or have Ensure read `get/status`.

**M4. Spec-level: the connection moment is the profile's creation, not its pairing.**
- `audience.py:257-268` follows spec §3.3: the moment is `bot_profiles.created_at`, which is profile.add.
- A long gap between creating the profile and scanning the QR shortens or removes the 30-minute settle window at the
  real pairing, while contacts are still syncing (live check 7: about 70 % of FullNames arrive after 2 minutes).
- Option: let the first `connected` of a line with no predecessors set `now()`.

**M5. A message refused `cancelled_personal` cannot be answered after the chat is made business again.**
- The replay is a duplicate claim: `CLAIM_SQL` re-claims only `claimed` / `generated` rows older than 10 minutes. Yet the
  sheet promises «сразу ответит».
- This needs «personal» while the reply is being prepared, then «business» again, so it is rare. Same semantics as
  `cancelled_mode` today.

**M6. The first message of a new chat waits longer than the spec says.**
- Decide adds two Wappi reads and two to three Postgres nodes (Ensure / Write / Backfill, about 1.9 s each on prod): about
  5–8 s, against the spec's «1–3 с».
- Put the real figure in the package.

**M7. `list` sends every holding decision of each profile on every read, with no delta.**
- An account with thousands of chats gets hundreds of KB per read, at launch, on return to the app, after writes and
  after messages from undecided chats.
- Add a `since` / version delta later.

**M8. Documentation that is no longer true.**
- CLAUDE.md:52 «… so that stamp shows only on staging and in failed executions». The stamp is the terminal node, so a
  failed execution never has one (deferred lines 94/95; still present).
- CLAUDE.md Part B «Semantics»: «a Record Suppressed that n8n failed hands its input on … and stays
  `other:Record Suppressed`». On the Decide path the input handed on is Decide's answer, which the stamp labels
  `business` (deferred line 84).
- `audience.py:72-74, :88-89`, WIPE_SQL's own comment (`audience.py:525-528`, which ships inside the Sweep's deployed
  query) and the README Sweep row name two clock paths; the bind is the third (deferred line 97b).

**M9. The wipe's table list is hard-coded, so a future per-chat table would escape the two-day wipe.**
- Where: `CHAT_TABLES` at verify-personal-chats.py:123 and tests/test_personal_wipe.py:75.
- A future per-chat table added to `MOVED_TABLES` and Delete Chat Memory would escape the wipe with every guard green.
- Fix: derive the list from `clone_lineage.MOVED_TABLES` minus bot_schedule, chat_audience and audience_connections.

**M10. On Telegram the chat card comes from the first `chats/get` page only (`TG_CARD_LIMIT = 20`,
`build-decide-audience.py:172`).**
- An account with many busy channels and groups can push the writing chat off that page between the webhook and the
  read. Its flags then come back null: personal_unknown, not written, retried on every message.
- This is a residual risk. Document it next to I1.

## 3. Приложение — разбор финального ревью

### Deferred findings — triage (app half)

Line numbers are those of `final-review-deferred.md`.

| Line | Verdict | Why / evidence |
|---|---|---|
| 5 | WRONG / already done | Exact, case-sensitive, untrimmed literals are pinned client-side (`ChatAudienceRuleTests` ~:252–280) and the table has `tg_chat_type_user_in_another_case_is_unknown`. |
| 26 | CAN WAIT | Display/notification only; a Telegram private dialog without a `user` card is rare and the server decides on the next message. |
| 27 | CAN WAIT | A few string ops per row per parse. |
| 28 | CAN WAIT | The source guard next to it is the real pin. |
| 29 | CAN WAIT | `TelegramNonHumanTypes` is pinned against audience.py; a new literal needs a code change anyway. |
| 30 | MUST FIX (as line 48) | Superseded by the ruling — see I-1. |
| 31 | CAN WAIT / partly WRONG | 409 retry is bounded (8/launch + re-arm); the refused-placeholder is replaced by the next list read; «AllowsAssistant while a move to business is pending» is by design (drawn at once); «same number after an outside logout asks again» is fixed by the last-paired-number key (30491aa5, pinned by `FreshProfile_WithTheSameNumber_OwesNothing`). |
| 32 | CAN WAIT | Real (`isActiveAndEnabled` is false in `Awake`, so `Kick`/`ReadList` there are no-ops), but `OpenSendGate` kicks the queue and the first `OnApplicationFocus(true)` reads the list; same pattern in two shipped siblings. Move both to `Start()` when touching the file; the assertion at ChatAudienceWiringTests.cs:289 then pins something real. |
| 33 | CAN WAIT | The list read 2 s after the `connected` answer replaces the voided decisions. |
| 34 | CAN WAIT | Cache hygiene (old profiles' chat ids stay in `audience_cache.json` until the full wipe); prune profiles no bot holds later. |
| 35 | CAN WAIT | A non-human is personal anyway; the next list read replaces the placeholder. |
| 36 | WRONG / obsolete | The capture-at-pairing-start approach is gone: the baseline is `{bot}AudienceNumber{Ch}`; `Settings_ReportThePairing_AgainstTheLastPairedNumber` pins that the settings never decide against the stored number. |
| 37 | CAN WAIT | Pairing itself needs the token to list the profile (every proxy op checks it), so the drop only fires for a profile that is really gone. |
| 38 | CAN WAIT | Duplication / method length only. |
| 39 | CAN WAIT | `ClearAll` deletes the file; `dashboard_cache.json` has the same gap — add both to `DiskWipeTargets` in one privacy pass. |
| 40 | CAN WAIT → device pass | Already listed in §6а.2. |
| 41 | CAN WAIT → device pass | Inherited from the unread badge; cosmetic, only under a stencil Mask. |
| 42 | CAN WAIT | Facts changes without a store change are rare (a contact saved meanwhile); the next refresh corrects it. |
| 43 | CAN WAIT (partly addressed) | Filter wiring and AddChat order are now pinned by source guards (`ChatRowSwipeWiringTests.TheList_GivesEveryRowItsPlace…`, `ChatRowAvatarTests.AddChat_DecidesVisibility_BeforeTheBind`) and the counters by the pure `WorkingUnreadTotal`; `SetAudienceMarks` / `ActiveUnreadTotal` themselves remain unpinned. |
| 44 | CAN WAIT | Small allocations on a settings/switcher open. |
| 45 | CAN WAIT | Comment wording. |
| 46 | CAN WAIT | The list parse runs only on navigation (no list poll), so a busy group costs at most one forced read per navigation; add `NonHuman == None` (one line) — and do add it to the I-3 fix, where it matters. |
| 47 | CAN WAIT | A read landing too early is followed by the next trigger. |
| 48 | **MUST FIX before merge** | Ruling; not implemented — I-1. |
| 49 | CAN WAIT | Unreachable (`ViewOf` never returns null). |
| 50 | CAN WAIT | `inFolder` now only names the move; cleanup. |
| 51 | WRONG now | `PersonalChatsFolderView.HandleRowMove` calls `chatList.RequestMove(chat)` (PersonalChatsFolderView.cs:397) — it must stay non-private. |
| 52 | CAN WAIT | Works; returning the accepted flag is a refactor. |
| 53 | CAN WAIT | Test hygiene. |
| 54 | CAN WAIT → device pass | Already in §6а.3; the builder diagram's «(parked at rest)» describes the runtime state, harmless. |
| 55 | CAN WAIT | A possible 0.22 s flash depending on handler order; paint the chip's stored mode before activating it if the device pass shows it. |
| 56 | CAN WAIT (do it in the wave) | The heal re-creates only a mode flag (no text or name), and the phone's `SemiAutoStore` keeps the choice anyway; but it defeats §4.6's wipe of that row and contradicts §6 («если данные ещё не удалены») — one-line `OpenChatAllowsAssistant()` guard. |
| 57 | CAN WAIT → device pass | Already in §6а.5. |
| 58 | CAN WAIT | « · » and the ZWSP literal are glyph-safe; style only. |
| 59 | CAN WAIT | No failure in 4001/4002; reset `UsageStore` / the schedule in SetUp when touching those fixtures. |
| 60 | CAN WAIT | Comment / harness duplication. |
| 61 | **MUST FIX before merge** | Ruling; not implemented — I-2. |
| 67 | CAN WAIT (cheap, do it in the wave) | Narrow (a tap on a ~38u sliver while the card is up) and recoverable (the card is still there after Back); stretching the card's scrim to the open chat's rect closes it. |
| 68 | CAN WAIT | Cached after the first find. |
| 69 | CAN WAIT | Declared; needs a lost answer AND an unseen number change; the next owed pairing overwrites it. |
| 70 | CAN WAIT | Doc nit; the method's own summary names the folder. |
| 71 | CAN WAIT | Worst case one extra, dismissable screen (a typed 10-digit vs a stored 11-digit number). |
| 72 | CAN WAIT | Off by one in rare twin cases in a bot-switcher count. |
| 73 | CAN WAIT | Unreachable. |
| 74 | CAN WAIT (one line) | Each fetch now returns business rows only, so the stale window is until the next «Сводка» fetch; route `PaywallController.cs:665` through `DashboardAudienceFilter.WithoutPersonal`. |
| 75 | CAN WAIT | Scroll reset on a rare event; unpinned branch. |
| 76 | CAN WAIT | `Tally` reads only parsed data; low throw risk. |
| 77 | CAN WAIT | Different parents (prefab vs scene); cosmetic. |
| 94–95 | CAN WAIT (do it in the wave) | CLAUDE.md:52 still says «…only on staging and in failed executions»; the Outcome stamp is terminal, so a failed run never carries it — drop the phrase. privacy.html:70 «только отметка» (dialog_counts also kept) is the owner's wording call, pending. |
| 100 | CAN WAIT (do it before the device pass) | Device pass §6а.9 searches «discarded crossed messages/get response», which misses `GetMessagesRoutine`'s «…crossed messages/get response … (attempt N)» (ChatManager.cs:1449) — the log that explains an empty history. Search «crossed messages/get response». |
| 101 | CAN WAIT | `chat.LID` rides the cached raw body; a list without it is not observed. |
| 102 | CAN WAIT | Doc wording of a test-only overload. |
| 103 | CAN WAIT | A test that cannot fail; drop or re-aim. |
| 104 | CAN WAIT | Unreachable (null ids never reach the gate). |
| 105 | CAN WAIT | An unexpected warning does not fail an EditMode test; add `LogAssert.Expect` for clarity. |
| 106 | CAN WAIT | A group's `chat.LID` is empty in practice; harmless. |

Left to the server reviewer: lines 1–4, 6–25 (incl. 24, the Handoffs webhook), 62–66, 79–92, 96–97; 78, 93, 98, 99 are status / ordering lines, not findings.

#### Minor (Nice to Have) — new, not in the deferred list

- **m-1.** Moves queued under a profile a reconnect replaced are refused (403: the refreshed token no longer lists it) and dropped
  with «Не удалось перенести чат» — `ChatAudienceSyncQueue` keys desires by profile and nothing re-keys them. Rare (a move made
  inside the reconnect window); re-key undelivered desires to the bot's new profile of the same channel, or deliver them before
  the Create.
- **m-2.** Channels paired before this version never send `connected`: the server's row (created later by Decide Audience) has no
  number, so the first «Сменить номер» after the upgrade is not a restart on the server (`CONNECTED_SQL` only fills the number) —
  the old number's computed decisions keep holding, although the app shows the onboarding by its own comparison. Until a first
  message arrives the app also has no connection moment for those profiles, so every undecided chat reads personal/unknown. Cheap
  fix: `PresetChannelsSeen` also queues `ReportConnected` for each real current profile (the server takes the number and keeps the
  line's moment). Only the owner's test bots today (no live bots on prod).
- **m-3.** `MakeBusinessCopy`'s «Авто» ignores a channel switched off in the settings (its clone inactive): the sheet promises
  answers nobody will send. Consider the bot's `isOn{Ch}`.
- **m-4.** CLAUDE.md `Chat/` «The working list» says «Every move goes through `ChatListView.RequestMove`, the folder's rows
  included» — true for swipes only; the sheets and the folder's «Сделать рабочими» / «Готово» call `ChatAudienceSyncService`
  directly. «The list is read at launch» holds through `OnApplicationFocus`, not through the `Awake` call (deferred 32).
- **m-5.** Device pass §6а should also cover: a move made offline survives a restart and is delivered later; after an F2
  reconnect the chats keep their kinds (I-4); a new client replying in an open chat the owner started gets chip + panel (I-3); a
  bot deleted and created anew owes «Кто из них клиенты?» again (spec §6); a sheet opened while the composer's keyboard is up (its
  buttons, 96–380u, and the 320u toast sit where the keyboard is).
