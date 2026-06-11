using System.Runtime.InteropServices;

public static class IOSBridge
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void playUrl(string url);

    [DllImport("__Internal")]
    private static extern void pausePlayer();

    [DllImport("__Internal")]
    private static extern void resumePlayer();

    [DllImport("__Internal")]
    private static extern void stopPlayer();
#endif

    public static void PlayUrl(string url)
    {
#if UNITY_IOS && !UNITY_EDITOR
        playUrl(url);
#endif
    }

    public static void Pause()
    {
#if UNITY_IOS && !UNITY_EDITOR
        pausePlayer();
#endif
    }

    public static void Resume()
    {
#if UNITY_IOS && !UNITY_EDITOR
        resumePlayer();
#endif
    }

    public static void Stop()
    {
#if UNITY_IOS && !UNITY_EDITOR
        stopPlayer();
#endif
    }
    
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void seekPlayer(float seconds);
#endif

    public static void Seek(float seconds)
    {
#if UNITY_IOS && !UNITY_EDITOR
    seekPlayer(seconds);
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void setSpeed(float speed);
#endif

    public static void SetSpeed(float speed)
    {
#if UNITY_IOS && !UNITY_EDITOR
    setSpeed(speed);
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _SetDarkKeyboard(bool enable);
#endif

    // Overrides the window interface style so the system keyboard renders dark
    // (WhatsApp attachment-preview parity). Callers must pair enable=true with
    // enable=false on every exit path, or all keyboards in the app stay dark.
    // No-op on Android/Editor: Android offers no API to theme the IME.
    public static void SetDarkKeyboard(bool enable)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _SetDarkKeyboard(enable);
#endif
    }

}