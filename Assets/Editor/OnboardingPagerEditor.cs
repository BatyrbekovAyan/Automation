using UnityEditor;
using UnityEditor.UI;

/// <summary>
/// IN-07: makes <see cref="OnboardingPager"/>'s own serialized fields visible in the Inspector.
///
/// <c>ScrollRect</c> ships a custom editor (<see cref="ScrollRectEditor"/>) that draws ONLY
/// ScrollRect's own properties, so a subclass field like <c>pageCount</c> silently never appears —
/// it worked purely because <c>OnboardingScreenBuilder</c> stamps it through <c>SerializedObject</c>,
/// leaving no hand-editable path if the slide count ever changes.
///
/// Draws the base ScrollRect inspector, then any properties this subclass adds.
/// </summary>
[CustomEditor(typeof(OnboardingPager))]
[CanEditMultipleObjects]
public class OnboardingPagerEditor : ScrollRectEditor
{
    private SerializedProperty _pageCount;

    protected override void OnEnable()
    {
        base.OnEnable();
        _pageCount = serializedObject.FindProperty("pageCount");
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();          // the stock ScrollRect inspector

        if (_pageCount == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Onboarding Pager", EditorStyles.boldLabel);

        serializedObject.Update();
        EditorGUILayout.PropertyField(_pageCount);
        serializedObject.ApplyModifiedProperties();
    }
}
