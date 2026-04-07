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

}