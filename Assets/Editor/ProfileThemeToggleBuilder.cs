using System.Linq;
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the «Тёмная тема» switch to the Profile tab's main list.
///
/// It CLONES an existing, hand-tuned ToggleRow out of PanelNotifications rather
/// than constructing one: that row already carries the correct track/knob
/// geometry, sprites and spacing, all of which were adjusted by hand after the
/// original builder ran. Re-running ProfileSubPagesBuilder to add a row would
/// destroy-and-rebuild the panels and lose exactly that work.
///
/// This is the first wirer that ADDS GameObjects rather than only components —
/// unavoidable, since a new row is new objects. Everything already in the scene
/// is left untouched; the clone is appended to Section1 after a cloned Divider.
///
/// Idempotent: re-running finds the existing row by name and only re-wires it.
/// </summary>
public static class ProfileThemeToggleBuilder
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string RowName = "ТёмнаяТемаRow";
    private const string SectionPath = "ScrollView/Viewport/Content/Section1";

    [MenuItem("Tools/Theme/Profile/Add Dark Theme Toggle")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject profile = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            profile = FindDeep(root.transform, "Screen_Profile");
            if (profile != null) break;
        }
        if (profile == null) { Debug.LogError("[ProfileThemeToggle] Screen_Profile not found"); return; }

        var section = profile.transform.Find(SectionPath);
        if (section == null) { Debug.LogError($"[ProfileThemeToggle] {SectionPath} not found"); return; }

        var subPages = profile.GetComponentInChildren<ProfileSubPages>(true);
        if (subPages == null) { Debug.LogError("[ProfileThemeToggle] ProfileSubPages not found"); return; }

        // Reuse a real tuned row as the template.
        var template = profile.GetComponentsInChildren<ToggleRow>(true).FirstOrDefault();
        if (template == null) { Debug.LogError("[ProfileThemeToggle] no ToggleRow to clone"); return; }

        var existing = section.Find(RowName);
        ToggleRow row;
        if (existing != null)
        {
            row = existing.GetComponent<ToggleRow>();
            Debug.Log("[ProfileThemeToggle] Row already present — re-wiring only.");
        }
        else
        {
            // A divider first, cloned from one already in this section so it
            // inherits the section's own tuned inset and colour.
            var dividerTemplate = section.Cast<Transform>().FirstOrDefault(t => t.name == "Divider");
            if (dividerTemplate != null)
            {
                var div = Object.Instantiate(dividerTemplate.gameObject, section);
                div.name = "Divider";
                div.transform.SetAsLastSibling();
            }

            var clone = Object.Instantiate(template.gameObject, section);
            clone.name = RowName;
            clone.transform.SetAsLastSibling();
            row = clone.GetComponent<ToggleRow>();

            var label = clone.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (label != null) label.text = "Тёмная тема";
        }

        var so = new SerializedObject(subPages);
        var prop = so.FindProperty("darkThemeToggle");
        if (prop == null) { Debug.LogError("[ProfileThemeToggle] darkThemeToggle field missing — compile first"); return; }
        prop.objectReferenceValue = row;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ProfileThemeToggle] '{RowName}' present in Section1 and wired to " +
                  "ProfileSubPages.darkThemeToggle. Scene saved.");
    }

    private static GameObject FindDeep(Transform t, string name)
    {
        if (t.name == name) return t.gameObject;
        foreach (Transform c in t)
        {
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
