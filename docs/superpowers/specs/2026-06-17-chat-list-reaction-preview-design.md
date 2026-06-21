# Chat-list reaction preview — WhatsApp style

**Date:** 2026-06-17
**Branch:** feat/whatsapp-send-reactions
**Status:** Approved (Phase 1 build now; Phase 2 architected-for, deferred)

## Problem

In the chat list, when a chat's last message is a reaction, the row shows only the
bare emoji (e.g. `❤️`). WhatsApp shows context: **who reacted** and **on which
message** — e.g. `You reacted ❤️ to "See you tomorrow"`.

That preview comes exclusively from Wappi's `chats/filter` bulk fetch:
`ChatDialog.last_message_data` (the emoji) + `last_message_type == "reaction"`.
`ChatPreviewFormatter` deliberately passes reaction text through untouched today
(`GetMediaInfo` returns `(null, null)` for `"reaction"`). The live send/receive
reaction paths on this branch do not touch the chat-list preview at all.

## Data availability (the constraint that shapes the design)

- **Who reacted** + **emoji**: always derivable. `IsLastMessageMine` → "You" vs
  "Reacted"; emoji is `last_message_data`. ✅
- **...to "the message text"**: needs the *text of the reacted-to message*. The
  chat-list payload (`ChatDialog`) carries **no** `stanzaId` and no target text —
  only the emoji and the reaction's own `last_message_id`. The target id
  (`stanzaId`) and target text only exist on a `RawMessage` from the *messages*
  endpoints. So the quoted text is reachable for free **only** on the live paths
  (`SendReaction` / `SyncLatestMessages`), where we already hold the target
  `MessageViewModel` (which has `.text`). For bulk-fetched reactions it is not
  reachable without an extra fetch (Phase 2).

## Scope

**Phase 1 (this build):** who + emoji everywhere, plus the quoted text where it is
free (live reactions). No new network calls.

**Phase 2 (deferred, architected-for):** backfill the quoted text for
bulk-fetched reactions via a lazy / serial / cached `messages/id/get` fetch, then
upgrade the row in place. Phase 1 leaves the exact data slot Phase 2 fills, so
Phase 2 is purely additive (no formatter/VM rework).

## Format spec

The reaction branch of `ChatPreviewFormatter` produces:

| Case | Preview |
|---|---|
| You reacted, target text known | `You reacted ❤️ to "See you tomorrow"` |
| You reacted, target unknown | `You reacted ❤️` |
| They reacted, target text known | `Reacted ❤️ to "See you tomorrow"` |
| They reacted, target unknown | `Reacted ❤️` |
| Target is media | `You reacted ❤️ to 📷 Photo` (type label, no quotes) |
| Reaction removed (empty emoji) | `Reaction removed` |

Rules:
- "Who": `IsLastMessageMine` → `You reacted` vs `Reacted`. 1:1 is this app's
  dominant case, so an unnamed `Reacted` reads correctly. Group reactor names are
  a Phase 2 refinement (available on the live path via `senderName`).
- No delivery tick on a reaction row (suppressed in the reaction branch).
- Quoted snippet capped at ~24 chars + ellipsis; smart-quotes `“ ”`.
- `Reaction removed` is shown whenever the reaction emoji is empty/whitespace and
  the last-message type is `reaction` (independent of who).

## Data plumbing (the Phase-2 seam)

- `ChatViewModel` gains two optional fields `ReactionTargetText` and
  `ReactionTargetType`, written together via one new method
  `UpdateReactionContext(text, type)`. `null` = "target unknown" → formatter omits
  the `to "…"` clause.
- `ChatPreviewFormatter.Format` gains two optional params:
  `Format(rawText, type, deliveryStatus, isMine, string reactionTargetText = null,
  string reactionTargetType = null)`, with an early `if (type == "reaction")`
  branch → `FormatReaction(...)`. All existing call sites compile unchanged.
- `ChatItemView.UpdatePreviewText` passes `vm.ReactionTargetText /
  vm.ReactionTargetType` through to `Format`.

Phase 2 later just calls `UpdateReactionContext(fetchedText, fetchedType)` for
bulk-fetched reaction rows — no other change.

## Where the fields get set

- **Bulk fetch** (`ParseChatsJson`, ChatManager.cs ~242/252): reaction rows render
  `You reacted/Reacted ❤️` with `ReactionTargetText = null`. Core fix; 100%
  reliable; this is what the user sees today as "just emoji."
- **Live send** (`SendReaction`, ChatManager.ReactionSend.cs): we hold the `target`
  VM → set the chat's reaction context to `target.text/type` so the row reads
  `You reacted ❤️ to "their message"`. Update preview/meta **in place, without
  reordering** the list.
- **Live receive** (`SyncLatestMessages` only — **not** historical chat-open in
  `GetMessagesRoutine`): when a genuinely new incoming reaction lands and
  `ev.time ≥ LastMessageTime`, set `Reacted ❤️ to "…"` from the resolved target
  VM. Historical reactions processed during chat open do not touch the list.

## Consistency guard

Any write that sets a reaction as the last message sets `ReactionTargetText`
*together* (to a value or explicitly `null`). This prevents a newer emoji-only
reaction (e.g. from a later bulk fetch) from inheriting an older reaction's quoted
text. The formatter consults the reaction fields only when `type == "reaction"`,
so the slot can never leak into non-reaction rows.

## Testing

EditMode tests (no asmdef — compile into `Assembly-CSharp-Editor`, per project
setup) on the pure `ChatPreviewFormatter` reaction path:
- the four who × text-known/unknown cases,
- media-target label,
- empty emoji → `Reaction removed`,
- snippet truncation at the cap.

Pure-function tests; no Unity runtime needed. Run via the test bridge / headless
runner.

## Out of scope (Phase 1)

- The `messages/id/get` fetch + cache subsystem (Phase 2).
- Group reactor names for incoming reactions on the bulk path.
- Reordering the chat list on a live reaction (we update preview in place only).
