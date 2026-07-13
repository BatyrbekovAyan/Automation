# Phase 7: «Вместе» Suggestions + Dashboard on Telegram - Context

**Gathered:** 2026-07-13
**Status:** Ready for planning
**Source:** Design-spec express path (docs/superpowers/specs/2026-07-12-telegram-parity-design.md §D2, §D3; .planning/research/telegram-parity/suggestions-vmeste.md + dashboard-scope.md; autonomous session)

<domain>
## Phase Boundary

The two remaining Telegram-aware client surfaces: (a) «Вместе» suggestions populate and stay RAG-grounded in Telegram chats (client payload; the server's channel-branched RAG shipped in Phase 4), and (b) the «Сводка» dashboard counts, filters (bot-level chips), and deep-links Telegram conversations. All C#, fully autonomous, headless-testable.

Live-webhook verification of suggestions on a real Telegram chat belongs to the TPL-06/e2e owner session (dev n8n deploy is owner-gated); this phase verifies at the payload/unit level.

NOT here: any n8n edits (done in Phase 4), reactions-receive/media (05-06, capture-gated), prod.

</domain>

<decisions>
## Implementation Decisions

### Suggestions client (SUGG-01, SUGG-02) — locked, design D3
- `N8nSuggestionsProvider` selects ids by the OPEN CHAT's channel: read `ChatManager.Instance.ActiveChannel` at request-build time (the provider already re-resolves bot/chat per request; the chat currently open IS the active channel's — the seam guarantees it).
- Payload (additive v1.1; DTO keys frozen, ADD-only, never rename):
  - `profileId` = channel-appropriate profile id (WA: whatsappProfileId; TG: telegramProfileId) — server treats it as pass-through today.
  - `botWaId` = whatsappWorkflowId ALWAYS (backward compat; sentinel semantics unchanged).
  - NEW `botTgId` = telegramWorkflowId (same ""/"-1" sentinel conventions).
  - NEW `channel` = "whatsapp" | "telegram" (lowercase; server Prep defaults absent→whatsapp — shipped in Phase 4).
- `SuggestRepliesDtos` request DTO gains the two fields with the same field-name-is-wire-key convention + doc comment.
- `BuildPayloadJson` stays pure/static → extend its tests: channel selection matrix (WA chat / TG chat / TG-only bot), sentinel passthrough, backward-compat (botWaId always present).
- The provider's Empty short-circuits, drain-gate usage, requestSeq semantics: UNCHANGED.
- SemiAutoStore / ReplyModeToggleBinder: NO changes (bot-scoped default is a locked decision).

### Dashboard (DASH-01..03) — locked, design D2 + dashboard-scope research
- `AuthedProfiles()` + `ProfileToBot()` also collect/map `telegramProfileId` (same `Bot.UnauthedProfileSentinel` guard). Server Prep already strips "-1" defensively.
- **Bot-level chips (DASH-02):** `_botFilter` becomes bot-scoped — chips are built per BOT (one chip per bot name), and filtering matches a SET of that bot's profile ids. `DashboardMetrics.FilterByProfile` generalizes to `FilterByProfiles(rows, ISet<string>)` (keep the old single-id overload delegating to it if tests reference it). Chip label stays the bot name; a dual-channel bot gets ONE chip covering both profiles.
- **Channel-aware deep-link (DASH-03):** the outcome row resolves its channel from WHICH map matched the profileId (build two maps or one map profileId→(botName, channel)). `OpenChat` flow: `SetActiveBot(botName)` → `SetActiveChannel(channel)` (new step; ChatManager clamps/no-ops as needed; keep the existing soft-fail comment semantics) → tab switch via `BottomTabManager.WhatsAppTabIndex` (now the «Чаты» tab, constant unchanged) → deferred `SelectChat(chatId)`. Order matters: bot first (channel restore fires), then explicit channel, then select.
- Row cosmetics: `ChatDisplayName` already passes non-@c.us ids through (`ChatIdFormat.Recipient` is suffix-conditional); Telegram rows fall back to raw id + silhouette when the chat isn't in the active lookup — ACCEPTED v1 degradation (design D2). No channel badge this phase (chips are bot-level; row shows bot tag as today).
- `DashboardModels` wire shapes: UNCHANGED (server contract untouched).

### Testing (locked)
- EditMode: payload-builder channel matrix; profile-collection/map (pure seams — extract if currently inline in DashboardPage; follow the existing pure-seam precedents); FilterByProfiles set semantics incl. dual-channel bot; deep-link channel resolution (pure part). Full suite green (baseline 901).
- No scene changes; no builder runs.

### Claude's Discretion
- Whether profileId→(bot,channel) is one dictionary or two; internal naming; where extracted pure seams live (Dashboard/ folder conventions).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

- `docs/superpowers/specs/2026-07-12-telegram-parity-design.md` — §D2 (dashboard scope), §D3 (suggestions contract)
- `.planning/research/telegram-parity/suggestions-vmeste.md` — provider walkthrough §1, payload shape, channel-coupling map §4
- `.planning/research/telegram-parity/dashboard-scope.md` — exact file:line for AuthedProfiles/ProfileToBot/BuildChips/FilterByProfile/OpenChat + the chip/deep-link cost analysis
- `Assets/Scripts/Chat/N8nSuggestionsProvider.cs`, `Assets/Scripts/Chat/SuggestRepliesDtos.cs` — the frozen v1 contract (field-name-is-wire-key)
- `Assets/Scripts/Main/Dashboard/*.cs`, `Assets/Scripts/Main/ChatManager.Dashboard.cs`, `ChatManager.Channel.cs` (SetActiveChannel/ActiveChannel)
- `.claude/skills/chat-data-flow/SKILL.md`, `.claude/skills/bot-persistence/SKILL.md`, `.claude/skills/unity-api-integration/SKILL.md`
- Phase-4 server counterpart (already shipped): `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` — Prep accepts channel/botTgId; If routes botWaId|botTgId retrieve nodes

</canonical_refs>

<specifics>
## Specific Ideas

- The suggestions live e2e on Telegram requires the owner's dev n8n deploy (04-HUMAN-UAT) — note in the phase UAT that SUGG live verification rides that session.
- 891→900→901 test-count history: baseline for this phase is 901.
- Do not re-introduce a direct `bot.whatsappProfileId` read anywhere new — always channel-resolved.
</specifics>

<deferred>
## Deferred Ideas

- Row channel badge / per-channel avatar+title resolution for inactive-bot chats → polish backlog.
- Suggestions provider reading history for the NON-active channel → not a real case (suggestions only fire for the open chat).
</deferred>

---

*Phase: 07-vmeste-suggestions-dashboard-on-telegram*
*Context gathered: 2026-07-13 via design-spec express path*
