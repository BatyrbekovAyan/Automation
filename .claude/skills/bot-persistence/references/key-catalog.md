# Bot Persistence — Full Key Catalog

Read this when you need the exhaustive list of PlayerPrefs keys, their types/defaults, or the global (non-bot) keys. The source of truth is `Bot.cs` (`DeleteBot` lists every per-bot key) plus the read/write sites in `Manager.cs` and `BotSettings*.cs`.

## Table of contents
1. Per-bot scalar keys
2. The activation keys (expanded)
3. List keys (products & services)
4. Global / app-level keys
5. Worked examples (full read, full write, safe shrink)

---

## 1. Per-bot scalar keys

All prefixed with `<botName>` (the bot GameObject's `.name`, e.g. `Bot0`). Defaults are what to pass as the second arg to `GetString`/`GetInt`.

| Suffix | Type | Default | Meaning |
|--------|------|---------|---------|
| `Name` | string | `""` | Display name |
| `Business` | string | `""` | Business description (free text) |
| `BusinessType` | string | `""` | BusinessTypesSO **id** (`bt.id`), drives the icon/tint via `BusinessTypesSO.TryGetById` |
| `Prompt` | string | `""` | System prompt text |
| `WhatsappNumber` | string | `""` | WhatsApp phone |
| `TelegramNumber` | string | `""` | Telegram phone |
| `WhatsappProfileId` | string | `"-1"` | Wappi WhatsApp profile id (`"-1"` = unauthed) |
| `TelegramProfileId` | string | `"-1"` | Wappi Telegram profile id (`"-1"` = unauthed) |
| `WhatsappWorkflowId` | string | `"-1"` | n8n WhatsApp workflow id (`"-1"` = none) |
| `TelegramWorkflowId` | string | `"-1"` | n8n Telegram workflow id (`"-1"` = none) |
| `isOnWhatsapp` | int 0/1 | varies (see §2) | WhatsApp channel enabled |
| `isOnTelegram` | int 0/1 | varies (see §2) | Telegram channel enabled |
| `Active` | int 0/1 | `0` | Connection-confirmed state |
| *(bare name, no suffix)* | int 0/1 | `1` | Master activation toggle |
| `ProductsNumber` | int | `0` | Product list count (see §3) |
| `ServicesNumber` | int | `0` | Service list count (see §3) |
| `WhatsappRetiredProfileId` / `TelegramRetiredProfileId` | string | `""` | M4/F2: the profile id the server retired on that channel (410 `profile_retired` on claim.start). Written ONLY through `ProfileRetirement` (`Mark`, `AfterCreate`). F2 (T8) MOVES the channel aside: `…WorkflowId` → `…RetiredWorkflowId`, the current `…ProfileId`/`…WorkflowId` → `"-1"`, `isOn…` → 0 (Bot fields + PlayerPrefs in one `Store`, one Save) — every reader then sees a disconnected channel. Exception `app_delete`: ids stay, the marker applies to the current id and yields to the token (M4). Sent as Create's `PreviousProfileId` from settings |
| `WhatsappRetiredReason` / `TelegramRetiredReason` | string | `""` | M4: the server's `deleted_reason` (`trial_expiry`/`churn_grace`/`liveness`/`app_delete`/`channel_limit`; `""` = not told) — the card's subline via `ProfileRetirementCopy` |
| `WhatsappRetiredWorkflowId` / `TelegramRetiredWorkflowId` | string | `""` (`"-1"` = the channel had no clone) | F2 (T8): the clone the channel had when its profile was retired. Read by the deletion (`ProfileRetirement.ServerIdsOf` → V12 deletes it and its chunks/memory with the retired profile — without it a bot with only retired ids would read as `NothingOnServer`). The whole retired set is cleared ONLY when a Create's `adoptedProfiles` names the retired profile (`ProfileRetirement.AfterCreate`) or with the bot |
| `WhatsappUnauthorized` / `TelegramUnauthorized` | int 1 / absent | absent (= not marked) | A4 «защита платящих» (2026-10-14): the channel's last Wappi status (the proxy's `status` op, `Manager.ChannelHealthRoutine` at launch and on a return to the app, ≤ 1/min) said `authorized:false` — the card shows «Канал отключён · переподключите в настройках» and a gray icon. NON-destructive: nothing is deleted or switched off. Written ONLY through `ChannelHealth.Apply` (1 on a positive false; the key DELETED on `authorized:true`), removed on a re-authorisation (`ChannelHealth.UnmarkProfile` from Manager's auth pollers) and in `Bot.DeleteBotLocally` (`ChannelHealth.Clear`). Inert while the channel has no profile or is retired (`ChannelHealth.CardShowsUnauthorized`). Suffix via `ChannelHealth.Suffix(channel)` |
| `WhatsappCreatePending` / `TelegramCreatePending` | int 0/1 | `0` | F1 (T8): a Create from the settings failed (any refusal but `channel_limit`, or no answer) for a paired profile the bot KEEPS (the pending-profile ledger is settled so the quit-sweep does not delete it). Repeated on the next settings open / channel switch-on (`CreateWorkflowResponse.RetryOnSettingsOpen`/`RetryOnChannelOn`; Create is idempotent on the server). Written by `Manager.MarkCreatePending`, cleared on success / `channel_limit`, deleted in `Bot.DeleteBotLocally`. Suffix via `CreateWorkflowResponse.PendingSuffix(channel)` |

Note: the profile/workflow ids are also mirrored as public fields on the `Bot` component (`whatsappProfileId`, `telegramProfileId`, `whatsappWorkflowId`, `telegramWorkflowId`) — the PlayerPrefs copy is the persisted form. The six retired-set keys (M4 + F2) are deleted by `ProfileRetirement.Clear(transform.name)` inside `Bot.DeleteBotLocally`, which also deletes both `…CreatePending` keys and (A4) both `…Unauthorized` marks (`ChannelHealth.Clear`). Legacy keys `isOn` and `Status` are also deleted in `DeleteBot` but are not actively written; don't add new dependencies on them.

## 2. The activation keys (expanded)

Three independent keys, easy to confuse:

- **Bare `<botName>`** (no suffix), int, default `1`. The master on/off switch shown on the bot card. Written by `Bot.EnableBot()` as `PlayerPrefs.SetInt(transform.name, enabled ? 1 : 0)`; read by `Bot.SetSwitches()` as `PlayerPrefs.GetInt(transform.name, 1)`. Because this key IS the bare name, you must never store any other value under the unsuffixed name.
- **`<botName>Active`**, int, default `0`. Whether the bot is confirmed live/connected (drives "Active" vs "Connecting.." vs "Not Active" status text + color).
- **`<botName>isOnWhatsapp` / `<botName>isOnTelegram`**, int 0/1. Per-channel enable, set from the BotSettings toggles. Read defaults are inconsistent across the codebase (some sites default `1`, some `0`) — pass the default that matches the call site's intent explicitly rather than assuming.

## 3. List keys (products & services)

Two parallel lists with identical structure. **Count key = plural noun + `Number`; item keys = singular noun + index.**

Products:
- Count: `<botName>ProductsNumber` (int, default 0)
- Item i (0-based): `<botName>Product{i}` (name), `<botName>Product{i}Price`, `<botName>Product{i}Description`

Services:
- Count: `<botName>ServicesNumber` (int, default 0)
- Item i (0-based): `<botName>Service{i}` (name), `<botName>Service{i}Price`, `<botName>Service{i}Description`

All item values are strings, default `""`.

## 4. Global / app-level keys

Not namespaced by bot — app-wide:

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `ids` | int | `0` | Bot id counter — next bot number / count |
| `Locale` | int | `0` | Selected app language/locale index |
| `WhatsappCooldownFinishTime` | string | `"-1"` | Auth-code resend cooldown end (unix-ish), `"-1"` = no cooldown |
| `TelegramCooldownFinishTime` | string | `"-1"` | Telegram auth-code resend cooldown end |
| `lastCreatedWhatsappProfileId` | string | `"-1"` | Profile id from the most recent WhatsApp creation (recovery) |
| `lastCreatedTelegramProfileId` | string | `"-1"` | Same for Telegram |
| `lastCreatedWhatsappProfileIdSaved` | int | `0`/`1` | Flag: whether the above was persisted to a bot |
| `lastCreatedTelegramProfileIdSaved` | int | `0`/`1` | Same for Telegram |
| `KeyName`, `KeyEmail` | string | const defaults | Profile page name/email (constants `KeyName`/`KeyEmail`) |

## 5. Worked examples

**Full read of a bot into a model:**
```csharp
string b = bot.name;
var model = new BotData {
    Name         = PlayerPrefs.GetString(b + "Name", ""),
    Business     = PlayerPrefs.GetString(b + "Business", ""),
    BusinessType = PlayerPrefs.GetString(b + "BusinessType", ""),
    Prompt       = PlayerPrefs.GetString(b + "Prompt", ""),
    IsOnWhatsapp = PlayerPrefs.GetInt(b + "isOnWhatsapp", 0) == 1,
    WhatsappProfileId = PlayerPrefs.GetString(b + "WhatsappProfileId", Bot.UnauthedProfileSentinel),
};
int productCount = PlayerPrefs.GetInt(b + "ProductsNumber", 0);
// ...loop product items...
```

**Safe list write (handles shrink):**
```csharp
string b = bot.name;
int oldCount = PlayerPrefs.GetInt(b + "ProductsNumber", 0);

for (int i = 0; i < products.Count; i++) {
    PlayerPrefs.SetString(b + "Product" + i, products[i].Name);
    PlayerPrefs.SetString(b + "Product" + i + "Price", products[i].Price);
    PlayerPrefs.SetString(b + "Product" + i + "Description", products[i].Description);
}
// Clear orphan tail from a previous, longer list
for (int i = products.Count; i < oldCount; i++) {
    PlayerPrefs.DeleteKey(b + "Product" + i);
    PlayerPrefs.DeleteKey(b + "Product" + i + "Price");
    PlayerPrefs.DeleteKey(b + "Product" + i + "Description");
}
PlayerPrefs.SetInt(b + "ProductsNumber", products.Count);
PlayerPrefs.Save();
```
