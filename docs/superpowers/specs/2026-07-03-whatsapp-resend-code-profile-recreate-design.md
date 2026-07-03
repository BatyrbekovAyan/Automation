# WhatsApp Resend Code — Silent Profile Recreate

**Date:** 2026-07-03
**Status:** Approved (autonomous session — decisions documented in lieu of interactive Q&A; owner described the exact desired behavior)

## Problem

WhatsApp pairing codes issued via Wappi (`/api/sync/auth/code`) frequently fail to authorize on the first attempt. WhatsApp will not issue a fresh code for the **same profile** for ~2 minutes. The owner's manual workaround: go back (deletes bot + Wappi profile), create a new bot (new profile), request a code — a brand-new profile gets a code instantly. Clients don't know this trick and are stuck waiting 2+ minutes.

## Goal

The resend button ("Получить другой код") already enables after the existing 30s `WhatsappCodeTimer` cooldown — unchanged. When pressed, the app should **silently** perform the workaround behind the standard loading overlay: delete the current Wappi profile, create a replacement **with the same name**, and request the code on it. To the client it looks exactly like a normal "get another code" press: spinner → new code appears → 30s timer restarts.

## Current architecture (as explored)

- One shared auth page + one `GetWhatsappCode()` coroutine serve **both** the Add-Bot wizard and the Bot-Settings re-auth flows. `Manager.whatsappProfileId` is the single id used by the code request, QR poll (`OpenWhatsappQRPanel`), and status poll (`GetWhatsappProfileStatus`, which re-reads the field every 5s iteration).
- **Wizard:** profile created on auth entry; Bot object + n8n workflow are created only *after* `whatsappAuthCompleted`. A mid-auth profile swap is invisible downstream.
- **Settings:** every path that reaches the code panel has **no live WhatsApp workflow** (fresh-enable creates it only in `OnWhatsappAuthFromSettingsDone`; change-number and logout call `GetDeleteWhatsappWorkflow` first). So a swap is safe there too, but the new id must be mirrored to the Bot component + `Bot{N}WhatsappProfileId` PlayerPrefs, because the done-handler and `CreateWhatsappWorkflowFromEdit` read the **bot's** id.
- Orphan safety: `CreateWhatsappProfile` records `lastCreatedWhatsappProfileId` with `...Saved=0`; startup deletes unsaved profiles.

## Design

All changes in `Assets/Scripts/Main/Manager.cs` (no scene/prefab/UI changes, no new files).

New state: `_whatsappCodeIssued` (true once a code was issued for the current profile → that profile is cooldown-poisoned), `_whatsappQrCoroutine` (handle so the QR poll can be stopped/restarted).

`GetWhatsappCode()` gains a pre-step when `_whatsappCodeIssued || whatsappProfileId == "-1"`:

1. Stop the status poll, then one-shot `get/status` check on the old profile. **If it reports authorized, abort the recreate** (the first code actually worked — user pressed resend right as auth landed), restart the status poll so the success UI appears, and bail. Prevents deleting an authorized profile.
2. `RecreateWhatsappProfileForNewCode()`:
   - Delete old profile via existing `DeleteWhatsappProfile(id, true)` (delete-first mirrors the proven manual flow and frees the Wappi profile slot before adding). If delete fails → keep the old profile and fall through; Wappi's cooldown error shows on the button exactly as today.
   - Create replacement via existing `CreateWhatsappProfile(name, true)` — same name: wizard → `formBotName`, settings → saved bot name. Up to 3 attempts, 2s apart (creation failure is silent in the existing coroutine — detected by the id staying "-1").
   - Settings mode: mirror new id to Bot component + PlayerPrefs, mark `lastCreatedWhatsappProfileIdSaved=1` (the id is now referenced by a persisted bot; startup cleanup must not reap it). On total create failure, clear the bot's id to "-1" so the next toggle-on re-provisions fresh instead of dead-ending on a deleted id. Wizard mode: no PlayerPrefs — the existing `lastCreated…Saved=0` crash-cleanup covers abandonment.
   - Restart the QR poll — the displayed QR belonged to the deleted profile; the fresh poll re-points it and its built-in 3s delay doubles as Wappi provisioning time.
   - Wait 3s (provisioning, mirrors `OpenWhatsappQRPanel`), clear `_whatsappCodeIssued`.
3. Fall through to the unchanged code request → new code displayed, `WhatsappCodeTimer` restarts (its `OnEnable` starts a fresh 30s window), status poll restarts, `_whatsappCodeIssued` set true again.
4. If recreation failed outright (id still "-1"): flash the standard button error and re-enable — a retry press re-enters the provision path (delete skipped for "-1"), so the flow self-heals. This also self-heals the pre-existing silent-failure case where the wizard's initial profile creation failed.

`ChangeWhatsappNumber` (in-page number edit) intentionally leaves `_whatsappCodeIssued` true: the profile is still inside WhatsApp's cooldown, so the next "Получить код" press for the new number also silently recreates. `ShowWhatsappAuth` resets the flag (new auth session).

Telegram is out of scope (different code-entry model; no reported cooldown pain).

## Failure modes

| Failure | Behavior |
|---|---|
| Resend pressed just as first code authorizes | Pre-delete status check catches it; success UI proceeds |
| `profile/delete` fails | Old profile kept; plain code request → Wappi error on button (today's behavior) |
| `profile/add` fails ×3 | Button error flash; bot id cleared to "-1" in settings; retry re-provisions |
| App killed mid-recreate (wizard) | `lastCreated…Saved=0` → startup deletes orphan |
| App killed mid-recreate (settings) | Bot PlayerPrefs already point at new saved profile → next toggle-on resumes auth on it |
| User scans QR after resend | QR poll restarted for new profile → QR stays valid |

## Testing

No new pure logic to unit-test (the decision is two booleans; everything else is UnityWebRequest coroutine orchestration reusing production-proven primitives — the project has no web-request mocking seam and building one is out of scope). Verification = full EditMode suite still compiles/passes + owner device pass on the wizard resend and settings change-number resend.
