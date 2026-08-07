using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// Throwaway on-device probe for the 4 GO/NO-GO checks in the 2026-08-07
/// text-selection spec. Self-guiding: the on-screen banner walks the owner
/// through 3 actions in Russian (tap the field, type a letter, type another
/// letter); checks A–D run and grade themselves. Delete together with the
/// spike scene once the verdict lands.
public class TextSelectionSpikeProbe : MonoBehaviour
{
    public TMP_InputField plainField;   // "alpha beta gamma"
    public TMP_InputField emojiField;   // "hi 😂👍 end" (emoji may render as boxes — irrelevant)

    enum Step { WaitFocus, ArmB, WaitB, ArmC, WaitC, Done }

    static readonly FieldInfo KbField = typeof(TMP_InputField).GetField(
        "m_SoftKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);

    const string BaseText = "alpha beta gamma";
    const string PasteText = "alpha ZZ gamma";   // caret parked at index 8, right after ZZ

    Step _step = Step.WaitFocus;
    string _resultA = "", _resultB = "", _resultC = "", _resultD = "";
    float _focusedSince = -1f;

    GUIStyle _banner, _line, _button;

    TouchScreenKeyboard Kb => KbField?.GetValue(plainField) as TouchScreenKeyboard;

    void Update()
    {
        if (plainField == null || emojiField == null) return;

        switch (_step)
        {
            case Step.WaitFocus:
                if (!plainField.isFocused) { _focusedSince = -1f; break; }
                if (_focusedSince < 0) _focusedSince = Time.unscaledTime;

                if (Kb != null)
                {
                    RunCheckA();
                    _step = Step.ArmB;
                }
                else if (Application.isEditor && Time.unscaledTime - _focusedSince > 1f)
                {
                    _resultA = "A: —— в редакторе нет TouchScreenKeyboard; проверка только на телефоне";
                    _step = Step.ArmB;
                }
                else if (Time.unscaledTime - _focusedSince > 5f)
                {
                    _resultA = "A: FAIL — клавиатура открыта, но TouchScreenKeyboard не появился";
                    Debug.Log("[spike] " + _resultA);
                    _step = Step.ArmB;
                }
                break;

            case Step.ArmB:
                if (!plainField.isFocused) { _step = Step.WaitFocus; break; }
                plainField.text = BaseText;
                int wordStart = BaseText.IndexOf("beta");
                plainField.selectionStringAnchorPosition = wordStart;
                plainField.selectionStringFocusPosition = wordStart + 4;
                KeyboardSelectionSync.Push(plainField);
                _step = Step.WaitB;
                break;

            case Step.WaitB:
            {
                if (!plainField.isFocused) { _step = Step.WaitFocus; break; }
                string t = plainField.text;
                if (t == BaseText) break;                       // nothing typed yet
                if (!t.Contains("beta"))
                {
                    _resultB = "B: PASS — набранная буква ЗАМЕНИЛА выделенное слово";
                    _step = Step.ArmC;
                }
                else
                {
                    _resultB = $"B: FAIL — выделение не заменилось, текст стал: «{t}»";
                    _step = Step.ArmC;
                }
                Debug.Log("[spike] " + _resultB);
                break;
            }

            case Step.ArmC:
                if (!plainField.isFocused) { _step = Step.WaitFocus; break; }
                plainField.text = PasteText;
                plainField.stringPosition = 8;                  // caret right after "ZZ"
                KeyboardSelectionSync.Push(plainField);
                _step = Step.WaitC;
                break;

            case Step.WaitC:
            {
                if (!plainField.isFocused) { _step = Step.WaitFocus; break; }
                string t = plainField.text;
                if (t == PasteText) break;                      // nothing typed yet
                if (t.Length > PasteText.Length && t[8] != ' ')
                    _resultC = "C: PASS — буква встала сразу после «ZZ», как и курсор";
                else
                    _resultC = $"C: FAIL — буква встала не туда, текст стал: «{t}»";
                Debug.Log("[spike] " + _resultC);
                RunCheckDAndFinish();
                break;
            }
        }
    }

    void RunCheckA()
    {
        var kb = Kb;
        _resultA = $"A: {(kb.canSetSelection ? "PASS" : "FAIL")} — canSetSelection={kb.canSetSelection}, canGetSelection={kb.canGetSelection}";
        Debug.Log("[spike] " + _resultA);
    }

    void RunCheckDAndFinish()
    {
        string t = emojiField.text;
        int end = t.IndexOf(" end");
        string copied = end > 3 ? t.Substring(3, end - 3) : "";
        _resultD = copied == "\U0001F602\U0001F44D"
            ? "D: PASS — эмодзи-индексы честные (len=4)"
            : $"D: FAIL — вырезалось «{copied}» len={copied.Length}";
        Debug.Log("[spike] " + _resultD);
        _step = Step.Done;
    }

    string CurrentInstruction()
    {
        switch (_step)
        {
            case Step.WaitFocus:
                return "ШАГ 1 из 3\nНажмите пальцем на верхнее поле с текстом «alpha beta gamma», чтобы открылась клавиатура.";
            case Step.ArmB:
            case Step.WaitB:
                return "ШАГ 2 из 3\nСлово «beta» сейчас выделено. Нажмите ОДНУ любую букву на клавиатуре (не Enter).";
            case Step.ArmC:
            case Step.WaitC:
                return "ШАГ 3 из 3\nТеперь курсор стоит после «ZZ». Нажмите ещё ОДНУ любую букву.";
            default:
                return "ГОТОВО ✓  Сделайте скриншот этого экрана и отправьте в чат.\n\nБонус: зажмите пробел и поводите пальцем — если «курсор:» ниже меняется, напишите «бонус работает».";
        }
    }

    void OnGUI()
    {
        float scale = Screen.width / 400f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        EnsureStyles();

        GUILayout.BeginArea(new Rect(8, 72, 384, 240), GUI.skin.box);
        if (plainField == null || emojiField == null)
        {
            GUILayout.Label("ОШИБКА: поля не подключены — пересоберите сцену.", _banner);
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label(CurrentInstruction(), _banner);
        GUILayout.Space(4);
        if (_resultA != "") GUILayout.Label(_resultA, _line);
        if (_resultB != "") GUILayout.Label(_resultB, _line);
        if (_resultC != "") GUILayout.Label(_resultC, _line);
        if (_resultD != "") GUILayout.Label(_resultD, _line);
        if (_step == Step.Done && plainField.isFocused)
            GUILayout.Label($"курсор: {plainField.stringPosition}", _line);

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Начать заново", _button, GUILayout.Height(30)))
            Restart();
        GUILayout.EndArea();
    }

    void Restart()
    {
        _step = Step.WaitFocus;
        _resultA = _resultB = _resultC = _resultD = "";
        _focusedSince = -1f;
        plainField.text = BaseText;
    }

    void EnsureStyles()
    {
        if (_banner != null) return;
        _banner = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, fontStyle = FontStyle.Bold };
        _line = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        _button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
    }
}
