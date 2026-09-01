using UnityEngine;

/// <summary>
/// Hides the bottom nav bar (BottomNavPanel) for as long as ANY GameObject carrying
/// this component is active. The bar draws ABOVE ScreenContainer, so a full-screen
/// overlay (the Screen_New add-bot wizard, the auth pages stacked over it) otherwise
/// shows a bar whose taps switch screens BENEATH the overlay — dead-looking buttons
/// (owner check 2026-09-01; same failure the paywall fixed in PaywallController).
///
/// Ref-counted, because the overlays NEST: the wizard stays active while an auth page
/// opens above it, so a plain per-object hide/show would flash the bar back mid-flow.
/// The bar's prior active state is captured on the 0→1 transition and restored on
/// 1→0 — another surface's own hide is never overwritten.
///
/// Attached by NavBarHiderWirer (Tools/Nav Restructure/Wire Nav Bar Hiders) to
/// Screen_New, WhatsappAuth and TelegramAuth. Profile sub-pages deliberately KEEP the
/// bar: there a tab tap honestly leaves the page (the sub-page lives inside the tab
/// screen), the standard nested-page pattern.
/// </summary>
[DisallowMultipleComponent]
public class NavBarHider : MonoBehaviour
{
    private static int _activeHiders;
    private static bool _barWasActive;

    private void OnEnable()
    {
        if (_activeHiders++ != 0) return;

        var bar = BottomTabManager.Instance;
        _barWasActive = bar != null && bar.gameObject.activeSelf;
        if (_barWasActive) bar.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (--_activeHiders > 0) return;
        _activeHiders = 0;   // defensive clamp against unbalanced disables on teardown

        if (_barWasActive && BottomTabManager.Instance != null)
            BottomTabManager.Instance.gameObject.SetActive(true);
        _barWasActive = false;
    }
}
