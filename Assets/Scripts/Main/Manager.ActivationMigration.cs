using System.Collections;
using UnityEngine;

// 2026-08 capsule↔ReplyMode unification (sketch 006 follow-up). The bots-page
// «Авто» capsule stopped driving the master activation key (bare botName) and
// became the bot's ReplyMode; the master key is dead storage. Bots the owner
// paused with the OLD switch sit with their n8n workflows deactivated — and no
// UI could ever wake them again — so this one-shot, retry-safe migration turns
// «на паузе» into its new-model equivalent, «Вместе»: workflow active, replies
// suppressed server-side. Pure decision matrix in BotActivationMigration
// (EditMode-tested); this partial is only the PlayerPrefs/network I/O.
public partial class Manager
{
    /// <summary>
    /// Called from the top of LoadBots(). The ForceSemi pref write happens
    /// synchronously (before the cards instantiate and read the mode); each
    /// bot's network work runs as its own background coroutine.
    /// </summary>
    private void MigratePausedBotsActivation()
    {
        for (int i = 0; i < id; i++)
        {
            string botKey = "Bot" + i.ToString();
            if (!PlayerPrefs.HasKey(botKey + "Name")) continue;

            var plan = BotActivationMigration.Plan(
                PlayerPrefs.GetInt(botKey, 1),
                // Through the binder, not a hand-rolled key read: it owns the
                // "ReplyMode" suffix and the semi default, and a drift between the
                // two would silently disarm the ForceSemi safety pin below.
                (int)ReplyModeToggleBinder.GetMode(botKey),
                PlayerPrefs.GetInt(botKey + "isOnWhatsapp", 1) == 1,
                PlayerPrefs.GetInt(botKey + "isOnTelegram", 1) == 1,
                PlayerPrefs.GetString(botKey + "WhatsappProfileId", Bot.UnauthedProfileSentinel),
                PlayerPrefs.GetString(botKey + "TelegramProfileId", Bot.UnauthedProfileSentinel),
                PlayerPrefs.GetString(botKey + "WhatsappWorkflowId", Bot.UnauthedProfileSentinel),
                PlayerPrefs.GetString(botKey + "TelegramWorkflowId", Bot.UnauthedProfileSentinel));
            if (!plan.NeedsMigration) continue;

            // Paused-while-Auto pins to Semi NOW, before any card reads the mode —
            // a rescued bot must never resurrect auto-replying.
            if (plan.ForceSemi)
            {
                PlayerPrefs.SetInt(botKey + "ReplyMode", (int)ReplyModeToggleBinder.ReplyMode.Semi);
                PlayerPrefs.Save();
            }

            StartCoroutine(MigrateOneBotRoutine(botKey, plan));
        }
    }

    private IEnumerator MigrateOneBotRoutine(string botKey, BotActivationMigration.MigrationPlan plan)
    {
        bool allOk = true;

        // 1. Suppression rows FIRST — the server reads row-absence as «reply»
        // (LOCKED semantics), so activating before the row lands would let a
        // rescued bot answer a client in the gap.
        if (plan.SuppressProfileIds.Length > 0)
        {
            bool suppressed = false;
            yield return SyncReplyModeRoutine(
                BuildReplyModePayload(plan.SuppressProfileIds, "*", true), ok => suppressed = ok);
            allOk &= suppressed;
        }

        // 2. Re-activate the enabled channels' workflows (idempotent on n8n).
        if (allOk)
        {
            foreach (string workflowId in plan.ActivateWorkflowIds)
            {
                bool activated = false;
                yield return SetWorkflowActiveRoutine(workflowId, true, ok => activated = ok);
                allOk &= activated;
            }
        }

        // 3. Only a fully-landed migration clears the dead master key — an
        // offline/failed launch keeps it, so the next launch retries (every
        // request above is an idempotent upsert/activate).
        if (allOk)
        {
            PlayerPrefs.DeleteKey(botKey);
            PlayerPrefs.Save();
            Debug.Log($"[ActivationMigration] {botKey}: paused → Вместе (suppressed first, workflows re-activated).");
        }
        else
        {
            Debug.LogWarning($"[ActivationMigration] {botKey}: incomplete — will retry next launch.");
        }
    }
}
