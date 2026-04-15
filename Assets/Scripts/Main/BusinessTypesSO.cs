using UnityEngine;

[CreateAssetMenu(menuName = "Automation/Business Types", fileName = "BusinessTypes")]
public class BusinessTypesSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public string id;          // stable kebab-case key, e.g. "beauty_salon"
        public string displayName; // user-facing, e.g. "Beauty Salon"
        public Sprite sprite;
        public Color tileColor;
    }

    [SerializeField] private Entry[] entries;

    public Entry[] All => entries ?? System.Array.Empty<Entry>();
    public int Count => entries == null ? 0 : entries.Length;

    public bool TryGetById(string id, out Entry entry)
    {
        if (!string.IsNullOrEmpty(id) && entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].id == id)
                {
                    entry = entries[i];
                    return true;
                }
            }
        }
        entry = default;
        return false;
    }

    public bool TryGetByIndex(int index, out Entry entry)
    {
        if (entries != null && index >= 0 && index < entries.Length)
        {
            entry = entries[index];
            return true;
        }
        entry = default;
        return false;
    }

    public int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id) || entries == null) return -1;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].id == id) return i;
        return -1;
    }
}
