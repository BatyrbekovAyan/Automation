# App Review Notes (вставить в App Store Connect → App Review Information → Notes)

Ниже готовый EN-текст. Перед вставкой: заменить `<APP NAME>` на финальное имя,
`<VIDEO URL>` на ссылку демо-видео (непубличный YouTube/Drive), проверить, что
юр-ссылки уже живые. Тот же текст пригодится для ответа на вопросы Google Play.

---

**What the app is.**
<APP NAME> is a business tool for small-business owners in Kazakhstan/CIS. The
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
on a real device and number:

**Demo video: <VIDEO URL>**

**What you CAN test directly in the review build without any account:**
1. First-run onboarding.
2. Bot creation wizard up to the WhatsApp/Telegram authorization screen
   (QR + pairing code UI).
3. The paywall: open it from the trial pill in the header of the Bots tab, or
   from Profile → «Подписка» («Изменить тариф»). Monthly/yearly toggle, three
   tiers, feature list, Terms of Use and Privacy Policy links at the bottom,
   and "Restore purchases" («Восстановить покупки»).
4. Sandbox purchase and restore of any subscription tier work with a sandbox
   Apple ID as usual (products: 3 tiers × monthly/yearly + one consumable
   dialog top-up).

**About the free trial.**
The 5-day trial is app-level functionality: it starts when the user first
connects a messenger channel, requires no payment method, and no payment is
requested outside of standard auto-renewable In-App Purchases. All payments go
exclusively through StoreKit. The trial button on the paywall
(«Попробовать 5 дней бесплатно») does not initiate any purchase.

**About messenger connectivity (Guideline 5.2.2 context).**
The app connects only accounts that belong to the user themselves — the user
authorizes their own WhatsApp/Telegram via QR/pairing code exactly like linking
WhatsApp Web/Desktop. The app does not access other people's accounts, does not
send bulk/unsolicited messages, and is designed for replying to inbound customer
messages of the user's own business. Messaging transport is provided by the
Wappi service; AI replies are generated server-side (OpenAI).

**Subscription disclosure locations.**
Price and period are shown on each tier card; the auto-renewal notice is shown
under the CTA; Terms of Use and Privacy Policy are linked directly on the
paywall and in the App Store metadata.

**Contact for review questions:** synergyexpertgroup@gmail.com
