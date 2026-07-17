# First-Run Onboarding Flow — Design Spec

**Date:** 2026-07-17
**Status:** APPROVED (2 design-review rounds with owner; rev2 incorporates all owner feedback)
**Visual reference:** `.planning/sketches/onboarding/onboarding-proposal.html` (7 phone mockups, approach comparison, copy deck; also published as a private artifact)
**Registered as:** Phase 11 (v1.3) — `.planning/phases/11-first-run-onboarding-flow/`

## Problem

A brand-new user opens the app onto the Bots empty state and an auto-opened AddBotPanel. Nothing explains what the product does before the form appears, nothing addresses the #1 owner fear at the scariest step (linking their personal WhatsApp/Telegram), and after a successful auth the user is dropped into an empty app with no path to a *working* bot (price list uploaded, first live reply). Install → working bot conversion leaks at three places: pre-form comprehension, auth trust, post-auth activation.

## Goals

1. Value comprehension in under a minute, before the creation form.
2. Trust at the auth step — channel-specific reassurance, on both WhatsApp and Telegram.
3. Carry the user past auth to the two actions that make the bot actually valuable: price-list upload, first live bot reply.

## Non-goals

- No account/registration system (the app deliberately has none).
- No coach marks / tooltip tours over existing UI.
- No changes to the AddBotPanel wizard itself, the auth network flows, or any n8n workflow.
- No video/Lottie assets (static composition + DOTween only).

## Locked design constraints (owner feedback, 2026-07-17)

1. **Telegram is a first-class channel everywhere** — channels slide shows both; trust block and success moment exist for both auth flows; checklist channel label reflects the bot's actual channel.
2. **No «Пропустить» on informative slides** — the 3 slides advance only with «Далее» / «Создать бота».
3. **Price-list upload comes AFTER authorization** — the carousel does not pitch uploading; the success screen leads into upload.
4. **No QR anywhere** — the app's auth pages are code-based (WhatsApp pairing code; Telegram phone → code → optional 2FA cloud password). The design references only those flows.

## Flow (5 beats)

```
First launch (no bots, OnboardingSeen unset)
  → [NEW] Welcome carousel — 3 slides, «Далее»/«Создать бота» only
  → [EXISTING] AddBotPanel wizard (name · business · channel)
  → [ENHANCED] Auth screen (per channel) + «Это безопасно» trust block
  → [ENHANCED] Auth success panel → «Загрузить прайс-лист» / «Позже»
  → [NEW] «Первые шаги» checklist card on the Bots page until 4/4 done
```

## Screen specs

All sizes in canvas reference units (1080×1920, dp × 3) per `unity-ui-builder`; exact values calibrated at builder time against the measured type scale (H1 50–55, body 40–42, caption 28–32; CTA height ≈150; page margins 96).

### Slides 1–3 (new `Screen_Onboarding`)

