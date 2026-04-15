using UnityEngine;

[CreateAssetMenu(menuName = "Automation/Business Icons", fileName = "BusinessIcons")]
public class BusinessIconsSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public Sprite sprite;
        public Color tileColor;
    }

    [SerializeField] private Entry[] entries;

    public Entry[] Entries => entries;
    public int Count => entries == null ? 0 : entries.Length;

    public bool TryGet(int index, out Entry entry)
    {
        if (entries != null && index >= 0 && index < entries.Length)
        {
            entry = entries[index];
            return true;
        }
        entry = default;
        return false;
    }
}
