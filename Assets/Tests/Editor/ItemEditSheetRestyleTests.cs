using System.Linq;
using Automation.BotSettingsUI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pins the item-edit sheet restyle (sketch 007 «B2») produced by
/// Tools/BotSettings/Restyle Item Edit Sheet, and — more importantly — pins the
/// behaviour the restyle must NOT have cost: the twelve serialized references
/// ItemEditSheet wires in Awake, the bottom-anchored/zero-pivot SheetRoot its
/// hide-position snapshot depends on, the delete button's invisible-but-
/// raycastable target graphic, and the price field's text staying free of the
/// ₸ glyph (card.Price reaches PlayerPrefs and the n8n payload verbatim).
///
/// Both sheets are asserted, always: the two branches are separate object trees
/// and a builder that resolves only one of them would leave the app half
/// restyled with nothing else complaining.
/// </summary>
public class ItemEditSheetRestyleTests
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const float Tolerance = 0.5f;

    private const float ExpectedSheetHeight = 1149f;
    private const float ExpectedDragZoneHeight = 156f;
    private const float ExpectedRingWidth = 3f;
    private const string SuffixGlyph = "₸";

    private static ItemEditSheet[] LoadSheets()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");

        var sheets = prefab.GetComponentsInChildren<ItemEditSheet>(true);
        Assert.AreEqual(2, sheets.Length,
            "Expected exactly two item-edit sheets (product + service).");
        return sheets;
    }

    private static T Ref<T>(Object owner, string property) where T : Object =>
        new SerializedObject(owner).FindProperty(property)?.objectReferenceValue as T;

    // ------------------------------------------------------------------
    // Wiring that the restyle must not have disturbed
    // ------------------------------------------------------------------

    [Test]
    public void BothSheets_KeepEverySerializedReference()
    {
        string[] properties =
        {
            "sheetRoot", "nameField", "priceField", "descField",
            "doneButton", "deleteButton",
            "deleteConfirmPopup", "deleteConfirmYes", "deleteConfirmNo",
            "scrimBehind", "scrimBehindGroup", "scrimBehindFinger",
        };

        foreach (var sheet in LoadSheets())
        {
            var so = new SerializedObject(sheet);
            foreach (var property in properties)
            {
                var found = so.FindProperty(property);
                Assert.IsNotNull(found, $"ItemEditSheet no longer has a '{property}' field?");
                Assert.IsNotNull(found.objectReferenceValue,
                    $"'{sheet.name}.{property}' lost its reference — a builder re-created the object " +
                    "instead of mutating it, and the sheet will silently stop working.");
            }
        }
    }

    // Awake snapshots hiddenAnchored = -sheetRoot.rect.height exactly once, so
    // the sheet only hides off-screen while it is bottom-anchored with pivot 0.
    [Test]
    public void SheetRoot_StaysBottomAnchoredAtItsNewHeight()
    {
        foreach (var sheet in LoadSheets())
        {
            var root = Ref<RectTransform>(sheet, "sheetRoot");
            Assert.AreEqual(0f, root.pivot.y, Tolerance, $"{sheet.name}: SheetRoot pivot.y must stay 0.");
            Assert.AreEqual(0f, root.anchorMin.y, Tolerance, $"{sheet.name}: SheetRoot must stay bottom-anchored.");
            Assert.AreEqual(0f, root.anchorMax.y, Tolerance, $"{sheet.name}: SheetRoot must stay bottom-anchored.");
            Assert.AreEqual(ExpectedSheetHeight, root.sizeDelta.y, Tolerance,
                $"{sheet.name}: SheetRoot height drifted from the restyle layout.");
            Assert.IsNull(root.GetComponent<LayoutGroup>(),
                $"{sheet.name}: a layout group on SheetRoot would fight the DOTween writes to anchoredPosition.");
        }
    }

    [Test]
    public void DragZone_ClearsTheHeaderAndStaysOnTop()
    {
        foreach (var sheet in LoadSheets())
        {
            var root = Ref<RectTransform>(sheet, "sheetRoot");
            var zone = root.Find("DragZone") as RectTransform;
            Assert.IsNotNull(zone, $"{sheet.name}: DragZone missing — swipe-to-dismiss is gone.");
            Assert.AreEqual(ExpectedDragZoneHeight, zone.sizeDelta.y, Tolerance,
                $"{sheet.name}: DragZone height must match the restyled header block.");
            Assert.AreEqual(root.childCount - 1, zone.GetSiblingIndex(),
                $"{sheet.name}: DragZone must stay the last sibling or it loses the raycast.");
            var image = zone.GetComponent<Image>();
            Assert.IsNotNull(image);
            Assert.IsTrue(image.raycastTarget, $"{sheet.name}: DragZone must keep receiving pointers.");
        }
    }

    // ------------------------------------------------------------------
    // The restyle itself
    // ------------------------------------------------------------------

    [Test]
    public void EveryField_HasAWellBoundToItsOwnField()
    {
        foreach (var sheet in LoadSheets())
        {
            foreach (var field in Fields(sheet))
            {
                var well = field.transform.Find("Well") as RectTransform;
                Assert.IsNotNull(well, $"{sheet.name}/{field.name}: no Well — run Tools/BotSettings/Restyle Item Edit Sheet.");

                var border = well.GetComponent<FieldWellFocusBorder>();
                Assert.IsNotNull(border, $"{sheet.name}/{field.name}: Well has no FieldWellFocusBorder.");
                Assert.AreSame(field, Ref<EditableField>(border, "field"),
                    $"{sheet.name}/{field.name}: the well's focus border points at another field.");
                Assert.AreSame(well.GetComponent<Image>(), Ref<Graphic>(border, "ring"),
                    $"{sheet.name}/{field.name}: the focus border does not own its own ring graphic.");

                // A second colour owner would repaint a focused ring back to
                // the rest role on the next Theme.Changed.
                Assert.IsNull(well.GetComponent<ThemedColor>(),
                    $"{sheet.name}/{field.name}: the Well must not carry a ThemedColor — " +
                    "FieldWellFocusBorder owns that colour.");

                var fill = well.Find("Fill") as RectTransform;
                Assert.IsNotNull(fill, $"{sheet.name}/{field.name}: Well has no Fill.");
                Assert.AreEqual(ExpectedRingWidth, fill.offsetMin.x, Tolerance,
                    $"{sheet.name}/{field.name}: the ring width drifted.");
                Assert.AreEqual(-ExpectedRingWidth, fill.offsetMax.y, Tolerance,
                    $"{sheet.name}/{field.name}: the ring width drifted.");
            }
        }
    }

    [Test]
    public void Input_CoversItsWellAndDrawsAboveIt()
    {
        foreach (var sheet in LoadSheets())
        {
            foreach (var field in Fields(sheet))
            {
                var well = (RectTransform)field.transform.Find("Well");
                var input = Ref<TMP_InputField>(field, "input");
                Assert.IsNotNull(input, $"{sheet.name}/{field.name}: input reference lost.");
                var inputRt = (RectTransform)input.transform;

                Assert.AreEqual(well.sizeDelta.y, inputRt.sizeDelta.y, Tolerance,
                    $"{sheet.name}/{field.name}: the input must cover the whole well (tap target).");
                Assert.AreEqual(well.anchoredPosition.y, inputRt.anchoredPosition.y, Tolerance,
                    $"{sheet.name}/{field.name}: the input is offset from its well.");
                Assert.Less(well.GetSiblingIndex(), inputRt.GetSiblingIndex(),
                    $"{sheet.name}/{field.name}: the well must draw BEHIND the input, not over its text.");

                // Padding lives on the viewport, so the well stays tappable.
                var viewport = input.textViewport;
                Assert.IsNotNull(viewport, $"{sheet.name}/{field.name}: input lost its text viewport.");
                Assert.Greater(viewport.offsetMin.x, 0f,
                    $"{sheet.name}/{field.name}: text sits flush against the well's border.");
            }
        }
    }

    [Test]
    public void PriceField_ShowsTheCurrencyOutsideTheInput()
    {
        foreach (var sheet in LoadSheets())
        {
            var price = Ref<EditableField>(sheet, "priceField");
            var suffix = price.transform.Find("Suffix");
            Assert.IsNotNull(suffix, $"{sheet.name}: the price well has no ₸ suffix.");

            var tmp = suffix.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(tmp);
            Assert.AreEqual(SuffixGlyph, tmp.text);
            Assert.IsFalse(tmp.raycastTarget, $"{sheet.name}: the suffix must not steal taps from the field.");

            // The glyph must never live in the value: card.Price is written to
            // PlayerPrefs and into the n8n catalog payload verbatim.
            var input = Ref<TMP_InputField>(price, "input");
            StringAssert.DoesNotContain(SuffixGlyph, input.text ?? string.Empty);

            foreach (var other in new[] { Ref<EditableField>(sheet, "nameField"), Ref<EditableField>(sheet, "descField") })
                Assert.IsNull(other.transform.Find("Suffix"),
                    $"{sheet.name}/{other.name}: only the price field carries a currency suffix.");
        }
    }

    [Test]
    public void Buttons_AreStackedFullWidthWithATextOnlyDelete()
    {
        foreach (var sheet in LoadSheets())
        {
            var done = (RectTransform)Ref<Button>(sheet, "doneButton").transform;
            var deleteButton = Ref<Button>(sheet, "deleteButton");
            var delete = (RectTransform)deleteButton.transform;

            foreach (var rt in new[] { done, delete })
            {
                Assert.AreEqual(0f, rt.anchorMin.x, Tolerance, $"{sheet.name}/{rt.name}: must span the sheet width.");
                Assert.AreEqual(1f, rt.anchorMax.x, Tolerance, $"{sheet.name}/{rt.name}: must span the sheet width.");
            }

            Assert.Greater(done.anchoredPosition.y, delete.anchoredPosition.y,
                $"{sheet.name}: «Готово» sits above «Удалить» in the stacked layout.");

            // Invisible fill, but still the Button's targetGraphic — disabling
            // or removing the Image kills the tap silently.
            var image = deleteButton.GetComponent<Image>();
            Assert.IsNotNull(image, $"{sheet.name}: delete button lost its target graphic.");
            Assert.IsTrue(image.enabled && image.raycastTarget,
                $"{sheet.name}: the delete button's graphic must stay enabled and raycastable.");
            Assert.AreEqual(0f, image.color.a, 0.01f, $"{sheet.name}: the delete button should read as text-only.");
            Assert.AreEqual(Selectable.Transition.None, deleteButton.transition,
                $"{sheet.name}: ColorTint on a fully transparent graphic gives no feedback.");

            AssertLabelRole(deleteButton, ThemeRole.Destructive, sheet.name);
            AssertLabelRole(Ref<Button>(sheet, "doneButton"), ThemeRole.AccentOnFill, sheet.name);
        }
    }

    // The keyboard lift is DERIVED from where «Готово» sits: liftReduction is
    // how much of the keyboard's height the sheet declines to rise by, so
    // «Готово» clears the keyboard by (DoneY − liftReduction) and «Удалить»,
    // which ends exactly at liftReduction, stays fully behind it. Move the
    // buttons and this pairing has to be re-derived, or the keyboard eats
    // «Готово» again (device report 2026-08-17).
    [Test]
    public void KeyboardLift_LeavesDoneClearAndDeleteBehindTheKeyboard()
    {
        foreach (var sheet in LoadSheets())
        {
            float liftReduction = new SerializedObject(sheet).FindProperty("liftReduction").floatValue;
            var done = (RectTransform)Ref<Button>(sheet, "doneButton").transform;
            var delete = (RectTransform)Ref<Button>(sheet, "deleteButton").transform;

            float deleteTop = delete.anchoredPosition.y + delete.sizeDelta.y;
            Assert.GreaterOrEqual(liftReduction, deleteTop,
                $"{sheet.name}: «Удалить» would poke out from behind the keyboard — " +
                "a destructive action next to the keyboard's top row is an accidental-tap hazard.");

            float doneClearance = done.anchoredPosition.y - liftReduction;
            Assert.Greater(doneClearance, 0f,
                $"{sheet.name}: the keyboard covers «Готово» (clearance {doneClearance}).");
        }
    }

    [Test]
    public void Title_NamesTheItemBeingEdited()
    {
        foreach (var sheet in LoadSheets())
        {
            var root = Ref<RectTransform>(sheet, "sheetRoot");
            var title = root.Find("Title")?.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(title, $"{sheet.name}: no Title text.");

            bool isProduct = sheet.name.Contains("Product");
            Assert.AreEqual(isProduct ? "Товар" : "Услуга", title.text);
            Assert.AreEqual(TextAlignmentOptions.MidlineLeft, title.alignment,
                $"{sheet.name}: the sheet title is left-aligned in the restyle.");
        }
    }

    [Test]
    public void FieldLabels_AreUppercaseCaptionsOutsideTheWell()
    {
        foreach (var sheet in LoadSheets())
        {
            foreach (var field in Fields(sheet))
            {
                var label = Ref<TextMeshProUGUI>(field, "labelText");
                Assert.IsNotNull(label, $"{sheet.name}/{field.name}: label reference lost.");
                Assert.AreEqual(label.text.ToUpperInvariant(), label.text,
                    $"{sheet.name}/{field.name}: field labels are uppercase in the restyle.");
                Assert.IsFalse(label.raycastTarget,
                    $"{sheet.name}/{field.name}: the label must not intercept taps meant for the well.");

                var labelRt = (RectTransform)label.transform;
                var well = (RectTransform)field.transform.Find("Well");
                Assert.Greater(labelRt.anchorMin.y, well.anchorMax.y,
                    $"{sheet.name}/{field.name}: the label belongs above the well, not inside it.");

                // With the label outside the well, a placeholder repeating it
                // would render «НАЗВАНИЕ» above «Название».
                var placeholder = Ref<TMP_InputField>(field, "input").placeholder as TMP_Text;
                if (placeholder != null)
                    StringAssert.AreNotEqualIgnoringCase(label.text, placeholder.text ?? string.Empty,
                        $"{sheet.name}/{field.name}: the placeholder duplicates the field label.");
            }
        }
    }

    // ------------------------------------------------------------------

    private static EditableField[] Fields(ItemEditSheet sheet) => new[]
    {
        Ref<EditableField>(sheet, "nameField"),
        Ref<EditableField>(sheet, "priceField"),
        Ref<EditableField>(sheet, "descField"),
    }.Where(field => field != null).ToArray();

    private static void AssertLabelRole(Button button, ThemeRole expected, string sheetName)
    {
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Assert.IsNotNull(label, $"{sheetName}/{button.name}: no label.");
        var themed = label.GetComponent<ThemedColor>();
        Assert.IsNotNull(themed,
            $"{sheetName}/{button.name}: the label has no ThemedColor — it would stay white in the light theme.");
        Assert.AreEqual(expected, themed.Role, $"{sheetName}/{button.name}: wrong theme role on the label.");
    }
}
