// ============================================================
//  BottomTabManager.cs
//  Manages a WhatsApp-style bottom navigation bar with 5 tabs.
//
//  Dependencies : Unity UI, TextMeshPro
//  Unity Version: 6.x  |  C# 9+
//  Author       : Your Name
// ============================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Serialisable data container for a single navigation tab.
/// Populate every field via the Unity Inspector.
/// </summary>
[Serializable]
public class TabData
{
    [Header("Identity")]
    [Tooltip("Human-readable name shown in the Inspector list.")]
    public string tabName = "Tab";

    [Header("UI References")]
    [Tooltip("Root GameObject of this tab (carries the Button component).")]
    public GameObject tabRoot;

    [Tooltip("The Image component used to display the tab icon.")]
    public Image iconImage;

    [Tooltip("The TextMeshProUGUI component used to display the tab label.")]
    public TextMeshProUGUI labelText;

    [Tooltip("The full-screen panel / page that becomes visible when this tab is active.")]
    public GameObject screenPanel;

    [Header("Visuals")]
    [Tooltip("Icon sprite shown when this tab is INACTIVE (outline / muted variant).")]
    public Sprite inactiveIcon;

    [Tooltip("Icon sprite shown when this tab is ACTIVE (filled / coloured variant).")]
    public Sprite activeIcon;
}

/// <summary>
/// Central controller for the bottom navigation bar.
///
/// Setup steps:
///   1. Attach this script to the BottomNavPanel GameObject.
///   2. Populate the <see cref="tabs"/> list in the Inspector
///      (one entry per tab, in left-to-right display order).
///   3. Assign <see cref="activeColor"/> and <see cref="inactiveColor"/>.
///   4. Set <see cref="defaultTabIndex"/> to the tab that should
///      be selected when the scene first loads.
/// </summary>
public class BottomTabManager : MonoBehaviour
{
    // ------------------------------------------------------------------ //
    //  Inspector-exposed fields                                            //
    // ------------------------------------------------------------------ //

    [Header("Tab Configuration")]
    [Tooltip("Add one TabData entry per tab, ordered left to right.")]
    [SerializeField] private List<TabData> tabs = new();

    [Header("Colour Scheme")]
    [Tooltip("Colour applied to the icon and label of the ACTIVE tab.")]
    [SerializeField] private Color activeColor = new Color(0.07f, 0.53f, 0.45f); // WhatsApp teal

    [Tooltip("Colour applied to the icon and label of all INACTIVE tabs.")]
    [SerializeField] private Color inactiveColor = new Color(0.55f, 0.55f, 0.55f); // Muted gray

    [Header("Startup")]
    [Tooltip("Zero-based index of the tab selected when the scene loads.")]
    [SerializeField] private int defaultTabIndex = 3; // 'Chats' matches WhatsApp default

    // ------------------------------------------------------------------ //
    //  Private state                                                       //
    // ------------------------------------------------------------------ //

    /// <summary>Index of the currently active tab (-1 = none selected yet).</summary>
    private int _activeTabIndex = -1;

    // ------------------------------------------------------------------ //
    //  Unity lifecycle                                                     //
    // ------------------------------------------------------------------ //

    private void Awake()
    {
        // Validate configuration before wiring up buttons
        if (!ValidateTabData())
        {
            Debug.LogError("[BottomTabManager] One or more TabData entries are incomplete. " +
                           "Please check the Inspector and assign all required references.");
            return;
        }

        RegisterButtonCallbacks();
    }

    private void Start()
    {
        // Select the default tab once all other Start() calls have run
        SwitchTab(defaultTabIndex);
    }

    // ------------------------------------------------------------------ //
    //  Public API                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Programmatically switches to the tab at <paramref name="index"/>.
    /// Safe to call from any other script (e.g., a back-button handler).
    /// </summary>
    /// <param name="index">Zero-based tab index.</param>
    public void SwitchTab(int index)
    {
        if (!IsValidIndex(index))
        {
            Debug.LogWarning($"[BottomTabManager] SwitchTab({index}) is out of range. " +
                             $"Valid range: 0 – {tabs.Count - 1}.");
            return;
        }

        // Skip if this tab is already active (avoids redundant UI work)
        if (index == _activeTabIndex) return;

        _activeTabIndex = index;

        for (int i = 0; i < tabs.Count; i++)
        {
            bool isActive = (i == index);
            ApplyTabState(tabs[i], isActive);
        }
    }

    /// <summary>
    /// Returns the zero-based index of the currently active tab,
    /// or -1 if no tab has been selected yet.
    /// </summary>
    public int ActiveTabIndex => _activeTabIndex;

    /// <summary>
    /// Returns the <see cref="TabData"/> for the currently active tab,
    /// or <c>null</c> if no tab is active yet.
    /// </summary>
    public TabData ActiveTab => IsValidIndex(_activeTabIndex) ? tabs[_activeTabIndex] : null;

    // ------------------------------------------------------------------ //
    //  Private helpers                                                     //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Registers an <c>onClick</c> listener on every tab's Button component.
    /// Uses a local copy of <c>i</c> to avoid the classic C# closure/loop bug.
    /// </summary>
    private void RegisterButtonCallbacks()
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            // Capture loop variable in a local so the lambda holds the correct value
            int capturedIndex = i;

            Button button = tabs[i].tabRoot.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogError($"[BottomTabManager] Tab '{tabs[i].tabName}' root GameObject " +
                               "is missing a Button component.");
                continue;
            }

            button.onClick.AddListener(() => SwitchTab(capturedIndex));
        }
    }

    /// <summary>
    /// Applies either the active or inactive visual state to a single tab.
    /// </summary>
    /// <param name="tab">The tab to update.</param>
    /// <param name="isActive">Whether to apply the active state.</param>
    private void ApplyTabState(TabData tab, bool isActive)
    {
        Color targetColor = isActive ? activeColor : inactiveColor;

        // --- Icon ---
        if (tab.iconImage != null)
        {
            // tab.iconImage.color = targetColor;

            // Swap sprite only when distinct sprites have been provided
            if (isActive && tab.activeIcon != null)
                tab.iconImage.sprite = tab.activeIcon;
            else if (!isActive && tab.inactiveIcon != null)
                tab.iconImage.sprite = tab.inactiveIcon;
        }

        // --- Label ---
        if (tab.labelText != null)
            tab.labelText.color = targetColor;

        // --- Screen panel ---
        if (tab.screenPanel != null)
            tab.screenPanel.SetActive(isActive);
    }

    /// <summary>
    /// Checks that every <see cref="TabData"/> entry has the minimum required
    /// references assigned. Logs a descriptive warning for each gap found.
    /// </summary>
    /// <returns><c>true</c> if all entries pass validation.</returns>
    private bool ValidateTabData()
    {
        bool isValid = true;

        foreach (TabData tab in tabs)
        {
            if (tab.tabRoot == null)
            {
                Debug.LogWarning($"[BottomTabManager] '{tab.tabName}' → tabRoot is not assigned.");
                isValid = false;
            }

            if (tab.iconImage == null)
            {
                Debug.LogWarning($"[BottomTabManager] '{tab.tabName}' → iconImage is not assigned.");
                isValid = false;
            }

            if (tab.labelText == null)
            {
                Debug.LogWarning($"[BottomTabManager] '{tab.tabName}' → labelText is not assigned.");
                isValid = false;
            }

            // screenPanel is optional: a tab might not control a dedicated screen
        }

        return isValid;
    }

    /// <summary>Returns <c>true</c> when <paramref name="index"/> is within bounds.</summary>
    private bool IsValidIndex(int index) => index >= 0 && index < tabs.Count;
}