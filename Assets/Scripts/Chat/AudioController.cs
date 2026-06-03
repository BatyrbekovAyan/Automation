using UnityEngine;
using System;
using UnityEngine.UI;
using System.Runtime.InteropServices; // Required for iOS DllImport
using System.Globalization;

public class AudioController : MonoBehaviour
{
    public static AudioController Instance;

    public event Action<string> OnAudioStarted; 
    public event Action<string> OnAudioStopped; 
    public event Action<string, float, float> OnAudioProgress;

    public Slider progressSlider;

    private string currentUrl;
    private bool isPaused = false;

    public static float CurrentSpeed { get; private set; } = 1f;
    public event Action<float> OnSpeedChanged;

    // --- NATIVE IOS LINK ---
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _ToggleProximitySensor(bool enable);
#endif

    void Awake()
    {
        Instance = this;
    }

    // Safety Catch: Turn off the sensor if the app gets minimized or destroyed!
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) SetProximitySensor(false);
        else if (!string.IsNullOrEmpty(currentUrl) && !isPaused) SetProximitySensor(true);
    }

    void OnDisable()
    {
        SetProximitySensor(false);
    }

    public void ToggleAudio(string url)
    {
        if (currentUrl == url)
        {
            if (isPaused) Resume();
            else Pause();
        }
        else
        {
            PlayAudio(url);
        }
    }

    public void PlayAudio(string url)
    {
        if (!string.IsNullOrEmpty(currentUrl)) OnAudioStopped?.Invoke(currentUrl);
        
        currentUrl = url;
        isPaused = false;
        
        OnAudioStarted?.Invoke(url);

        // --- THE FIX: Turn sensor ON ---
        SetProximitySensor(true);

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Play(url);
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.PlayUrl(url);
#endif
    }

    public void Pause()
    {
        isPaused = true;
        if (!string.IsNullOrEmpty(currentUrl)) OnAudioStopped?.Invoke(currentUrl);
        
        // --- THE FIX: Turn sensor OFF ---
        SetProximitySensor(false);

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Pause();
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Pause();
#endif
    }

    public void Resume()
    {
        isPaused = false;
        if (!string.IsNullOrEmpty(currentUrl)) OnAudioStarted?.Invoke(currentUrl);
        
        // --- THE FIX: Turn sensor ON ---
        SetProximitySensor(true);

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Resume();
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Resume();
#endif
    }

    public void Stop()
    {
        if (!string.IsNullOrEmpty(currentUrl)) OnAudioStopped?.Invoke(currentUrl);
        currentUrl = null;
        isPaused = false;

        // --- THE FIX: Turn sensor OFF ---
        SetProximitySensor(false);

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Stop();
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Stop();
#endif
    }

    public void Seek(float seconds)
    {
        if (string.IsNullOrEmpty(currentUrl)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Seek(seconds);
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Seek(seconds);
#endif
    }

    public void SeekTo(string url, float seconds)
    {
        if (currentUrl != url) PlayAudio(url);
        Seek(seconds);
    }

    public void CycleSpeed() => SetSpeed(AudioBubbleMath.NextSpeed(CurrentSpeed));

    public void SetSpeed(float speed)
    {
        CurrentSpeed = speed;
        OnSpeedChanged?.Invoke(speed);
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.SetSpeed(speed);
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.SetSpeed(speed);
#endif
    }

    public void OnNativeProgress(string data)
    {
        var parts = data.Split('|');
        string url = parts[0];
        
        // --- THE FIX: Ignore ghost messages! ---
        // If this URL is no longer the actively playing track, drop the message instantly!
        if (url != currentUrl) return; 

        float pos = float.Parse(parts[1], CultureInfo.InvariantCulture);
        float dur = float.Parse(parts[2], CultureInfo.InvariantCulture);

        OnAudioProgress?.Invoke(url, pos, dur);
    }

    // --- THE FIX: Catch the finished signal from Android/iOS ---
    public void OnNativeAudioFinished(string url)
    {
        // Make sure the audio that finished is actually the one we are tracking
        if (currentUrl == url)
        {
            // 1. Force the UI slider to snap perfectly back to 0 seconds
            OnAudioProgress?.Invoke(url, 0f, 1f); 
            
            // 2. Trigger the standard Stop routine (flips the button sprite and turns off proximity)
            Stop();
        }
    }
    
    // --- HELPER METHOD TO ROUTE TO NATIVE PLUGINS ---
    private void SetProximitySensor(bool enable)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _ToggleProximitySensor(enable);
#elif UNITY_ANDROID && !UNITY_EDITOR
        // Assuming you will add this method to your AndroidBridge Java code!
        AndroidBridge.ToggleProximity(enable); 
#endif
    }
}