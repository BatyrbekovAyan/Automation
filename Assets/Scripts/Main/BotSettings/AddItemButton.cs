using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>Dashed-border "+ Добавить товар" button. Styling in prefab.</summary>
    public class AddItemButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        public UnityEvent OnTap = new UnityEvent();

        private void Awake() => button.onClick.AddListener(() => OnTap.Invoke());
        private void OnDestroy() => button.onClick.RemoveAllListeners();
    }
}
