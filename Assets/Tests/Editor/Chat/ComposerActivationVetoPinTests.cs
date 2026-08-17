using System.Reflection;
using NUnit.Framework;
using UnityEngine.EventSystems;

// Reflection pins for the composer's activation veto (model E rule 4, the two-step entry:
// from Collapsed a tap on the composer raises the panel and must NOT focus the field).
// The behaviour itself is device-only — it lives in TMP's pointer dispatch and needs a rendered
// canvas plus a real keyboard — so what is pinned here is the SHAPE the fix depends on. Both pins
// below record a bug that actually shipped and was reported from the device.
public class ComposerActivationVetoPinTests
{
    private const BindingFlags AnyDeclared =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Test]
    public void Veto_IsConsultedOnBothActivationRoutes()
    {
        // TMP reaches activation by two pointer routes and gating either alone leaves the other
        // open: OnPointerDown selects the field (→ OnSelect → ActivateInputField), and
        // OnPointerClick activates directly. Gating OnSelect instead is forbidden — it would break
        // the ⌨ key, the post-Send re-focus and the reply focus, which activate programmatically.
        Assert.IsNotNull(
            typeof(DeferredDismissInputField).GetMethod("OnPointerDown", AnyDeclared),
            "DeferredDismissInputField must override OnPointerDown — the veto has to cover TMP's " +
            "selection route, not just the click route.");
        Assert.IsNotNull(
            typeof(DeferredDismissInputField).GetMethod("OnPointerClick", AnyDeclared),
            "DeferredDismissInputField must override OnPointerClick — TMP activates directly there.");
    }

    [Test]
    public void VetoedPointer_IsNotForgottenOnRelease()
    {
        // THE bug, reported from the device 2026-08-14: one tap in the MIDDLE of the composer both
        // raised the panel and opened the keyboard, while taps on its bare edges behaved.
        // DragShield covers the middle and re-sends the tap as a synthetic down → up → click burst
        // carrying ITS OWN raycast; it also implements IPointerClickHandler on purpose. A release
        // hook that forgot the vetoed pointer unless a click could still reach THIS field therefore
        // resolved the handler walk to the shield and dropped the mark between our down and our
        // click. By the time the click arrived the veto predicate had flipped false — the
        // pointer-down veto had already raised the panel — so the click fell through to base and
        // activated the field.
        // The mark is cleared in exactly three places instead: the top of the next press,
        // the click that consumes it, and OnDisable.
        Assert.IsNull(
            typeof(DeferredDismissInputField).GetMethod("OnPointerUp", AnyDeclared),
            "DeferredDismissInputField must NOT hook OnPointerUp: a release-time 'forget the vetoed " +
            "pointer' step is defeated by DragShield's synthetic dispatch and lets one tap both " +
            "raise the panel and open the keyboard.");
    }

    [Test]
    public void Veto_IsPerInstanceAndOptIn()
    {
        // Default-null so the other ~12 scene fields and every prefab field keep stock TMP
        // behaviour; the controller installs it on the composer alone.
        FieldInfo veto = typeof(DeferredDismissInputField).GetField(
            "ActivationVeto", BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(veto, "ActivationVeto must stay a public per-instance field the controller installs.");
        Assert.IsFalse(veto.IsStatic, "a static veto would gate every input field in the project.");

        EventInfo announced = typeof(DeferredDismissInputField).GetEvent("ActivationVetoed");
        Assert.IsNotNull(announced,
            "a swallowed tap must be announced, or the veto silently eats the gesture instead of " +
            "handing it to the controller as «raise the panel».");
    }
}
