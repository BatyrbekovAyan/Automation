using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Builds the throwaway spike scene for the 2026-08-07 text-selection spec.
/// Edit-Mode only. Saves Assets/Scenes/SpikeTextSelection.unity.
/// Builds ADDITIVELY so the currently open scene (Main.unity, possibly with
/// unsaved owner changes) is never closed or dirtied — the spike scene is
/// created, populated, saved, and closed again in one pass.
public static class TextSelectionSpikeBuilder
{
    [MenuItem("Tools/Text Selection/Build Spike Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        // InputSystemUIInputModule, NOT StandaloneInputModule: the project runs
        // the new Input System only (activeInputHandler: 1) — the legacy module
        // would leave the spike scene without any working input on device.
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(es, scene);

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(canvasGo, scene);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        var plain = BuildField(canvasGo.transform, "PlainField", new Vector2(0, 500), "alpha beta gamma");
        var emoji = BuildField(canvasGo.transform, "EmojiField", new Vector2(0, 280), "hi \U0001F602\U0001F44D end");

        var probeGo = new GameObject("SpikeProbe", typeof(TextSelectionSpikeProbe));
        SceneManager.MoveGameObjectToScene(probeGo, scene);
        var probe = probeGo.GetComponent<TextSelectionSpikeProbe>();
        probe.plainField = plain;
        probe.emojiField = emoji;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/SpikeTextSelection.unity");
        EditorSceneManager.CloseScene(scene, true);
        Debug.Log("[TextSelectionSpikeBuilder] Spike scene saved and closed (Main scene untouched).");
    }

    static TMP_InputField BuildField(Transform parent, string name, Vector2 pos, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(960, 140);
        rt.anchoredPosition = pos;
        go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 1f);

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(24, 12); art.offsetMax = new Vector2(-24, -12);

        var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(area.transform, false);
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 44;
        tmp.color = Color.white;

        var field = go.GetComponent<TMP_InputField>();
        field.textViewport = art;
        field.textComponent = tmp;
        field.lineType = TMP_InputField.LineType.MultiLineSubmit;
        field.shouldHideMobileInput = true;   // matches every field in the app
        field.text = text;
        return field;
    }
}
