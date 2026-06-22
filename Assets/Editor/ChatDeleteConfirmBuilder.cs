using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class ChatDeleteConfirmBuilder
{
    [MenuItem("Tools/Chat/Build Delete-Confirm Popup")]
    public static void Build()
    {
        var parent = Selection.activeGameObject;
        if (parent == null)
        {
            Debug.LogError("[ChatDeleteConfirmBuilder] Select the chat list screen panel first.");
            return;
        }

        var panel = NewRect("DeleteChatConfirmPanel", parent.transform);
        Stretch(panel);
        var backdrop = panel.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.5f);

        var card = NewRect("Content", panel);
        card.sizeDelta = new Vector2(820, 460);
        Center(card);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = Color.white;

        var title = NewText("Title", card, "Удалить чат?", 44, FontStyles.Bold, TextAlignmentOptions.Center);
        Anchor((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(760, 70), new Vector2(0, -60));

        var body = NewText("Body", card, "", 32, FontStyles.Normal, TextAlignmentOptions.Center);
        body.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        Anchor((RectTransform)body.transform, new Vector2(0.5f, 1f), new Vector2(760, 140), new Vector2(0, -150));

        var cancel = NewButton("CancelButton", card, "Отмена", new Color(0.93f, 0.93f, 0.93f), Color.black);
        Anchor((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(360, 110), new Vector2(-195, 70));

        var del = NewButton("DeleteButton", card, "Удалить", new Color(0.89f, 0.29f, 0.29f), Color.white);
        Anchor((RectTransform)del.transform, new Vector2(0.5f, 0f), new Vector2(360, 110), new Vector2(195, 70));

        var controller = panel.gameObject.AddComponent<ChatDeleteConfirm>();
        var so = new SerializedObject(controller);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("bodyText").objectReferenceValue = body;
        so.FindProperty("cancelButton").objectReferenceValue = cancel;
        so.FindProperty("deleteButton").objectReferenceValue = del;
        so.ApplyModifiedProperties();

        panel.gameObject.SetActive(false);
        Selection.activeGameObject = panel.gameObject;
        Debug.Log("[ChatDeleteConfirmBuilder] Built DeleteChatConfirmPanel. Now wire ChatListView.deleteConfirm to its ChatDeleteConfirm.");
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size,
                                           FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style; t.alignment = align;
        t.color = Color.black; t.enableWordWrapping = true;
        return t;
    }

    private static Button NewButton(string name, Transform parent, string label, Color bg, Color fg)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var t = NewText("Label", rt, label, 32, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch((RectTransform)t.transform);
        t.color = fg;
        return btn;
    }
}
