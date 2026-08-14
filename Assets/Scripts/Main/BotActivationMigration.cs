using System.Collections.Generic;

/// <summary>
/// Plan for the one-shot 2026-08 unification migration: the bots-page capsule
/// stopped driving the master activation key (bare botName) and became the
/// bot's ReplyMode — the same store as the chats-header «Авто» button. A bot
/// the owner PAUSED with the old switch has its n8n workflows deactivated and,
/// with the master key dead, no UI could ever re-activate them. The safe
/// equivalent of «на паузе» in the new model is «Вместе»: workflow active,
/// replies suppressed server-side.
///
/// Pure so EditMode tests pin the matrix; Manager.ActivationMigration.cs does
/// the PlayerPrefs/network I/O. Ordering invariant (enforced by the consumer):
/// the '*' suppression row is written BEFORE workflows re-activate — the
/// server reads ABSENCE of a row as «reply» (LOCKED semantics), so a
/// default-Semi bot with no row would auto-reply the moment its workflow wakes.
/// </summary>
public static class BotActivationMigration
{
    public readonly struct MigrationPlan
    {
        /// <summary>False for bots that were never paused — nothing to do.</summary>
        public readonly bool NeedsMigration;

        /// <summary>Stored mode was Auto — pin it to Semi so the pause survives as «Вместе».</summary>
        public readonly bool ForceSemi;

        /// <summary>Profile ids needing the '*' suppressed=true row, BEFORE any activation.</summary>
        public readonly string[] SuppressProfileIds;

        /// <summary>Workflow ids to re-activate: channel toggle on + real id only.</summary>
        public readonly string[] ActivateWorkflowIds;

        public MigrationPlan(bool needsMigration, bool forceSemi,
            string[] suppressProfileIds, string[] activateWorkflowIds)
        {
            NeedsMigration = needsMigration;
            ForceSemi = forceSemi;
            SuppressProfileIds = suppressProfileIds;
            ActivateWorkflowIds = activateWorkflowIds;
        }
    }

    private static readonly string[] None = System.Array.Empty<string>();

    /// <summary>
    /// <paramref name="masterValue"/> is the bare-key read with its historical
    /// default (1 = on): only an explicit 0 — a bot actually paused with the
    /// old switch — migrates. <paramref name="storedReplyMode"/> is the raw
    /// ReplyMode read (0 = Auto, 1 = Semi, default Semi).
    /// </summary>
    public static MigrationPlan Plan(int masterValue, int storedReplyMode,
        bool waEnabled, bool tgEnabled,
        string waProfileId, string tgProfileId,
        string waWorkflowId, string tgWorkflowId)
    {
        if (masterValue != 0) return new MigrationPlan(false, false, None, None);

        var suppress = new List<string>(2);
        if (IsReal(waProfileId)) suppress.Add(waProfileId);
        if (IsReal(tgProfileId)) suppress.Add(tgProfileId);

        var activate = new List<string>(2);
        if (waEnabled && IsReal(waWorkflowId)) activate.Add(waWorkflowId);
        if (tgEnabled && IsReal(tgWorkflowId)) activate.Add(tgWorkflowId);

        // Suppress ALWAYS (not only when ForceSemi): a paused bot that was
        // default-Semi may have no server row at all, and absence reads as «reply».
        return new MigrationPlan(
            needsMigration: true,
            forceSemi: storedReplyMode == 0,
            suppressProfileIds: suppress.ToArray(),
            activateWorkflowIds: activate.ToArray());
    }

    // Profile AND workflow ids share the same sentinel scheme ("" never set,
    // "-1" never authed) — BotCardModel.IsConnected is that exact predicate.
    private static bool IsReal(string id) => BotCardModel.IsConnected(id);
}
