// IMPORTANT: These values are persisted to disk by Unity's JsonUtility
// (via ChatHistoryCache) as integer ordinals. Never reorder, rename, or
// insert members before existing ones — doing so will silently corrupt
// cached message history on device. Append only.
public enum DeliveryStatus
{
    None,
    Pending,
    Sent,
    Delivered,
    Read,
    Failed
}
