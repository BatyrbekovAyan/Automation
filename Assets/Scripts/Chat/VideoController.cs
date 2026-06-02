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

        gameObject.SetActive(false); 
    }

    public void PlayVideo(string url, float aspectRatio, float videoRotation = 0f)
    {
        gameObject.SetActive(true);

        // --- FIX: PREVENT WHITE FLASH ---
        // 1. Clear the old texture immediately
        rawImage.texture = null;

        // 2. Turn the screen BLACK while loading
        rawImage.color = Color.black;

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
    }

    void OnPrepareCompleted(VideoPlayer vp)
    {
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

    public void CloseVideo()
    {
        videoPlayer.Stop();
        gameObject.SetActive(false);
        rawImage.texture = null;
        rawImage.color = Color.black; // Reset to black on close just in case
    }
}