Shared layout: hero composition zone (vertically centered), title (H1 50, Ink #1A1A2E, center), body (38–40, Muted #65676B, center, ≤ 620 wide), page dots (active dot = Primary #1B7CEB elongated pill), full-width primary CTA in the thumb zone. Paging via the existing `SnappyFlickScrollRect`; slide transitions 0.3s OutCubic. No skip affordance.

1. **Ценность** — hero: a mini chat mock (incoming customer question about a part, bot answer with a live price, typing indicator). Title «Бот отвечает клиентам за вас». CTA «Далее».
2. **Контроль** — hero: the two mode cards Авто (selected) / Вместе with one-line descriptions. Title «Вы решаете, сколько доверить». CTA «Далее». Positioned deliberately *before* the app asks for channel access.
3. **Каналы** — hero: WhatsApp + Telegram cards, both check-marked, one-line descriptions («Клиенты пишут на ваш номер» / «Ваш личный аккаунт Telegram»). Title «Работает там, где ваши клиенты». CTA **«Создать бота»** → closes onboarding, opens the existing AddBotPanel.

### Auth trust blocks (enhancement to existing shared auth screens)

A compact card inserted into the WhatsApp code panel AND the Telegram phone/code/2FA panels (screens are shared by wizard and settings re-auth — the block appears in both contexts, which is desirable). Green-tinted card (bg ≈ #F2F8F2, border ≈ #DCEDDD), lock icon (Image + sprite, NOT a TMP glyph), title «Это безопасно» + channel-specific body (see copy deck). No QR references.

### Success moment (enhancement to existing `WhatsappAuthSuccessPanel` / `TelegramAuthSuccessPanel`)

Green check circle animated DOScale 0.9→1 OutBack + fade; title «Бот подключён!»; body pivots to the price list; primary CTA «Загрузить прайс-лист» deep-links to BotSettings → «Прайс-листы» tab for the just-authed bot; ghost «Позже» dismisses to the normal post-auth destination. If `UploadedFilesStore` already has files for this bot (settings re-auth case), the primary CTA becomes «Открыть чаты» instead.

### «Первые шаги» checklist (new prefab card on `BotsPage`, above the bots list)

Card: title «Первые шаги» + «N из 4» + slim progress bar; four rows with check/empty circles and chevrons; completed rows struck through. Appears with a 0.05s-stagger cascade. Rows and their derived-state facts:

| Row | Fact | Deep link |
|---|---|---|
| Создать бота | any bot exists | AddBotPanel |
| Подключить WhatsApp/Telegram (label = the bot's actual channel via `isOnWhatsapp`/`isOnTelegram`) | that channel authed (profile id not `""`/`"-1"`) | auth screen |
| Загрузить прайс-лист | `UploadedFilesStore` non-empty for the bot | BotSettings → «Прайс-листы» |
| Получить первый ответ бота (tapping shows hint: «Попросите знакомого написать вам — и посмотрите, как бот ответит») | first incoming chat whose history contains a bot (fromMe) reply — exact detection point is planner discretion | Chats screen |

Card hides permanently once all four facts are true (a completion flag so it never resurrects if data later changes).

## Copy deck (RU, formal «вы»)

| Element | Text |
|---|---|
| Слайд 1 заголовок / текст | Бот отвечает клиентам за вас / Круглосуточно, в WhatsApp и Telegram — на вашем обычном номере |
| Слайд 2 заголовок / текст | Вы решаете, сколько доверить / Полный автопилот или подтверждение каждого ответа — можно менять в любой момент |
| Слайд 3 заголовок / текст / CTA | Работает там, где ваши клиенты / Подключите WhatsApp, Telegram или оба сразу — канал выберете при создании бота / Создать бота |
| Доверие (WhatsApp) | Это безопасно — Работает через официальные «Связанные устройства» WhatsApp. Переписка остаётся у вас, отключить бота можно в любой момент. |
| Доверие (Telegram) | Это безопасно — Официальный вход Telegram: код приходит в само приложение. Переписка остаётся у вас, отключить бота можно в любой момент. |
| Успех заголовок / текст / кнопки | Бот подключён! / Осталось научить бота вашим ценам — загрузите прайс-лист, и он будет отвечать по вашим товарам / Загрузить прайс-лист · Позже |
| Чек-лист заголовок / пункты | Первые шаги / Создать бота · Подключить WhatsApp/Telegram · Загрузить прайс-лист · Получить первый ответ бота |

## State & persistence

- `PlayerPrefs "OnboardingSeen"` (int 1) — set when the carousel is dismissed via «Создать бота» (or when a bot already exists on first run after update, so existing users never see it). Lives OUTSIDE the per-bot key namespace; `PlayerPrefs.DeleteAll()` in «Удалить все данные» wipes it automatically (intended: full wipe re-runs onboarding).
- `PlayerPrefs "OnboardingChecklistDone"` (int 1) — set when all 4 facts first become true; gates the card permanently.
- Checklist step states are always derived live from facts (never stored per-step).

## Architecture

- **`Screen_Onboarding`** built by a new `[MenuItem]` editor builder (`OnboardingScreenBuilder`, follows `NavRestructureBuilder` pattern: idempotent delete-and-rebuild, explicit font/anchor stamping, Image+sprite icons, RoundedCorners script). Scene mutation committed immediately after the builder run (parallel-scene-clobber rule).
- **Screen order**: `Screen_Onboarding` inserted after `Screen_New`, BEFORE `WhatsappAuth`/`TelegramAuth` (auth pages must stay LAST). `NavRestructureBuilder.ReorderScreens` must be taught the new screen — builders must rewire consumers.
- **Gate logic**: on `Manager.Start` (after the orphan-profile sweep), if no bots exist AND `OnboardingSeen` unset → show `Screen_Onboarding` instead of the AddBotPanel auto-open; carousel CTA sets the flag and calls the existing `AddBotPanel.Instance.Open()` path. `BotsPage` auto-open behavior is otherwise unchanged.
- **Trust blocks + success**: added inside the existing auth panels (Manager-owned WhatsApp/Telegram auth screens); success CTA uses the existing `Manager.openBotSettings` path targeting the Files tab.
- **Checklist**: `FirstStepsCard` MonoBehaviour on a new prefab; refresh on `BotsPage.OnEnable` + relevant events; rows call existing navigation entry points.
- No new network calls; no n8n changes; no secrets.

## Requirements (to formalize at milestone start)

- **ONB-01**: One-time 3-slide carousel, no skip, CTA into the existing wizard; never re-shown (`OnboardingSeen`); existing users (bots present) never see it.
- **ONB-02**: «Это безопасно» trust block on both channels' auth panels, channel-specific copy, code flows only (no QR).
- **ONB-03**: Post-auth success moment on both channels with «Загрузить прайс-лист» deep link (fallback «Открыть чаты» when files already exist).
- **ONB-04**: Derived-state «Первые шаги» card on BotsPage: 4 steps, channel-aware label, per-row deep links, permanent hide on completion.
- **ONB-05**: Zero regression — empty state, AddBotPanel auto-open (post-onboarding), auth flows, and the full EditMode suite unchanged/green.

## Testing

- EditMode: gate logic (first-run vs `OnboardingSeen` vs bots-exist), checklist fact derivation (per-channel label, sentinel profile ids, `UploadedFilesStore`, completion latch), success-CTA target selection (files-exist fallback). Pure-logic classes extracted for testability (no MonoBehaviour-only logic where a pure class works).
- Device visual pass (Game view 1080×2400 first): carousel paging feel, trust block on both auth panels, success animation, checklist cascade.

## Out of scope / future

- Re-entry point to the carousel from Профиль → «О приложении» (cheap; decide at planning).
- Localized KZ copy variants; analytics/funnel instrumentation; interactive demo-bot sandbox before auth.
