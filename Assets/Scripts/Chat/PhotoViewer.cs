using UnityEngine;
using UnityEngine.UI;

public class PhotoViewer : MonoBehaviour
{
    public static PhotoViewer Instance;

    public GameObject panel;
    public Image fullScreenImage;

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

        // Auto-setup
        if (!panel) panel = gameObject;
        if (!fullScreenImage) fullScreenImage = GetComponentInChildren<Image>();

        // Hide on startup
        panel.SetActive(false);
    }

    public void ShowImage(Sprite sprite)
    {
        if (sprite == null) return;

        panel.SetActive(true);
        fullScreenImage.sprite = sprite;
        
        // "Preserve Aspect" does the heavy lifting for us here
        fullScreenImage.preserveAspect = true; 
    }

    public void Close()
    {
        panel.SetActive(false);
        fullScreenImage.sprite = null;
    }
}