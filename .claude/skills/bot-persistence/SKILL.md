---
name: bot-persistence
description: Read and write bot entity data in this Unity app's PlayerPrefs store. Use whenever you touch bot creation, editing, deletion, activation, the products/services lists, or any per-bot field (name, business, prompt, profile/workflow IDs, numbers) — even a single one-field read or write. Bot data is namespaced by the bot GameObject's name with exact key suffixes and list conventions; get a key, default, or lifecycle step wrong and data silently vanishes or leaks between bots.
allowed-tools: Bash(find *) Read(*) Edit(*) Write(*) Glob(*) Grep(*)
---

# Bot Persistence — PlayerPrefs Entity Store

Every bot's data lives in `PlayerPrefs`, not a database or serialized asset. There's no schema enforcing anything, so a wrong key suffix or default writes to the void and reads back empty — silently. This skill carries the exact conventions so that doesn't happen. The canonical source is `Assets/Scripts/Main/Bot.cs` (especially `DeleteBot`, which enumerates the full per-bot key set) and the read/write sites in `Manager.cs` and `BotSettings*.cs`.

## The model: keys are namespaced by the bot GameObject's name

Every per-bot key is `<botName> + "<Suffix>"`, where `<botName>` is the bot **GameObject's name** (e.g. `Bot0`, `Bot1`):

- Inside `Bot.cs`: `transform.name`
- From Manager / BotSettings: `Manager.openBot.name`, `openBot.name`, `bot.name`

**Never hardcode `"Bot0"`.** Always derive the prefix from the live bot reference's `.name`. The number is an identity assigned at creation; hardcoding it corrupts a different bot.

```csharp
// Read
string botName = Manager.openBot.name;
string prompt  = PlayerPrefs.GetString(botName + "Prompt", "");

// Write
PlayerPrefs.SetString(botName + "Prompt", newPrompt);
PlayerPrefs.Save(); // flush after a batch of writes (see Defaults & Save)
```

For the full key catalog, list conventions, and global (non-bot) keys, read **`references/key-catalog.md`**.

## The three "is this bot on?" keys — do not confuse them

This is the single biggest footgun. There are three different activation-ish keys:

| Key | Type | Meaning | Default on read |
|-----|------|---------|-----------------|
| `<botName>` (bare name, **no suffix**) | int 0/1 | The master activation toggle (the switch on the bot card) | `1` (on) |
| `<botName>` + `"Active"` | int 0/1 | Connection-confirmed / live state | `0` |
| `<botName>` + `"isOnWhatsapp"` / `"isOnTelegram"` | int 0/1 | Per-channel enable | varies — see catalog |

The bare GameObject name being a live key means **you cannot store anything else under the unsuffixed name**, and reads of it default to `1`. See `Bot.SetSwitches()` and `Bot.EnableBot()`.

## Lists: products & services (the singular/plural trap)

Lists use a count key plus zero-based indexed items. Note the asymmetry that trips everyone up: the **count key is the plural noun + `Number`**, but the **item keys use the singular noun**:

| | Count key | Item keys (i = 0-based) |
|--|-----------|------------------------|
| Products | `<botName>` + `"ProductsNumber"` | `"Product"+i`, `"Product"+i+"Price"`, `"Product"+i+"Description"` |
| Services | `<botName>` + `"ServicesNumber"` | `"Service"+i`, `"Service"+i+"Price"`, `"Service"+i+"Description"` |

**Read** = read the count, then loop:
```csharp
int n = PlayerPrefs.GetInt(botName + "ProductsNumber", 0);
for (int i = 0; i < n; i++) {
    string name  = PlayerPrefs.GetString(botName + "Product" + i, "");
    string price = PlayerPrefs.GetString(botName + "Product" + i + "Price", "");
    string desc  = PlayerPrefs.GetString(botName + "Product" + i + "Description", "");
}
```

**Shrinking a list leaves orphan tail keys.** If a bot had 5 products and now has 3, writing items 0–2 and setting `ProductsNumber=3` leaves `Product3*`/`Product4*` behind. They won't be read (the count gates the loop), but they resurface if the count ever grows again. When rewriting a list to a smaller size, delete the old tail (loop the *old* count and `DeleteKey` indices ≥ new count), mirroring how `Bot.DeleteBot()` loops to `ProductsNumber` before removing the count key.

## Defaults, sentinels & Save

- **Strings** default to `""`. **Counts** default to `0`.
- **Profile & workflow IDs** use `"-1"` as the "not set / unauthed" sentinel — there's a named constant `Bot.UnauthedProfileSentinel = "-1"`. Treat `"-1"` as "no profile," never as a real id.
- **`PlayerPrefs.Save()`** — call it after a logical batch of writes (e.g. finishing an edit, before a risky/async operation). Unity auto-flushes on clean quit, but mobile apps get killed; an explicit Save protects the just-edited bot.

## Lifecycle trap: Awake reads before the Manager has written

`Bot.Awake()` runs the moment a bot prefab is instantiated — **before** `Manager` renames the GameObject to its final `BotN` and writes its PlayerPrefs. So any PlayerPrefs read in `Awake` (e.g. `BusinessType` in `ApplyBusinessIcon`) sees the *default* for a freshly created bot, not the real value. The established fix: tolerate the empty read in `Awake`, expose a public refresh method (`Bot.RefreshBusinessIcon()`), and have `Manager` call it explicitly once rename + write are done. Follow this pattern for any new Awake-time read of bot data.

## Identity linkage & deletion

A bot is two paired GameObjects: the card under `BotsParent` and its config screen under `Manager.BotSettingsParentStatic`, **matched by identical sibling index**. Anything that reorders or destroys one must keep the other aligned.

Deleting a bot is a full teardown, not just key removal — `Bot.DeleteBot()` is the reference. It:
1. Deletes every per-bot key (looping `ProductsNumber`/`ServicesNumber` to clear list items, then the count keys),
2. `ChatManager.Instance.PurgeCacheForBot(transform.name)` — drops its chat caches,
3. `Manager.Instance.DeleteProfilesAndWorkflows(...)` — removes the Wappi profiles + n8n workflows,
4. Destroys the paired `BotSettings` at the same sibling index, then the bot card.

Reuse `DeleteBot()` rather than re-implementing teardown — partial deletes orphan PlayerPrefs keys, profiles, and workflows.

## Self-check before you hand off

- [ ] Every key prefix comes from a live bot reference's `.name`, never a hardcoded `"Bot0"`
- [ ] Used the correct activation key (bare name vs `Active` vs `isOn{Whatsapp,Telegram}`)
- [ ] List items use the **singular** prefix; count uses the **plural + `Number`**; loop is 0-based
- [ ] A list that can shrink deletes its orphan tail keys
- [ ] String reads default `""`, counts `0`, profile/workflow IDs `"-1"` (`Bot.UnauthedProfileSentinel`)
- [ ] `PlayerPrefs.Save()` called after the write batch
- [ ] No new PlayerPrefs read in `Bot.Awake()` that assumes the Manager has already renamed/written — use the refresh-method pattern
- [ ] Deletion goes through `Bot.DeleteBot()` (keys + cache + profiles/workflows + paired BotSettings), not an ad-hoc key wipe
