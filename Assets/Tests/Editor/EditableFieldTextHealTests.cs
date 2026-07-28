using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Automation.BotSettingsUI;

/// <summary>
/// Pins the blurred-field self-heal: a field that is not focused has no
/// legitimate way to change its text (user input reaches only the focused
/// field; app writes go through Value), so a direct text change — the way
/// Android keyboard-session bleed arrives during rapid field switches —
/// must be reverted on the next Update.
/// </summary>
public class EditableFieldTextHealTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
    }

    [Test]
    public void BlurredField_ForeignTextChange_IsHealed()
    {
        var field = BuildField(initialText: "+7 707 123 45 67");

        // Simulate the IME bleed: the native session deposits another
        // field's buffer straight into the TMP text, bypassing Value.
        field.InputField.text = "info@company.kz";

        InvokePrivate(field, "Update");
        Assert.AreEqual("+7 707 123 45 67", field.InputField.text,
            "blurred field kept foreign text — keyboard bleed not healed");
    }

    [Test]
    public void BlurredField_EmptyBleed_IsHealed()
    {
        // The "text erases itself" flavor: an empty buffer lands in a
        // blurred field that had content.
        var field = BuildField(initialText: "г. Алматы, ул. Толе би 285");
        field.InputField.text = "";

        InvokePrivate(field, "Update");
        Assert.AreEqual("г. Алматы, ул. Толе би 285", field.InputField.text);
    }

    [Test]
    public void ValueWrites_AreLegitimate_NotHealed()
    {
        var field = BuildField(initialText: "old");

        field.Value = "new";
        InvokePrivate(field, "Update");
        Assert.AreEqual("new", field.InputField.text,
            "a Value write is a legitimate external change and must stick");
    }

    [Test]
    public void UnwiredExpectation_DoesNotHealToEmpty()
    {
        // Without Awake (expectation never initialized) the heal must stay
        // inert rather than blanking whatever text is present.
        root = new GameObject("Field", typeof(RectTransform));
        var input = root.AddComponent<TMP_InputField>();
        var field = root.AddComponent<EditableField>();
        SetPrivate(field, "input", input);

        input.text = "untouched";
        InvokePrivate(field, "Update");
        Assert.AreEqual("untouched", input.text);
    }

    private EditableField BuildField(string initialText)
    {
        root = new GameObject("Field", typeof(RectTransform));
        var input = root.AddComponent<TMP_InputField>();
        var field = root.AddComponent<EditableField>();
        SetPrivate(field, "input", input);
        input.text = initialText;
        InvokePrivate(field, "Awake"); // wires listeners + captures expectation
        return field;
    }

    private static void SetPrivate(object target, string name, object value)
    {
        var type = target.GetType();
        FieldInfo info = null;
        while (type != null && info == null)
        {
            info = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }
        info.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name)
    {
        var type = target.GetType();
        MethodInfo info = null;
        while (type != null && info == null)
        {
            info = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }
        info.Invoke(target, null);
    }
}
