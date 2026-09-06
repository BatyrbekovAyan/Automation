# App Review Notes (вставить в App Store Connect → App Review Information → Notes)

Ниже готовый EN-текст. Ссылка на демо-видео уже вписана (непубличный путь на
choosereply.com, файл `~/choosereply/site/review/…` на VPS; исходник и монтаж —
`~/Projects/Builds/demo-tools/`). Перед вставкой проверить, что юр-ссылки живые
(https://choosereply.com/privacy.html, https://choosereply.com/terms.html). Тот же текст пригодится для ответа на вопросы Google Play.

ВАЖНО (аудит 2026-08-30, не откатывать): в notes НЕТ ни номера гайдлайна 5.2.2,
ни имени вендора-шлюза — сами notes не должны подсказывать ревьюеру формулировку
вопроса и гуглибельного вендора. Все содержательные тезисы (свой аккаунт, как
WhatsApp Web, только входящие, отвязка в любой момент) — остаются. НЕ добавлять:
утверждений об «авторизации от WhatsApp» (её нет — это 2.3.1(a)-риск), анализа
чьих-либо ToS, слов «unofficial»/«gateway vendor names».

---

**What the app is.**
Choose Reply is a business tool for small-business owners in Kazakhstan/CIS. The
owner connects their OWN WhatsApp and/or Telegram account, describes their
business (services, price lists, working details), and the app answers their
customers' incoming messages with AI — either fully automatically ("Auto" mode)
or semi-automatically, where the owner picks one of the suggested replies
("Together" mode). The app UI is Russian-only, targeting the Kazakhstan market.

**Why we cannot provide a demo account.**
There is no login/account system in the app itself. The core flow requires
linking a REAL WhatsApp account by scanning a QR code (or entering a pairing
code) from the WhatsApp mobile app on a live phone number, and then having a
real customer send messages to that number. This cannot be reproduced with test
credentials. We recorded a full demo video showing the entire flow end-to-end
with a real WhatsApp number (recorded in the iOS Simulator; the linked
WhatsApp account and the customer messages are real):

**Demo video: https://choosereply.com/review/923843a986255cba/ChooseReply-demo-2026-09.mp4**

**What you CAN test directly in the review build without any account:**
1. First-run onboarding.
2. Bot creation wizard up to the WhatsApp/Telegram authorization screen
   (QR + pairing code UI).
3. The paywall: open it from Profile → «Подписка» → «Изменить тариф»
   ("Change plan"). Monthly/yearly toggle, three tiers, feature list, Terms of
   Use and Privacy Policy links at the bottom, and "Restore purchases"
   («Восстановить покупки»). (If a trial is already running, the trial pill in
   the header of the Bots tab opens the same paywall.)
4. Sandbox purchase and restore of any subscription tier work with a sandbox
   Apple ID as usual (products: 3 tiers × monthly/yearly + one consumable
   dialog top-up).

**About the free trial.**
The 5-day trial is app-level functionality: it starts when the user first
connects a messenger channel, requires no payment method, and no payment is
requested outside of standard auto-renewable In-App Purchases. All payments go
exclusively through StoreKit. The trial button on the paywall
(«Попробовать 5 дней бесплатно») does not initiate any purchase.

**About messenger connectivity.**
The app connects only accounts that belong to the user themselves — the user
authorizes their own WhatsApp/Telegram via QR/pairing code exactly like linking
WhatsApp Web/Desktop. The user can unlink the account at any time, either from
the app or from WhatsApp's own "Linked Devices" screen (shown in the demo
video). The app does not access other people's accounts, does not send bulk or
unsolicited messages, and is designed for replying to inbound customer messages
of the user's own business. Messaging is handled by a server-side gateway
operated by us; AI replies are generated server-side. Data handling is described
in the privacy policy linked on the paywall and in the App Store metadata.

**Google Play — «App access» (вставить в Policy → App content → App access → инструкции).**
Тот же смысл, но без слов Apple/StoreKit/App Store; готовый EN-блок лежит в
`docs/store/play-console.md` §3.1 (логина нет; нужен реальный аккаунт мессенджера;
демо-видео; покупки — только Google Play Billing, тестовые аккаунты через
License testing).

**Subscription disclosure locations.**
Price and period are shown on each tier card; the auto-renewal notice is shown
under the CTA; Terms of Use and Privacy Policy are linked directly on the
paywall and in the App Store metadata.

**Contact for review questions:** synergyexpertgroup@gmail.com
