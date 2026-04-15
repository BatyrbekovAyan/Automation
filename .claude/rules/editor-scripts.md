---
paths:
  - "Assets/Scripts/Editor/**/*.cs"
  - "Assets/Editor/**/*.cs"
---

# Editor Script Rules

- Always wrap in #if UNITY_EDITOR / #endif if referenced from runtime code
- Use [MenuItem] for custom menu entries
- Use EditorGUILayout for inspector UI, not runtime UI components
- Never reference UnityEditor namespace from runtime scripts
- Use SerializedObject/SerializedProperty for undo-safe property editing
