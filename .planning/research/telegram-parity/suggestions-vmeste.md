# suggestions-vmeste

## Summary
The «Вместе» suggestions path is: SemiAutoToggle/SuggestionsPanel → SuggestionsController → ISuggestionsProvider seam → N8nSuggestionsProvider → POST {n8nBaseUrl}/webhook/SuggestReplies → Suggest Replies workflow (9PTyYcelRQI7bGDb). The ONLY channel-bound client code is N8nSuggestionsProvider.cs:75-76, which hardcodes bot.whatsappProfileId and bot.whatsappWorkflowId into the frozen v1 payload — everything above the seam is channel-agnostic. The n8n workflow never reads n8n_chat_histories (chat history rides in the payload's messages[] from the client's live chat cache) and never calls Wappi; its only channel dependency is the Supabase RAG metadata filter hardwired to key `botWaId` (chunks carry BOTH botWaId and botTgId, so a Telegram filter key already has data to match). Reply-mode state is bot-scoped, not channel-scoped: per-bot default `{botName}ReplyMode` (ReplyModeToggleBinder) + per-chat tri-state override `{botId}_semiAuto_{chatId}` (SemiAutoStore). Telegram support needs (a) channel-aware profile/workflow-id selection in the provider payload and (b) a channel-conditional RAG filter key (or generic-key stamping) in the workflow, plus one cosmetic prompt string; the deeper prerequisite is that ChatManager's chat pipeline itself only loads WhatsApp chats today.

## Open questions
- Whether the «Вместе» per-bot default ({botName}ReplyMode) should stay shared across channels or become per-channel when Telegram chats join the same bot — product decision, no technical blocker found.
- Exact shape of Telegram tapi chat ids ('from' values) as they'd appear in CurrentChatId — could not verify against live data (external API calls off-limits); the claim that WhatsApp/Telegram chat-id namespaces are disjoint rests on Wappi conventions (numeric tapi ids vs ...@c.us), not on observed payloads.
- Whether the deployed dev/prod Suggest Replies workflow matches the canonical Tools/n8n/workflows JSON byte-for-byte (repo copy is the only source inspected).
- Server-side «Вместе» suppression (bot still auto-replies in semi mode) is pending per project memory — its design will interact with any channel field added to the wire contract but is outside this task's scope.

## Report
# «Вместе» (co-pilot) reply-suggestions path — WhatsApp coupling map

## Architecture chain (client)

```
SemiAutoToggle (open-chat top bar, view-only)        Assets/Scripts/UI/SemiAutoToggle.cs
ReplyModeToggleBinder (chats-list, per-bot default)  Assets/Scripts/UI/ReplyModeToggleBinder.cs
        │ OnToggled / GetMode
SuggestionsController (mediator)                     Assets/Scripts/Chat/SuggestionsController.cs
        │ ISuggestionsProvider seam                  Assets/Scripts/Chat/ISuggestionsProvider.cs:13
N8nSuggestionsProvider (live provider)               Assets/Scripts/Chat/N8nSuggestionsProvider.cs
        │ POST {Manager.n8nBaseUrl}/webhook/SuggestReplies
Suggest Replies workflow                             Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
        │ {suggestions:[{text,label}x4]}
SuggestionsPanel / SuggestionCard (render)           Assets/Scripts/UI/SuggestionsPanel.cs, SuggestionCard.cs
```

Note: `QuickReplyPanel.cs` / `QuickReplyButton.cs` (Assets/Scripts/Chat/) are a legacy/prototype 2x2 quick-reply grid — the only live reference is an unused serialized field `MessagesBottomPanel.quickReplyPanel` (Assets/Scripts/Chat/MessagesBottomPanel.cs:18); nothing calls `SetReplies` at runtime (only its own `showTestButtons` self-test, QuickReplyPanel.cs:38-50). The shipped «Вместе» UI is SuggestionsPanel/SuggestionCard.

## 1. N8nSuggestionsProvider.cs walkthrough (file: /Users/ayan/Projects/Automation/Assets/Scripts/Chat/N8nSuggestionsProvider.cs)

Plain C# class (not MonoBehaviour) implementing `ISuggestionsProvider`; swapped in on the single line SuggestionsController.cs:31 (`_provider = new N8nSuggestionsProvider();`).

