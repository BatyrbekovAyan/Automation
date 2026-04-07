using UnityEngine;

public static class AndroidBridge
{
#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaClass player =
        new AndroidJavaClass("com.unity.audio.AudioPlayer");
#endif

    public static void Play(string url)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        player.CallStatic("play", url);
#endif
    }

    public static void Pause()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        player.CallStatic("pause");
#endif
    }

    public static void Resume()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        player.CallStatic("resume");
#endif
    }

    public static void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        player.CallStatic("stop");
#endif
    }
    
    public static void Seek(float seconds)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    player.CallStatic("seekTo", seconds);
#endif
    }

    // ⭐ THE NEW PROXIMITY SENSOR BRIDGE
    public static void ToggleProximity(bool enable)
    {
        // Double-check we are actually on an Android device to prevent Editor crashes
        if (Application.platform != RuntimePlatform.Android) return;

        try
        {
            // Reach into your exact Java package and class
            using (AndroidJavaClass audioPlayerClass = new AndroidJavaClass("com.unity.audio.AudioPlayer"))
            {
                // Call the static Java method and pass the boolean over!
                audioPlayerClass.CallStatic("toggleProximity", enable);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("AndroidBridge Error (ToggleProximity): " + e.Message);
        }
    }
}