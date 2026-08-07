using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// Throwaway on-device probe for the 4 GO/NO-GO checks in the 2026-08-07
/// text-selection spec. Drive it with the OnGUI buttons; results accumulate
/// on screen. Delete together with the spike scene once the verdict lands.
public class TextSelectionSpikeProbe : MonoBehaviour
{
    public TMP_InputField plainField;   // "alpha beta gamma"
    public TMP_InputField emojiField;   // "hi 😂👍 end"

    static readonly FieldInfo KbField = typeof(TMP_InputField).GetField(
        "m_SoftKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly List<string> _log = new List<string>();
    string _expectAfterTyping;
    TMP_InputField _watched;

    TouchScreenKeyboard Kb(TMP_InputField f) => KbField?.GetValue(f) as TouchScreenKeyboard;

    void Log(string s) { _log.Add(s); Debug.Log("[spike] " + s); }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(Vector3.one * (Screen.width / 400f));
        GUILayout.BeginArea(new Rect(5, 120, 390, 560));

        if (GUILayout.Button("A: keyboard + canSetSelection", GUILayout.Height(34)))
        {
            var kb = Kb(plainField);
            Log(kb == null
                ? "A: FAIL — no TouchScreenKeyboard (focus the top field first)"
                : $"A: {(kb.canSetSelection ? "PASS" : "FAIL")} — canSetSelection={kb.canSetSelection}, canGetSelection={kb.canGetSelection}");
        }

        if (GUILayout.Button("B: select 'beta' + sync (then type X)", GUILayout.Height(34)))
        {
            plainField.text = "alpha beta gamma";
            int s = plainField.text.IndexOf("beta");
            plainField.selectionStringAnchorPosition = s;
            plainField.selectionStringFocusPosition = s + 4;
            KeyboardSelectionSync.Push(plainField);
            _watched = plainField;
            _expectAfterTyping = "alpha X gamma";
            Log("B: armed — now type a capital X on the keyboard");
        }

        if (GUILayout.Button("C: paste-sim 'ZZ' at 6 (then type Y)", GUILayout.Height(34)))
        {
            plainField.text = "alpha beta gamma";
            var edit = new SelectionEditProbe("alpha beta gamma", 6, 10, "ZZ"); // replaces "beta"
            plainField.text = edit.NewText;
            plainField.stringPosition = edit.NewCaret;
            KeyboardSelectionSync.Push(plainField);
            _watched = plainField;
            _expectAfterTyping = "alpha ZZY gamma";
            Log("C: armed — now type a capital Y");
        }

        if (GUILayout.Button("D: emoji indices", GUILayout.Height(34)))
        {
            string t = emojiField.text; // "hi 😂👍 end"
            int i = t.IndexOf(" end");
            emojiField.selectionStringAnchorPosition = 3;   // start of emoji run
            emojiField.selectionStringFocusPosition = i;    // end of emoji run
            KeyboardSelectionSync.Push(emojiField);
            string copied = t.Substring(3, i - 3);
            Log($"D: {(copied == "😂👍" ? "PASS" : "FAIL")} — substring='{copied}' len={copied.Length}");
        }

        if (GUILayout.Button("Bonus: log selection each 0.5s (spacebar-trackpad)", GUILayout.Height(34)))
            InvokeRepeating(nameof(LogSel), 0f, 0.5f);

        foreach (var line in _log) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    void LogSel()
    {
        if (plainField != null && plainField.isFocused)
            Log($"sel now: caret={plainField.stringPosition} anchor={plainField.selectionStringAnchorPosition}");
    }

    void Update()
    {
        if (_watched == null || _expectAfterTyping == null) return;
        if (_watched.text == _expectAfterTyping)
        {
            Log("PASS — typed char replaced the synced selection");
            _watched = null; _expectAfterTyping = null;
        }
        else if (_watched.text.Length > "alpha beta gamma".Length && _watched.text.Contains("beta") &&
                 (_watched.text.EndsWith("X") || _watched.text.EndsWith("Y")))
        {
            Log($"FAIL — char appended at stale caret: '{_watched.text}'");
            _watched = null; _expectAfterTyping = null;
        }
    }

    /// Local copy of the Paste math so the spike does not depend on later tasks.
    readonly struct SelectionEditProbe
    {
        public readonly string NewText; public readonly int NewCaret;
        public SelectionEditProbe(string text, int start, int end, string clip)
        { NewText = text.Remove(start, end - start).Insert(start, clip); NewCaret = start + clip.Length; }
    }
}