- **Request (L32-48)**: resolves `ChatManager.Instance`; `Bot bot = Manager.Instance.FindBotByName(cm.CurrentBotId)` (L39). No bot or empty `cm.CurrentChatId` → `SuggestionStatus.Empty`, no network. Coroutine runs on ChatManager (always-active GameObject; the controller can be inactive when OnChatSelected fires).
- **Run coroutine (L52-98)**:
  - L57: `yield return ChatManager.Instance.WaitForChatFetchesDrain()` — public hook at Assets/Scripts/Main/ChatManager.Suggestions.cs:18 wrapping the private drain; provider only waits, never bumps `_chatFetchesInFlight` (Wappi response-crossing protection).
  - L64-70: re-resolves bot after the drain; `cm.TryGetRecentMessages(req.chatId, 12, out msgs)` — accessor at Assets/Scripts/Main/ChatManager.RecentMessages.cs:17-26; reads the private `_activeChatCache`, returns false if `chatId != currentChatId` (chat-switch guard → Empty, no paid LLM call).
  - **L72-81 — the WhatsApp hardcode**:
    - L75: `profileId: bot.whatsappProfileId`
    - L76: `botWaId: bot.whatsappWorkflowId` (comment: `""/"-1"` ⇒ server skips RAG; `"-1"` = `Bot.UnauthedProfileSentinel`, Assets/Scripts/Main/Bot.cs:67)
    - businessTypeId/businessName/ownerPrompt from PlayerPrefs `{botName}BusinessType` / `{botName}Name` / `{botName}Prompt` (L77-79); catalog from `BuildCatalog` (L104-124: "• {name} — {price}" lines from `{botName}Product{i}`/`{botName}Service{i}` keys).
  - L83-88: `POST {Manager.n8nBaseUrl}/webhook/SuggestReplies` (n8nBaseUrl at Assets/Scripts/Main/Manager.cs:165-175, default `https://bagkz.app.n8n.cloud`, dev override via PlayerPrefs `DevN8nBaseUrl`), UploadHandlerRaw + explicit `Content-Type: application/json` (L86), timeout 30.
  - L90-97: failure → `SuggestionStatus.Error`; success → `MapResponse(text, req.requestSeq)` (requestSeq stamped from the REQUEST, not server echo).
- **BuildPayloadJson (L134-160)** — frozen wire-contract v1 (DTO: Assets/Scripts/Chat/SuggestRepliesDtos.cs:27-41; field names ARE the wire keys, doc says "do NOT rename"). Exact payload shape:

```json
{
  "v": 1,
  "requestSeq": <long, monotonic>,
  "profileId": "<bot.whatsappProfileId>",
  "chatId": "<open chat id, e.g. 7707...@c.us>",
  "botWaId": "<bot.whatsappWorkflowId | \"\" | \"-1\">",
  "businessTypeId": "<kebab vertical id or legacy/empty>",
  "businessName": "<PlayerPrefs {bot}Name>",
  "ownerPrompt": "<clamped <=500>",
  "catalog": "<\"• name — price\" lines, clamped <=1500>",
  "steerTowardText": null | "<tapped card text>",
  "lastIncomingText": null | "<trigger message>",
  "messages": [ {"role":"client|business","text":"<clamped <=500>","ts":<unix sec>} ]  // last <=12, oldest->newest
}
```

  - `ToWireMessages` (L163-181): role = `isIncoming ? "client" : "business"`; `MediaText` (L187-203) collapses media to RU placeholders (`[фото]`, `[видео]`, `[голосовое сообщение]`, `[документ]`, `[стикер]`, `[сообщение]`) + caption.
- **MapResponse (L221-238)**: tolerant parse of `SuggestRepliesResponse` (SuggestRepliesDtos.cs:56-62); null/`error` non-empty/null suggestions/0 valid → Error; else first 4 valid `{text,label}` → `SuggestionItem{text,intentLabel}`, order preserved.

Controller triggers (SuggestionsController.cs): toggle-on (L114), chat-open restore (L98), incoming live message (L166-171, incoming-only), card tap re-cluster with `steerTowardText` (L160), manual refresh (L216-219). Card tap OVERWRITES the composer and never auto-sends (L153-162); actual delivery goes through the existing composer send → `https://wappi.pro/api/sync/message/send` (Assets/Scripts/Main/ChatManager.cs:1933) — itself WhatsApp-only.

## 2. Suggest Replies n8n workflow (file: /Users/ayan/Projects/Automation/Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json)

Node chain: Webhook `POST /SuggestReplies` (L4-19) → **Prep** code (jsCode L23) → If invalid? (L34-67) → If skipRag? (L68-101) → [true → Assemble; false → **Retrieve RAG** (L102-141) + Embeddings `text-embedding-3-small` (L142-161)] → **Assemble** (jsCode L164) → **LLM** gpt-4o-mini w/ strict JSON schema (jsonBody L183) → Validate (L205) → If ok? (L216-249) → [fail → LLM Retry (L258) → Validate 2 (L280)] → Build Response (L293) → Respond (L304-318).

