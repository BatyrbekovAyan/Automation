# Phase 11: First-Run Onboarding Flow - Context

**Gathered:** 2026-07-17
**Status:** Ready for planning (v1.3 — pure client UI; independent of v1.2, can be scheduled before or after Phases 9–10)
**Source:** Approved design spec `docs/superpowers/specs/2026-07-17-first-run-onboarding-design.md` (2 review rounds; visual mockups `.planning/sketches/onboarding/onboarding-proposal.html`)

<domain>
## Phase Boundary

First-run onboarding woven into the existing bot-creation path: a one-time 3-slide welcome carousel (new `Screen_Onboarding`), an «Это безопасно» trust block inside BOTH existing auth flows, an enhanced auth-success moment that leads into price-list upload, and a derived-state «Первые шаги» checklist card on BotsPage. No changes to the AddBotPanel wizard logic, auth network flows, or any n8n workflow. No new network calls.

</domain>

<decisions>
## Implementation Decisions

### Locked by owner review (2026-07-17)
- **Telegram parity everywhere**: slide 3 shows both channels; trust block + success moment exist for WhatsApp AND Telegram; checklist channel label = bot's actual channel (`isOnWhatsapp`/`isOnTelegram`)
- **No «Пропустить»**: informative slides advance only with «Далее»/«Создать бота»
- **Price-list upload strictly AFTER authorization**: carousel does not pitch upload; success screen's primary CTA is «Загрузить прайс-лист» → BotSettings «Прайс-листы» tab (fallback «Открыть чаты» when `UploadedFilesStore` already has files — settings re-auth case)
- **No QR**: only the real code flows (WhatsApp pairing code; Telegram phone → code → optional 2FA)

### Locked technical decisions (from the approved spec)
- `PlayerPrefs "OnboardingSeen"` outside the bot key namespace; existing users (bots present) auto-flagged so they never see the carousel; `DeleteAll` wipe re-runs onboarding by design
- `PlayerPrefs "OnboardingChecklistDone"` latch; step states always derived live from facts, never stored per-step
- `Screen_Onboarding` built by a new idempotent `[MenuItem]` builder (`OnboardingScreenBuilder`, NavRestructureBuilder pattern); inserted after `Screen_New`, BEFORE the auth screens (auth stays LAST); `NavRestructureBuilder.ReorderScreens` must learn the new screen; scene mutation committed immediately after the builder run
- Carousel paging = existing `SnappyFlickScrollRect`; transitions 0.3s OutCubic; success check DOScale 0.9→1 OutBack; checklist cascade 0.05s stagger
- Gate: on first run with no bots and flag unset, show carousel INSTEAD of the AddBotPanel auto-open; carousel CTA sets the flag and calls the existing `AddBotPanel.Instance.Open()`
- All sizes in 1080×1920 reference units per `unity-ui-builder`; icons = Image + sprite (never TMP glyphs); RoundedCorners script on cards/CTAs
- Full RU copy deck locked in the spec (formal «вы»)

### Claude's Discretion
- Exact detection point for «Получить первый ответ бота» (first incoming chat containing a fromMe reply — cache-scan vs event hook)
- Pure-class extraction boundaries for EditMode tests; file/prefab naming
- Whether the carousel re-entry from «О приложении» ships in this phase (cheap) or is dropped
- Hero compositions' exact construction (mini-chat mock, mode cards, channel cards) within the mockups' visual intent

</decisions>

<specifics>
## Deferred/Out of Scope

- Localized KZ copy variants, analytics/funnel instrumentation, pre-auth demo-bot sandbox
- Requirements ONB-01..ONB-05 formalized in REQUIREMENTS.md at v1.3 milestone start (definitions in the spec)
</specifics>
