using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace Automation.BotSettingsUI
{
    /// <summary>Wraps the existing Saved GameObject with fade in / auto-hide.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SavedToast : MonoBehaviour
    {
        [SerializeField] private float visibleDuration = 1.5f;
        [SerializeField] private float fadeDuration = 0.2f;
        private CanvasGroup group;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            gameObject.SetActive(false);
        }

        public void Show()
        {
            StopAllCoroutines();
            StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            gameObject.SetActive(true);
            group.DOKill();
            group.DOFade(1f, fadeDuration);
            yield return new WaitForSeconds(visibleDuration);
            group.DOFade(0f, fadeDuration).OnComplete(() => gameObject.SetActive(false));
        }
    }
}