- **profileId: parsed but NEVER used.** Prep echoes `profileId: String(b.profileId || '')` into its output item (L23) and nothing downstream references it — not the RAG filter, not the prompt, not the response. Pure pass-through.
- **chatId: validation only.** Prep sets `invalid = true` if `chatId` is not a non-empty string (L23). Never used as a lookup key. No format check — a Telegram chat id would pass.
- **botWaId: the two real uses.**
  1. skipRag gate: `const skipRag = (botWaId === '' || botWaId === '-1' || !queryText)` (Prep, L23).
  2. RAG scoping: `Retrieve RAG` = `@n8n/n8n-nodes-langchain.vectorStoreSupabase` in load mode against table `documents`, `queryName: match_documents`, topK 5, **metadata filter `{name: "botWaId", value: {{$json.botWaId}}}`** (L114-124). The query text is `lastIncomingText` or the last non-empty client message (Prep).
- **n8n_chat_histories: NOT used at all.** The workflow's conversation context is entirely the client-supplied `messages[]` (<=12 turns re-clamped server-side in Prep). Zero occurrences of `chat_histories`, `@c.us`, `api/sync`, or `tapi` in the JSON (grep-verified). It also never calls Wappi — the only external calls are Supabase (vector store) and OpenAI.
- **Session-key format (for reference — used by OTHER workflows, not this one):** `n8n_chat_histories.session_id` = `{profile_id}:{chat_id}`. Set identically in both bot templates' memory node: `sessionKey = $('Webhook').item.json.body.messages[0].profile_id + ':' + ...messages[0].from` (Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json:592 and 4VN3gsFaC2HUYmcc-Telegram_Bot.json:592 — same expression, so the format is already channel-uniform). Dashboard Outcomes parses it back with `split_part(session_id, ':', 1)` / `substr(..., position(':' in session_id)+1)` (2htWSV5IHO8E2CgB-Dashboard_Outcomes.json:38). (Incidental: THAT workflow has a WhatsApp-ism, `chat_id NOT LIKE '%@g.us'` group filter — irrelevant to suggestions but relevant to overall parity.)
- **WhatsApp id-format assumptions: none structural.** No `@c.us` handling, no `/api/sync` base. Two soft couplings only: (a) the payload/metadata field is *named* `botWaId` (semantically "workflow id used to stamp RAG chunks"); (b) cosmetic — the Assemble system prompt hardcodes «Владелец отправит выбранный вариант со своего WhatsApp» (L164).
- **RAG chunk metadata (from Upload File, Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json):** every chunk is stamped with BOTH `botWaId` (= whatsappWorkflowId form field) AND `botTgId` (= telegramWorkflowId), plus contentType/source/fileId. The bot reply workflows filter by their own `$workflow.id` against the channel-matching key: WhatsApp_Bot filters metadata `botWaId`, Telegram_Bot filters metadata `botTgId`. So the data for a Telegram-scoped suggestion RAG filter already exists — Suggest Replies just hardwires the WhatsApp key. (Project invariant per memory: keep a SINGLE-key match filter per query — don't OR two keys in one vector-store call.)

## 3. Reply-mode state («Авто»/«Вместе») — storage and scoping

Two layers, both client-side PlayerPrefs, both **bot-scoped (no channel dimension)**:

1. **Per-bot default** — chats-list header toggle, `ReplyModeToggleBinder` (Assets/Scripts/UI/ReplyModeToggleBinder.cs). Key `"{botName}ReplyMode"` (KeySuffix L45; write L153; read `GetMode` L63-64). Values: 0 = Авто (default), 1 = Semi/«Вместе». Confirmation popup on switch (L126-134); raises static `OnReplyModeChanged` (L43, L155). `botName` = the bot GameObject name (e.g. "Bot0") — one default per bot regardless of channel.
2. **Per-chat tri-state override** — `SemiAutoStore` (Assets/Scripts/Chat/SemiAutoStore.cs). Key `"{botId}_semiAuto_{chatId}"` (L29). Int tri-state: 0 = no override → inherit `ReplyModeToggleBinder.GetMode(botId) == Semi` (L26-27, L37); 1 = explicit OFF; 2 = explicit ON (L32-41). Toggling in an open chat always writes an explicit override (L40-41). `botId` = `ChatManager.CurrentBotId` (bot GameObject name, Assets/Scripts/Main/ChatManager.BotState.cs:14), `chatId` = `ChatManager.CurrentChatId` (the Wappi chat id — currently always a WhatsApp id like `...@c.us`). Persist/restore driven by SuggestionsController (Set at L109, IsOn at L93). Orphaned keys on bot delete are accepted by design (SemiAutoStore.cs:14).

Channel-scoping verdict: the key format has no channel component, but WhatsApp chat ids (`...@c.us`/`...@g.us`) and Telegram tapi chat ids live in disjoint string namespaces, so per-chat keys can't practically collide across channels for the same bot. The per-BOT default, however, is genuinely shared: if Telegram chats join the same bot, «Вместе»-by-default applies to both channels unless a channel dimension is added. Also note (adjacent, from project memory): server-side «Вместе» suppression is still pending — reply mode is purely client state; the bot's n8n reply workflow doesn't know about it on any channel.

## 4. What Telegram support requires

**Precondition (out of suggestions scope but load-bearing):** ChatManager's chat pipeline is WhatsApp-only — `GetActiveProfileId` reads only `bot.whatsappProfileId` (ChatManager.BotState.cs:142-147), `BeginLoadForActiveBot` gates on it (L200) and fires `BotHasNoWhatsApp` otherwise, all Wappi URLs use `https://wappi.pro/api/sync/` (e.g. ChatManager.cs:391, 525, 1933). Until Telegram chats flow through `_activeChatCache`, the suggestions path never fires for a Telegram chat at all (no chat open ⇒ Empty short-circuit at N8nSuggestionsProvider.cs:40). Once the pipeline is channel-aware, the suggestions stack itself is almost entirely channel-agnostic — SuggestionsController, SemiAutoStore, SemiAutoToggle, SuggestionsPanel, SuggestionRequest/Result all work off `CurrentBotId`/`CurrentChatId`/MessageViewModel with no channel references.

**Client payload changes (N8nSuggestionsProvider.cs):**
- L75-76 are the only channel-bound lines: select `bot.telegramProfileId`/`bot.telegramWorkflowId` vs `bot.whatsappProfileId`/`bot.whatsappWorkflowId` based on the open chat's channel (the provider needs a channel signal — most naturally from ChatManager alongside CurrentBotId/CurrentChatId).
- Wire contract: the v1 DTO's keys are frozen (SuggestRepliesDtos.cs:13 "do NOT rename"). Two options: (a) minimal — keep the `botWaId` key but carry the channel-appropriate workflow id, plus add a `channel` field so the server can pick the RAG filter key; (b) cleaner v2 — add `channel` + generic `botWorkflowId`, keep `botWaId` populated for backward compat during rollout. `profileId` is currently dead weight server-side, so passing `telegramProfileId` in it is harmless either way.
- The skipRag sentinel semantics (`""`/`"-1"`) hold unchanged for `telegramWorkflowId` (same sentinel convention, Bot.cs:67).

**Workflow changes (9PTyYcelRQI7bGDb-Suggest_Replies.json):**
- **Required:** RAG filter key selection. Today `Retrieve RAG` filters metadata `botWaId` only (L114-124). For a Telegram-chat request scoped by `telegramWorkflowId`, either branch to a second vector-store node filtering `botTgId` (channel If-branch; keeps the single-key-filter invariant), or keep one node and have the client send the WhatsApp workflow id even for Telegram chats — the latter works only for dual-channel bots and silently degrades to skipRag/no-grounding for Telegram-only bots (whatsappWorkflowId = ""/"-1"), so it is not parity.
- Prep: accept/propagate the `channel` field (and ideally validate it); adjust the `skipRag` computation to the channel-appropriate workflow id if the payload carries both.
- **Cosmetic:** Assemble system prompt line «Владелец отправит выбранный вариант со своего WhatsApp» (L164) → channel-neutral («со своего WhatsApp или Telegram») or parameterized by `channel`.
- **Nothing else server-side:** no n8n_chat_histories dependency, no Wappi calls, no id-format parsing, requestSeq/response envelope channel-neutral.

**Both sides:** yes — payload (channel-aware profile/workflow id + channel field) AND workflow (channel-conditional RAG metadata key + prompt string). Reply-mode state needs no storage change for correctness (chat-id namespaces are disjoint), but a product decision is needed on whether the per-bot «Вместе» default should become per-bot-per-channel; and the post-tap send path needs the Telegram send endpoint (tapi) as part of general chat-pipeline parity, not suggestions-specific work.
