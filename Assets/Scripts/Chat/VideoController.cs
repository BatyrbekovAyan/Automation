using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Runtime.InteropServices;

public class VideoController : MonoBehaviour
{
    public static VideoController Instance;

    public VideoPlayer videoPlayer;
    public RawImage rawImage;

    // Assign the Panel (Black Background) here so we know the screen size
    public RectTransform containerRect;

    // Optional: a spinner shown while the clip is preparing and hidden the moment it is ready or
    // fails. Leave unassigned to keep the legacy plain-black-while-loading behaviour.
    [SerializeField] private GameObject preparingSpinner;

    // A Prepare() that never completes (expired/403 link, undecodable HEVC/.mov, stalled remote
    // read) would otherwise leave the screen black forever — errorReceived does not fire for every
    // stall. This watchdog bails out and closes the player so the user is returned to the chat
    // instead of staring at black. Tuned generously so a slow-but-valid clip still gets to play.
    private const float PrepareTimeoutSec = 25f;
    private Coroutine prepareTimeoutCo;

    private float apiAspectRatio = 0f;
    private float videoRotation = 0f;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _ForceIOSPlaybackMode();
#endif
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (!videoPlayer) videoPlayer = GetComponentInChildren<VideoPlayer>();
        if (!rawImage) rawImage = GetComponentInChildren<RawImage>();
        
        if (!containerRect) containerRect = GetComponent<RectTransform>();

        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.APIOnly;
        videoPlayer.prepareCompleted += OnPrepareCompleted;
        videoPlayer.errorReceived += OnVideoError;

        if (preparingSpinner) preparingSpinner.SetActive(false);

        gameObject.SetActive(false);
    }

    public void PlayVideo(string url, float aspectRatio, float videoRotation = 0f)
    {
        // VideoPlayer can only decode a real http(s) URL or a local file path. An empty or
        // base64:// source (the /media/download fallback payload) would silently fail Prepare and
        // leave the screen black — reject it up front instead of opening a dead black player.
        if (string.IsNullOrEmpty(url) || url.StartsWith("base64://"))
        {
            Debug.LogWarning($"[VideoController] refusing unplayable source: {(string.IsNullOrEmpty(url) ? "<empty>" : "base64")}");
            FailPlayback();
            return;
        }

        gameObject.SetActive(true);

        // --- FIX: PREVENT WHITE FLASH ---
        // 1. Clear the old texture immediately
        rawImage.texture = null;

        // 2. Turn the screen BLACK while loading
        rawImage.color = Color.black;
        if (preparingSpinner) preparingSpinner.SetActive(true);

        this.apiAspectRatio = aspectRatio;
        this.videoRotation = videoRotation;

        // Reset Transform
        rawImage.rectTransform.localRotation = Quaternion.identity;
        rawImage.rectTransform.localScale = Vector3.one;
        rawImage.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rawImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rawImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        videoPlayer.url = url;
        videoPlayer.Prepare();

        if (prepareTimeoutCo != null) StopCoroutine(prepareTimeoutCo);
        prepareTimeoutCo = StartCoroutine(PrepareTimeoutRoutine());
    }

    void OnPrepareCompleted(VideoPlayer vp)
    {
        if (prepareTimeoutCo != null) { StopCoroutine(prepareTimeoutCo); prepareTimeoutCo = null; }
        if (preparingSpinner) preparingSpinner.SetActive(false);

        // --- FIX: SHOW VIDEO ---
        // 3. Video is ready! Assign texture.
        rawImage.texture = vp.texture;
        
        // 4. Turn screen WHITE so the video colors show correctly
        // (If we leave it black, the video will look very dark or invisible)
        rawImage.color = Color.white; 

        // --- ASPECT RATIO LOGIC ---
        float textureRatio = (float)vp.texture.width / vp.texture.height;
        bool shouldRotate;
        float rotZ;

        if (videoRotation != 0f)
        {
            // Known rotation (staged clip): apply it exactly. Unity's VideoPlayer decodes the
            // raw (unrotated) frame, so rotate the RawImage by -rotation to display upright.
            rotZ = -videoRotation;
            shouldRotate = (videoRotation == 90f || videoRotation == 270f);
        }
        else
        {
            // Unknown rotation (e.g. server video): fall back to the aspect-ratio heuristic.
            bool isTextureLandscape = textureRatio > 1.2f;
            bool isApiNotLandscape = apiAspectRatio > 0f && apiAspectRatio < 1.1f;
            shouldRotate = isTextureLandscape && isApiNotLandscape;
            rotZ = shouldRotate ? -90f : 0f;
        }

        // Remove old fitter
        var fitter = rawImage.GetComponent<AspectRatioFitter>();
        if (fitter) Destroy(fitter);

        // Apply Size + rotation
        rawImage.rectTransform.sizeDelta = new Vector2(vp.texture.width, vp.texture.height);
        rawImage.rectTransform.localRotation = Quaternion.Euler(0, 0, rotZ);

        // Calculate Scale (swap visual dims for 90/270)
        float visualWidth = shouldRotate ? vp.texture.height : vp.texture.width;
        float visualHeight = shouldRotate ? vp.texture.width : vp.texture.height;

        float parentWidth = containerRect.rect.width;
        float parentHeight = containerRect.rect.height;

        float widthScale = parentWidth / visualWidth;
        float heightScale = parentHeight / visualHeight;
        float finalScale = Mathf.Min(widthScale, heightScale);

        rawImage.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);

#if UNITY_IOS && !UNITY_EDITOR
        _ForceIOSPlaybackMode();
#endif

        vp.Play();
    }

    // VideoPlayer surfaces decode/network failures here. Without this handler a failed Prepare
    // left OnPrepareCompleted unreached and the screen black forever — the core of the
    // "tap a stuck video → black fullscreen" symptom.
    void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogWarning($"[VideoController] playback error: {message}");
        // Only tear down if the clip never started. An error during Prepare is the black-screen
        // failure we must escape; an error after a successful prepare (a rare mid-stream hiccup)
        // shouldn't yank a video the user is already watching.
        if (!vp.isPrepared) FailPlayback();
    }

    // Watchdog for a Prepare() that neither completes nor errors (some stalled remote reads do
    // exactly that). Closes the player so the user returns to the chat rather than to black.
    IEnumerator PrepareTimeoutRoutine()
    {
        float elapsed = 0f;
        while (elapsed < PrepareTimeoutSec)
        {
            if (videoPlayer.isPrepared) { prepareTimeoutCo = null; yield break; }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        prepareTimeoutCo = null;
        Debug.LogWarning($"[VideoController] Prepare timed out after {PrepareTimeoutSec:0}s — closing.");
        FailPlayback();
    }

    // Tear down a failed playback attempt and return to the chat. Closing beats an indefinite
    // black screen; the originating bubble now shows a dark card + play button so the user can
    // retry the tap.
    void FailPlayback()
    {
        if (prepareTimeoutCo != null) { StopCoroutine(prepareTimeoutCo); prepareTimeoutCo = null; }
        if (preparingSpinner) preparingSpinner.SetActive(false);
        CloseVideo();
    }

    public void CloseVideo()
    {
        if (prepareTimeoutCo != null) { StopCoroutine(prepareTimeoutCo); prepareTimeoutCo = null; }
        if (preparingSpinner) preparingSpinner.SetActive(false);
        videoPlayer.Stop();
        gameObject.SetActive(false);
        rawImage.texture = null;
        rawImage.color = Color.black; // Reset to black on close just in case
    }
}