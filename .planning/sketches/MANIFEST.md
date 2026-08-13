# Sketch Manifest

## Design Direction

Sketch 001 is a full single-file mockup (not a per-question variant exploration) — the
Claude Code candidate for the multi-platform UI redesign comparison described in
`docs/ui-redesign-prompt.md`. See its README for the identity direction and scope.

## Reference Points

`docs/ui-redesign-prompt.md` — the portable design brief this sketch was built from,
intended for pasting into multiple AI platforms (Claude, ChatGPT, Gemini, v0, etc.) for
comparison.

## Sketches

| # | Name | Design Question | Winner | Tags |
|---|------|----------------|--------|------|
| 001 | ai-assistant-redesign | Full-app visual identity candidate ("Артель") for the WhatsApp/Telegram AI sales-assistant redesign | N/A — single full-app candidate, judged externally against other platforms | full-app, identity, redesign |
| 002 | suggestions-panel-redesign | What form factor + skin for the reply-suggestions panel on the messages page (6 rounds: form factor → 4-rows → E+G synthesis → title placement → title on J → border-legend position) | **P** — J chassis (fixed sheet + inner scroll, bordered full-text cards) + border-legend title top-left; locked 2026-08-07 | suggestions, chat, panel, restyle |
| 003 | suggestions-keyboard-slot | If the suggestions panel moves INTO the keyboard slot (below the composer, mutually exclusive with the keyboard), how does it look and what switches it? Keeps 002's locked card design; explores chrome + switch affordance | **A «Прямая замена»** — locked P sheet in the keyboard slot, header kept, ✦⇄⌨ key inside the input field; locked 2026-08-12 | suggestions, messages-page, keyboard, layout |
| 004 | slot-key-placement | Where does the ✦⇄⌨ slot key live? Owner dislikes today's field-START spot («+» + key crowd the left edge). Baseline vs field-END / standalone button by send / panel-header exit + entry-only field key | — (pivoted → 005: keyboard is now focus-driven, so the key's panel⇄keyboard role dissolved; the placement findings feed 005's controls, all drawn at field-END) | suggestions, messages-page, composer, keyboard |
| 005 | slot-collapse | New model (given): keyboard only while the field is focused, panel is the slot's DEFAULT tenant — so what collapses EVERYTHING (composer flush to the bottom) and what brings the panel back? Thread-tap step-down / header chevron / chameleon field key / collapse-to-strip; round 2 added **E «тап + ручка»** (owner's corrections: thread tap only raises; draggable handle under the composer with 3 detents — collapsed / standard / full-content); rounds 3–4 refined E per owner: tap on the LOWERED composer raises the PANEL (no field focus), and ONE morphing key at the field's end showing the DESTINATION (panel up → ⌨ neutral; collapsed/keyboard → ✦ tinted; hidden in «Авто») | **E «тап + ручка + клавиша-хамелеон»** — thread/composer taps only raise (no field focus from collapsed), 3-detent handle (collapsed / standard 244 / full-content), one destination-glyph key at field end, all hidden in «Авто»; locked 2026-08-13 | suggestions, messages-page, composer, keyboard, collapse |
| 006 | bot-card-auto-pill | Авто-режим и тумблер активации бота — одно понятие: чем заменить iOS-свитч на карточке бота, чтобы он говорил языком капсулы «Авто» из шапки чатов? Baseline «Сегодня» vs A пилюля-1:1 в футере / B футер-сегмент «Авто \| Пауза» (язык лунки каналов) / C компактная карточка без футера; round 2 = **C2** по правкам владельца: точка на аватаре убрана, «Подключение…» моргает само, под именем тип бизнеса + brand-значки каналов (цветной = подключён+включён / серый = подключён, но канал выключен тумблером в настройках / нет значка = не подключён) | **C2** — locked 2026-08-13 | bots-page, bot-card, activation, auto-pill, restyle |
| 007 | product-service-card | Как должна выглядеть карточка товара/услуги на вкладках «Продукты»/«Услуги» настроек бота? Baseline «Сегодня» (квадрат-заглушка + мелкая цена) vs A «Реестр» (группа с hairline, без муляжа) / B «Ценник» (монограмма + пилюля цены) / C «Витрина» (сетка 2 колонки); round 2 (владелец выбрал B): вкладка целиком с прайс-листами наверху как сегодня — B2 секция-группа (анатомия строки 1:1: бейдж-расширение, имя, размер · дата, ✕, «Загрузить прайс-лист» строкой в группе, живая загрузка/удаление) vs B3 полка чипов; round 3 — владельцу понравился и нижний лист мокапа, поэтому реальный `ItemEditSheet` приведён к нему в Unity (аддитивный `ItemEditSheetRestyleBuilder`, новый `FieldWellFocusBorder`, клэмп подъёма над клавиатурой; 1812/1812 зелёных, префаб +14 объектов, 0 удалённых, повторный прогон побайтово идентичен) | **B2** — карточки-«ценники» + секция прайс-листов наверху; lock 2026-08-14, лист редактирования реализован (device-пасс за владельцем) | bot-settings, products, services, card, price-lists, item-edit-sheet, restyle |
