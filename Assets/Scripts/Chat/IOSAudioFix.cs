using UnityEngine;
using System.Runtime.InteropServices;

public class IOSAudioFix : MonoBehaviour
{
    // Link to our native .m file
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _ForceIOSPlaybackMode();
#endif

    void Start()
    {
        // Trigger it the second the app boots up!
#if UNITY_IOS && !UNITY_EDITOR
        _ForceIOSPlaybackMode();
#endif
    }
}