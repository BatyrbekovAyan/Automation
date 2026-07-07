using UnityEngine;

/// <summary>
/// Plays the local incoming-message cue (short clip + vibration) gated by the
/// Profile → Уведомления switches. Lives on ChatManager's always-active
/// GameObject (added and wired by ProfileSubPagesBuilder) — Screen_Profile
/// itself is inactive on other tabs, so the cue can't live there.
/// </summary>
public class NotificationFx : MonoBehaviour
{
    public static NotificationFx Instance;

    [SerializeField] private AudioClip incomingClip;

    private AudioSource _source;

    private void Awake()
    {
        Instance = this;
        _source = GetComponent<AudioSource>();
        if (_source == null) _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
    }

    /// <summary>Static entry so ChatManager's parse path needs no serialized ref.</summary>
    public static void OnIncomingDetected()
    {
        if (Instance != null) Instance.PlayIncoming();
    }

    private void PlayIncoming()
    {
        if (NotifPrefs.SoundEnabled && incomingClip != null && _source != null)
            _source.PlayOneShot(incomingClip);

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (NotifPrefs.VibrationEnabled)
            Handheld.Vibrate();
#endif
    }
}
