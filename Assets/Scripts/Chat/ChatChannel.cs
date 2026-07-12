/// <summary>
/// The messaging channel a chat / bot profile belongs to.
/// Persisted as its int ordinal (PlayerPrefs, OutboxEntry.channel), so the
/// values are load-bearing and MUST stay stable: WhatsApp = 0, Telegram = 1.
/// Plain enum — no MonoBehaviour, no namespace — matching the flat Chat/ file style.
/// </summary>
public enum ChatChannel
{
    WhatsApp = 0,
    Telegram = 1,
}
