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
    public void Veto_AnnouncesFromTheClick_NotFromThePress()
    {
        // Second half of the same device report. Swallowing must happen on the PRESS (TMP must not
        // select), but the ANNOUNCE — which is what raises the panel — has to wait for the click,
        // because the raise is destructive to its own gesture: it flips the veto predicate false for
        // any second dispatch of the same press (the composer sits under BOTH ClickPassthrough and
        // DragShield in one ~24u band, so one physical tap really does arrive twice), and it slides
        // the composer out from under the finger so the release is re-raycast elsewhere.
        // Structural pin: VetoActivation must not raise the event; only the click path may.
        string veto = ReadMethodBody("VetoActivation");
        Assert.IsFalse(veto.Contains("ActivationVetoed"),
            "VetoActivation must only MARK the gesture — announcing there raises the panel on the " +
            "press, which invalidates the veto's own condition for the second dispatch of that press.");

        string click = ReadMethodBody("OnPointerClick");
        Assert.IsTrue(click.Contains("AnnounceVeto"),
            "OnPointerClick must be the place a swallowed gesture is announced.");

        string down = ReadMethodBody("OnPointerDown");
        Assert.IsFalse(down.Contains("AnnounceVeto"),
            "OnPointerDown must never announce — see above.");
        Assert.IsTrue(down.Contains("vetoedPointerId) return"),
            "OnPointerDown must treat a repeat press carrying the marked id as the SAME gesture and " +
            "swallow it without re-deciding, or the second dispatch re-asks a question the first " +
            "press already changed the answer to.");
    }

    // Source-level pin: the behaviour is device-only (TMP pointer dispatch + a real keyboard), so
    // the structure that produces it is what can be checked here. Crude but stable — these three
    // method names are load-bearing and pinned by the test above.
    private static string ReadMethodBody(string methodName)
    {
        string[] lines = System.IO.File.ReadAllLines(
            "Assets/Scripts/Chat/DeferredDismissInputField.cs");
        var body = new System.Text.StringBuilder();
        int depth = 0;
        bool inside = false;
        foreach (string line in lines)
        {
            if (!inside && line.Contains(" " + methodName + "(")) inside = true;
            if (!inside) continue;
            string code = line.Split(new[] { "//" }, System.StringSplitOptions.None)[0];
            body.Append(code).Append('\n');
            depth += CountOf(code, '{') - CountOf(code, '}');
            if (depth == 0 && body.Length > 0 && code.Contains("}")) break;
        }
        Assert.IsNotEmpty(body.ToString(), $"{methodName} not found — the pin needs re-pointing.");
        return body.ToString();
    }

    private static int CountOf(string s, char c)
    {
        int n = 0;
        foreach (char ch in s) if (ch == c) n++;
        return n;
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
