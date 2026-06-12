#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AssignChatBackground
{
    const string SpritePath = "Assets/Images/Chat/ChatDoodleBackground.png";
    const string ObjectPath = "Canvas/ScreenContainer/Screen_Whatsapp/MessagesPanel/MovingArea/Background/Image";

    [MenuItem("Tools/Claude/Assign Chat Doodle Background")]
    public static void Assign()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null)
        {
            Debug.LogError($"AssignChatBackground: sprite not found at {SpritePath}");
            return;
        }

        GameObject go = GameObject.Find(ObjectPath);
        if (go == null)
        {
            Debug.LogError($"AssignChatBackground: GameObject not found at {ObjectPath}");
            return;
        }

        Image image = go.GetComponent<Image>();
        if (image == null)
        {
            Debug.LogError("AssignChatBackground: no Image component on target");
            return;
        }

        SerializedObject so = new SerializedObject(image);
        so.FindProperty("m_Sprite").objectReferenceValue = sprite;
        so.FindProperty("m_Color").colorValue = Color.white;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(image);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log($"AssignChatBackground: assigned {sprite.name} ({sprite.rect.width}x{sprite.rect.height}) color={image.color} to {ObjectPath}");
    }
}
#endif
