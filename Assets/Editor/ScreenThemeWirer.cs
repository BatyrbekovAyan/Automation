using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Value-driven theme binding for whole screens.
///
/// The chats-list wirers used explicit child paths, which is right for a small
/// surface. Screen_Bots and Screen_Dashboard carry 41 and 92 coloured graphics;
/// hand-writing ~130 paths would be slower AND more error-prone than the thing
/// it replaces. So this maps by CURRENT COLOUR instead: a graphic sitting at
/// #1A1A2E today IS primary ink, whatever its path, and binds to InkPrimary.
///
/// Still additive-only and non-destructive — components are added, fields are
/// written through SerializedObject, and no authored colour byte is touched.
///
/// #FFFFFF is deliberately NEVER auto-mapped: on this project white means both
/// "surface" and "no tint" on a sprite, and binding an icon's tint to a theme
/// colour would invert it in dark mode. Whites are listed in the report so they
/// can be bound by hand where they really are surfaces.
///
/// NOTE ON DELTAS: after the phase-4 palette flip, binding is no longer a no-op
/// by construction — adopting a token IS the visible «Чернильный» change. The
/// audit therefore reports every delta with its perceptual distance and Apply
/// requires the explicit accept-deltas entry point.
/// </summary>
public static class ScreenThemeWirer
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    /// <summary>Current authored colour → the role it semantically is.</summary>
    private static readonly (string hex, ThemeRole role)[] ValueMap =
    {
        // inks — the app's two conventions, both meaning the same thing
        ("#1A1A2E", ThemeRole.InkPrimary),
        ("#000000", ThemeRole.InkPrimary),
        ("#111111", ThemeRole.InkPrimary),
        ("#1C1C1E", ThemeRole.InkPrimary),
        ("#1C1C1F", ThemeRole.InkPrimary),
        ("#65676B", ThemeRole.InkSecondary),
        ("#666666", ThemeRole.InkSecondary),
        ("#6A6A6A", ThemeRole.InkSecondary),
        ("#8E8E93", ThemeRole.InkTertiary),
        ("#9A9A9A", ThemeRole.InkTertiary),
        // structure
        ("#F0F2F5", ThemeRole.Background),
        ("#F2F2F7", ThemeRole.Background),
        ("#E4E6EB", ThemeRole.Hairline),
        ("#E5E5EA", ThemeRole.Hairline),
        ("#E1E5EC", ThemeRole.Border),
        ("#C6CBD3", ThemeRole.InputBorder),
        ("#C7C7CC", ThemeRole.InputBorder),
        // accent — where «Чернильный» finally shows up
        ("#1B7CEB", ThemeRole.AccentFill),
        // dashboard statuses (DashboardStatusInfo FG values)
        ("#34C759", ThemeRole.StatusOrderCollected),
        ("#F57C00", ThemeRole.StatusOwnerNeeded),
        ("#007AFF", ThemeRole.StatusInDialog),
        // success-pill tint (CLAUDE.md soft tints)
        ("#E8F8EE", ThemeRole.PositiveBg),
        // found on the settings/list prefabs
        ("#636366", ThemeRole.InkSecondary),
        ("#ECECEE", ThemeRole.Hairline),
        ("#D9D9D9", ThemeRole.InputBorder),
        ("#E9E9EA", ThemeRole.SwitchOffTrack),   // BotCardFooterBuilder.TrackOffColor
        ("#E9E9EB", ThemeRole.Hairline),         // BotCardFooterBuilder.DividerColor — NOT the track
        // destructive: two red variants in BotSettings, unified onto one role
        ("#E53935", ThemeRole.Destructive),
        ("#EB4545", ThemeRole.Destructive),
        ("#F0F0F2", ThemeRole.Background),
        // dashboard period-selector track — previously "needs a design call";
        // dark coverage forces the call: it is ground, and without it the
        // segment labels (already ink-bound) wash out on a light track in dark
        ("#EDEFF3", ThemeRole.Background),
        // chats-panel shell leftovers surfaced by the shell audit
        ("#EFEFF0", ThemeRole.Background),   // search pill well (the "input field" gap)
        ("#1A1A1A", ThemeRole.InkPrimary),   // bot-switcher sheet ink variant
        ("#3A3A3C", ThemeRole.InkSecondary),
        // soft chips — previously "unmapped, needs a design call"; owner round 6
        // forces it: both stay light in dark without a role of their own.
        ("#E8F2FD", ThemeRole.AccentSoft),      // edit-button chip (profile + account)
        ("#FFCED5", ThemeRole.DestructiveSoft), // «Удалить все данные» chip
        // thread chrome (owner round 2: «messages page looks wrong»)
        ("#E9EDEF", ThemeRole.Hairline),     // action-menu dividers
        ("#6E6E73", ThemeRole.InkSecondary), // composer/attach icon tints
        ("#73737A", ThemeRole.InkSecondary),
        ("#667781", ThemeRole.InkSecondary), // WA meta grey
        ("#111B21", ThemeRole.InkPrimary),   // WA near-black ink
    };

    /// <summary>
    /// Per-target extra exclusions. #34C759 is genuinely AMBIGUOUS: on the
    /// dashboard it is the order-collected status, but on Bot.prefab it is the
    /// activation switch's ON green — which must never follow the theme, or
    /// «Бот работает» stops meaning one fixed thing. Value-mapping cannot tell
    /// them apart, so the switch's owner excludes it explicitly.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExtraExclusions = new()
    {
        ["Assets/Prefabs/Bot.prefab"] = new[]
        {
            "#34C759", // activation switch ON — Theme.Fixed.SwitchOnGreen
            "#00FF00", // pure debug green left in the prefab; not a theme colour
            "#2E9BE0", // channel-ish blue; needs a design call, not a guess
        },
    };

    /// <summary>
    /// Never auto-map. White is ambiguous (surface vs sprite "no tint"); the
    /// channel/identity colours must never follow the theme at all.
    /// </summary>
    private static readonly string[] NeverMap =
    {
        "#FFFFFF", "#25D366", "#2AABEE", "#34B7F1", "#00A884", "#2FB344", "#1FA855",
    };

    private static readonly string[] Prefabs =
    {
        "Assets/Prefabs/BotSettings.prefab",
        "Assets/Prefabs/Bot.prefab",
        "Assets/Prefabs/Product.prefab",
        "Assets/Prefabs/Service.prefab",
        "Assets/Prefabs/BotSwitcherRow.prefab",
    };

    [MenuItem("Tools/Theme/Screens/Audit Bots + Dashboard (dry run)")]
    public static void AuditAll() => Run(new[] { "Screen_Bots", "Screen_Dashboard" }, apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Bots + Dashboard (adopt palette)")]
    public static void ApplyAll() => Run(new[] { "Screen_Bots", "Screen_Dashboard" }, apply: true);

    /// <summary>
    /// Chat-thread colours that must NOT be value-mapped.
    ///   • suggestions greens — a LOCKED, research-backed palette for «Бот предлагает
    ///     ответ»; it is deliberate design, not drift, and must be swapped as a block.
    ///   • wallpaper family — the doodle paper/ink is a light-only authored asset.
    ///   • bubble fills — bound BY NAME below instead, since #FFFFFF on this surface
    ///     is overwhelmingly sprite tint (Thumbnail/PreviewImage/DocIcon/PlayOverlay).
    ///   • #1FA855 / #C5CDD2 — delivery-tick and accent greens/greys that need a
    ///     design call rather than a guess.
    /// </summary>
    private static readonly string[] ChatExclusions =
    {
        "#5E7C6E", "#DCEAE2", "#14241D", "#3E6B57", "#DCEFE6",   // suggestions — LOCKED
        "#F3F1EB", "#D9D4CA",                                     // doodle paper + ink
        "#D8FDD4",                                                // outgoing bubble (bound by name)
        "#1FA855", "#C5CDD2",                                     // ticks / ambiguous
    };

    /// <summary>Bubble fills, bound by NAME because their value is ambiguous.</summary>
    private static readonly (string prefab, string[] names, ThemeRole role)[] BubbleSpec =
    {
        ("Assets/Prefabs/MessageTextIncoming.prefab", new[] { "Bubble", "Tail" }, ThemeRole.BubbleIncoming),
        ("Assets/Prefabs/MessageTextOutgoing.prefab", new[] { "Bubble", "Tail" }, ThemeRole.BubbleOutgoing),
    };

    [MenuItem("Tools/Theme/Screens/Audit Chat Thread (dry run)")]
    public static void AuditChat() => RunChat(apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Chat Thread")]
    public static void ApplyChat() => RunChat(apply: true);

    private static void RunChat(bool apply)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] CHAT {(apply ? "APPLY" : "AUDIT")}");
        int total = 0;

        // (a) bubbles, by name
        foreach (var (path, names, role) in BubbleSpec)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { sb.AppendLine($"### {path}: NOT FOUND"); continue; }
            try
            {
                int n = 0;
                foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
                {
                    if (!names.Contains(g.name)) continue;
                    if (g.GetComponent<ThemedColor>() != null) continue;
                    sb.AppendLine($"    {System.IO.Path.GetFileName(path)}/{g.name}  " +
                                  $"#{ColorUtility.ToHtmlStringRGB(g.color)} -> {role} " +
                                  $"(#{ColorUtility.ToHtmlStringRGB(Theme.Light.Resolve(role))})");
                    if (!apply) { n++; continue; }
                    var b = g.gameObject.AddComponent<ThemedColor>();
                    var so = new SerializedObject(b);
                    so.FindProperty("role").enumValueIndex = (int)role;
                    so.FindProperty("target").objectReferenceValue = g;
                    so.FindProperty("preserveAlpha").boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    n++;
                }
                total += n;
                if (apply && n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // (b) safe inks in the message prefabs + separators
        foreach (var path in new[]
        {
            "Assets/Prefabs/MessageTextIncoming.prefab", "Assets/Prefabs/MessageTextOutgoing.prefab",
            "Assets/Prefabs/DateSeparator.prefab", "Assets/Prefabs/UnreadSeparator.prefab",
        })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            try
            {
                int n = BindSubtree(root, apply, sb, System.IO.Path.GetFileName(path), ChatExclusions);
                total += n;
                if (apply && n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // (c) MessagesPanel in the scene
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject panel = null;
        foreach (var r in scene.GetRootGameObjects())
        {
            panel = FindDeep(r.transform, "MessagesPanel");
            if (panel != null) break;
        }
        if (panel != null)
        {
            total += BindSubtree(panel, apply, sb, "MessagesPanel", ChatExclusions);
            if (apply)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        Debug.Log(sb.ToString());
        if (apply) Debug.Log($"[ScreenThemeWirer] Chat thread applied. {total} binding(s).");
    }

    [MenuItem("Tools/Theme/Screens/Audit Prefabs (dry run)")]
    public static void AuditPrefabs() => RunPrefabs(apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Prefabs (adopt palette)")]
    public static void ApplyPrefabs() => RunPrefabs(apply: true);

    private static void RunPrefabs(bool apply)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] PREFABS {(apply ? "APPLY" : "AUDIT")}");
        int total = 0;

        foreach (var path in Prefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { sb.AppendLine($"\n### {path}: NOT FOUND"); continue; }
            try
            {
                ExtraExclusions.TryGetValue(path, out var extra);
                int added = BindSubtree(root, apply, sb, path, extra ?? System.Array.Empty<string>());
                total += added;
                if (apply && added > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        Debug.Log(sb.ToString());
        if (apply) Debug.Log($"[ScreenThemeWirer] Prefabs applied. {total} binding(s) added.");
    }

    private static void Run(string[] roots, bool apply)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] {(apply ? "APPLY" : "AUDIT")}");
        int totalAdded = 0;

        foreach (var rootName in roots)
        {
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                root = FindDeep(go.transform, rootName);
                if (root != null) break;
            }
            if (root == null)
            {
                sb.AppendLine($"\n### {rootName}: NOT FOUND");
                continue;
            }

            totalAdded += BindSubtree(root, apply, sb, rootName, System.Array.Empty<string>());
        }

        Debug.Log(sb.ToString());

        if (!apply) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ScreenThemeWirer] Applied + scene saved. {totalAdded} binding(s) added. " +
                  "No objects created/destroyed/moved.");
    }

    /// <summary>Shared binding pass — used for both scene roots and prefabs.</summary>
    private static int BindSubtree(GameObject root, bool apply, StringBuilder sb,
                                   string label, string[] extraExclusions)
    {
        var mapped = new Dictionary<ThemeRole, int>();
        var unmapped = new Dictionary<string, int>();
        int added = 0, already = 0;

        foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
        {
            if (InAlwaysDarkOverlay(g.transform)) continue;
            string hex = "#" + ColorUtility.ToHtmlStringRGB(g.color);
            if (NeverMap.Contains(hex) || extraExclusions.Contains(hex)) continue;

            var hit = ValueMap.FirstOrDefault(m => m.hex == hex);
            if (hit.hex == null)
            {
                unmapped.TryGetValue(hex, out var n);
                unmapped[hex] = n + 1;
                continue;
            }

            mapped.TryGetValue(hit.role, out var c);
            mapped[hit.role] = c + 1;

            if (g.GetComponent<ThemedColor>() != null) { already++; continue; }
            if (!apply) { added++; continue; }

            var binding = g.gameObject.AddComponent<ThemedColor>();
            var so = new SerializedObject(binding);
            so.FindProperty("role").enumValueIndex = (int)hit.role;
            so.FindProperty("target").objectReferenceValue = g;
            so.FindProperty("preserveAlpha").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            added++;
        }

        sb.AppendLine($"\n### {label}  —  bound {added}, already {already}");
        foreach (var kv in mapped.OrderByDescending(k => k.Value))
            sb.AppendLine($"    {kv.Key,-24} x{kv.Value,-4} -> " +
                          $"{"#" + ColorUtility.ToHtmlStringRGB(Theme.Light.Resolve(kv.Key))}");
        if (unmapped.Count > 0)
        {
            sb.AppendLine("    unmapped (left alone — needs a design call):");
            foreach (var kv in unmapped.OrderByDescending(k => k.Value).Take(10))
                sb.AppendLine($"       {kv.Key} x{kv.Value}");
        }
        return added;
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

    // ── Shell pass: the surfaces dark mode was missing ─────────────────────
    //
    // Screens' TEXT went through the value passes, but their white chrome never
    // did — #FFFFFF is never value-mapped. Result in dark: light ink on white
    // cards, white nav/header bars on a dark ground. This pass binds white
    // SURFACES to ThemeRole.Surface under a tight structural rule instead of a
    // value rule:
    //
    //   Image + sprite == null + white + alpha ≥ 0.9
    //
    // which on this project singles out exactly the null-sprite+RoundedCorners
    // card/bar/pill fills. Sprited whites (icons, thumbnails) never match.
    // Name filters then drop the known non-surfaces:
    //   • Thumb/Knob — switch knobs must stay white on their track
    //   • Emoji*     — runtime-sprite targets; white is tint, not fill
    //   • *Icon* or under an "Icon" node — glyph art built from white bars
    //   • FullScreenImage — the photo viewer's texture target
    //   • SuggestionCard — the LOCKED suggestions system stamps card fills
    //     (rank tint) at runtime; a binding would fight it on every refresh
    //
    // The ValueMap sweep also runs on these roots so their non-white chrome
    // (profile bg, dividers, chevrons, track greys) binds in the same pass.
    // LoadingPanel is deliberately NOT here: it is an always-dark scrim (see
    // AlwaysDarkRoots) — sweeping it is what wrongly bound its black cover as ink.
    private static readonly string[] ShellSceneRoots =
    {
        "Screen_Profile", "Screen_Bots", "Screen_Dashboard",
        "BottomNavPanel", "ChatsPanel", "MessagesPanel",
    };

    /// <summary>
    /// Scene-root exclusions for the shell ValueMap sweep. #34C759 on toggles is
    /// the switch-ON green (Theme.Fixed), same ambiguity as Bot.prefab; the chat
    /// roots keep the chat exclusions (suggestions greens, wallpaper, ticks).
    /// </summary>
    private static string[] ShellExclusions(string root) => root switch
    {
        "Screen_Profile" => new[] { "#34C759" },
        "ChatsPanel" or "MessagesPanel" => ChatExclusions.Concat(new[] { "#34C759" }).ToArray(),
        _ => System.Array.Empty<string>(),
    };

    /// <summary>
    /// Explicit path bindings that no value/whites rule can decide:
    ///   • the thread's paper-coloured chrome (#F3F1EB is deliberately excluded
    ///     from the value sweep — QuickReplyPanel shares it and is runtime-stamped)
    ///   • the doodle art, themed by TINT (white passthrough / dark multiplier)
    ///   • the nav bar hairline (#CCCCCC would be too broad as a value rule)
    /// QuickReplyPanel itself is deliberately absent: QuickReplyPanel.cs stamps
    /// its root/cards with in/out variants and needs its own routing pass.
    /// </summary>
    private static readonly (string root, string path, ThemeRole role)[] SceneNamedSpec =
    {
        ("MessagesPanel", "MovingArea/Background",             ThemeRole.ChatWallpaper),
        ("MessagesPanel", "MovingArea/Background/Image",       ThemeRole.ChatWallpaperInk),
        ("MessagesPanel", "TopBar/Background",                 ThemeRole.ChatWallpaper),
        ("MessagesPanel", "MovingArea/BottomPanel/Background", ThemeRole.ChatWallpaper),
        ("BottomNavPanel", "Line",                             ThemeRole.Hairline),
        // Owner round 3 — thread chrome details. The composer icons are WHITE
        // art with a white authored tint (the dark look in light mode was an
        // unsaved hand tint); InkPrimary gives dark-in-light / light-in-dark.
        // The back circle sits at alpha 0.8 (below the whites-rule threshold) —
        // preserveAlpha keeps that. The QuickReply root is paper-coloured.
        ("MessagesPanel", "TopBar/LeftZone/BackButton",        ThemeRole.Surface),
        ("MessagesPanel", "MovingArea/BottomPanel/HorizontalLayout/AttachButton/Image", ThemeRole.InkPrimary),
        ("MessagesPanel", "MovingArea/BottomPanel/HorizontalLayout/MicButton/Image",    ThemeRole.InkPrimary),
        ("MessagesPanel", "MovingArea/BottomPanel/HorizontalLayout/SendButton/Circle",  ThemeRole.SendButton),
        ("MessagesPanel", "MovingArea/QuickReplyPanel",        ThemeRole.ChatWallpaper),
        // Owner round 4. The composer ring (#DDD9D8) reads as a decorative
        // outline, not a 3:1 affordance — Border keeps it subtle in both modes.
        // Typed text was #323232 (fine on white, invisible on dark); the caret
        // follows it via customCaretColor=false in ExpandableInput. The chats
        // search pill (#F1F1F1) becomes an inset well like the composer's.
        ("MessagesPanel", "MovingArea/BottomPanel/HorizontalLayout/InputOutline", ThemeRole.Border),
        ("MessagesPanel", "MovingArea/BottomPanel/HorizontalLayout/InputOutline/Input/InputField/Text Area/Text", ThemeRole.InkPrimary),
        ("ChatsPanel",    "Scroll/Viewport/Content/ChatsSearchBar/Pill", ThemeRole.Background),
    };

    private static readonly (string prefab, string child, ThemeRole role)[] PrefabNamedSpec =
    {
        ("Assets/Prefabs/DateSeparator.prefab", "BackgroundPill", ThemeRole.Surface),
        // The tail's border-coloured outline sits BEHIND the themed tail fill and
        // was still authored light — the "wrong colour" glow behind dark bubbles.
        ("Assets/Prefabs/MessageTextIncoming.prefab", "TailOutline", ThemeRole.BubbleBorder),
        ("Assets/Prefabs/MessageTextOutgoing.prefab", "TailOutline", ThemeRole.BubbleBorder),
        // Quoted-reply snippet: authored #4D4D4D — InkSecondary is a ΔE≈1 no-op
        // in light and readable on the dark bubble.
        ("Assets/Prefabs/MessageTextIncoming.prefab", "Bubble/QuotedCard/TextColumn/Snippet", ThemeRole.InkSecondary),
        ("Assets/Prefabs/MessageTextOutgoing.prefab", "Bubble/QuotedCard/TextColumn/Snippet", ThemeRole.InkSecondary),
        // The products/services bottom fade is WHITE ART with an alpha ramp
        // (Blur.png), so it themes by TINT — no new art needed. Background, not
        // Surface: its job is to dissolve the list into the panel ground, which
        // is #F0F2F5 -> Background. Named because the whites rule skips sprites.
        ("Assets/Prefabs/BotSettings.prefab", "Product/StickyFooter", ThemeRole.Background),
        ("Assets/Prefabs/BotSettings.prefab", "Service/StickyFooter", ThemeRole.Background),
    };

    [MenuItem("Tools/Theme/Screens/Audit Shell — nav, headers, profile, inputs (dry run)")]
    public static void AuditShell() => RunShell(apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Shell — nav, headers, profile, inputs")]
    public static void ApplyShell() => RunShell(apply: true);

    private static void RunShell(bool apply)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] SHELL {(apply ? "APPLY" : "AUDIT")} v2");
        int total = 0;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var rootName in ShellSceneRoots)
        {
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                root = FindDeep(go.transform, rootName);
                if (root != null) break;
            }
            if (root == null) { sb.AppendLine($"\n### {rootName}: NOT FOUND"); continue; }

            total += BindWhiteSurfaces(root, apply, sb, rootName);
            total += BindSubtree(root, apply, sb, rootName, ShellExclusions(rootName));

            foreach (var (specRoot, path, role) in SceneNamedSpec)
            {
                if (specRoot != rootName) continue;
                var t = root.transform.Find(path);
                var g = t != null ? t.GetComponent<Graphic>() : null;
                if (g == null) { sb.AppendLine($"    NAMED MISS: {rootName}/{path}"); continue; }
                total += BindNamed(g, role, apply, sb, $"{rootName}/{path}");
            }
        }
        if (apply)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // Prefab cards live the same problem: bound ink on unbound white fills.
        foreach (var path in Prefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            try
            {
                int n = BindWhiteSurfaces(root, apply, sb, System.IO.Path.GetFileName(path));
                total += n;
                if (apply && n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        foreach (var (prefabPath, child, role) in PrefabNamedSpec)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) continue;
            try
            {
                var t = root.transform.Find(child);
                var g = t != null ? t.GetComponent<Graphic>() : null;
                if (g == null) { sb.AppendLine($"    NAMED MISS: {prefabPath}/{child}"); continue; }
                int n = BindNamed(g, role, apply, sb,
                    $"{System.IO.Path.GetFileName(prefabPath)}/{child}");
                total += n;
                if (apply && n > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        Debug.Log(sb.ToString());
        if (apply) Debug.Log($"[ScreenThemeWirer] Shell applied. {total} binding(s).");
    }

    // ── Always-dark overlays ───────────────────────────────────────────────
    //
    // The attachment preview, photo viewer and video player are media overlays
    // that are DARK IN BOTH THEMES — like every messenger's. The value sweep
    // couldn't know that and bound their black bars as "ink" (#000000 is a
    // mapped ink value), so in dark mode the bars resolved near-WHITE and the
    // owner saw the screen invert. This pass strips every ThemedColor under
    // them, and restyles the attachment screen's authored blacks onto the
    // «Графит» dark set — static colours, deliberately NOT roles.
    //
    // Bot.prefab's activation-switch track binding is removed here too: the
    // track is STATE-driven by Bot.cs (fixed green when ON), which now resolves
    // SwitchOffTrack itself for the off state.
    private static readonly string[] AlwaysDarkRoots =
    {
        "AttachmentPreviewScreen", "PhotoViewerPanel", "VideoPlayerPanel",
        "LoadingPanel", // near-opaque black loading cover — dark in both themes
    };

    /// <summary>Static «Графит» recolours for always-dark chrome. Empty path = the root itself.</summary>
    private static readonly (string root, string path, string hex)[] OverlayRefine =
    {
        ("LoadingPanel", "",                                       "#0E1116"), // keeps 0.98 alpha
        ("AttachmentPreviewScreen", "Root",                        "#0E1116"),
        ("AttachmentPreviewScreen", "Root/TopBar",                 "#0E1116"),
        ("AttachmentPreviewScreen", "Root/BottomBar",              "#0E1116"),
        ("AttachmentPreviewScreen", "Root/BottomBar/Background",   "#0E1116"),
        ("AttachmentPreviewScreen", "Root/BottomBar/CaptionScroll","#171C24"),
        ("AttachmentPreviewScreen", "Root/TopBar/Content/BackButton","#171C24"), // keeps 0.8 alpha
    };

    /// <summary>
    /// True when a graphic lives inside an always-dark overlay. The shell/chat
    /// sweeps must skip these subtrees, or every apply would re-bind exactly the
    /// bindings the always-dark pass removes.
    /// </summary>
    private static bool InAlwaysDarkOverlay(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (System.Array.IndexOf(AlwaysDarkRoots, p.name) >= 0) return true;
        return false;
    }

    [MenuItem("Tools/Theme/Screens/Audit Always-Dark Overlays (dry run)")]
    public static void AuditAlwaysDark() => RunAlwaysDark(apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Always-Dark Overlays")]
    public static void ApplyAlwaysDark() => RunAlwaysDark(apply: true);

    private static void RunAlwaysDark(bool apply)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] ALWAYS-DARK {(apply ? "APPLY" : "AUDIT")}");
        int removed = 0, recoloured = 0;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var rootName in AlwaysDarkRoots)
        {
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                root = FindDeep(go.transform, rootName);
                if (root != null) break;
            }
            if (root == null) { sb.AppendLine($"    {rootName}: NOT FOUND"); continue; }

            foreach (var tc in root.GetComponentsInChildren<ThemedColor>(includeInactive: true))
            {
                var so = new SerializedObject(tc);
                var roleProp = so.FindProperty("role");
                sb.AppendLine($"    REMOVE {(ThemeRole)roleProp.enumValueIndex} on " +
                              $"{TransformPath(tc.transform, root.transform.parent)}");
                if (apply) Object.DestroyImmediate(tc);
                removed++;
            }

            foreach (var (specRoot, path, hex) in OverlayRefine)
            {
                if (specRoot != rootName) continue;
                var t = path.Length == 0 ? root.transform : root.transform.Find(path);
                var g = t != null ? t.GetComponent<Graphic>() : null;
                if (g == null) { sb.AppendLine($"    REFINE MISS: {rootName}/{path}"); continue; }
                if (!ColorUtility.TryParseHtmlString(hex, out var c)) continue;
                if (ColorUtility.ToHtmlStringRGB(g.color) == hex.TrimStart('#')) continue; // already done
                sb.AppendLine($"    REFINE {rootName}/{path}: " +
                              $"#{ColorUtility.ToHtmlStringRGB(g.color)} -> {hex} (alpha kept)");
                if (apply) g.color = new Color(c.r, c.g, c.b, g.color.a);
                recoloured++;
            }
        }
        if (apply)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // Bot.prefab activation-switch track binding
        var botRoot = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Bot.prefab");
        if (botRoot != null)
        {
            try
            {
                var track = botRoot.transform.Find("FooterRow/ActivationSwitch/Background");
                var tc = track != null ? track.GetComponent<ThemedColor>() : null;
                if (tc != null)
                {
                    sb.AppendLine("    REMOVE SwitchOffTrack binding on Bot.prefab " +
                                  "FooterRow/ActivationSwitch/Background (state-driven by Bot.cs)");
                    if (apply)
                    {
                        Object.DestroyImmediate(tc);
                        PrefabUtility.SaveAsPrefabAsset(botRoot, "Assets/Prefabs/Bot.prefab");
                    }
                    removed++;
                }
                else sb.AppendLine("    Bot.prefab switch track: no binding (already clean)");
            }
            finally { PrefabUtility.UnloadPrefabContents(botRoot); }
        }

        Debug.Log(sb.ToString());
        if (apply) Debug.Log($"[ScreenThemeWirer] Always-dark applied. " +
                             $"{removed} binding(s) removed, {recoloured} colour(s) refined.");
    }

    private static int BindNamed(Graphic g, ThemeRole role, bool apply, StringBuilder sb, string label)
    {
        if (g.GetComponent<ThemedColor>() != null) return 0;
        sb.AppendLine($"    NAMED: {label}  #{ColorUtility.ToHtmlStringRGB(g.color)} -> {role} " +
                      $"(#{ColorUtility.ToHtmlStringRGB(Theme.Light.Resolve(role))})");
        if (!apply) return 1;
        var binding = g.gameObject.AddComponent<ThemedColor>();
        var so = new SerializedObject(binding);
        so.FindProperty("role").enumValueIndex = (int)role;
        so.FindProperty("target").objectReferenceValue = g;
        so.FindProperty("preserveAlpha").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return 1;
    }

    /// <summary>
    /// The 9-sliced rounded-rect card sprite. On it, white TINT is the card fill
    /// (Profile's main-list cards, EditPopup) — so it counts as a surface even
    /// though it is sprited. Any other sprite keeps the "white = no tint" rule.
    /// </summary>
    private const string CardSpriteGuid = "7b3e9d159c0fd461e9bccc29d18eafdb";

    private static int BindWhiteSurfaces(GameObject root, bool apply, StringBuilder sb, string label)
    {
        int added = 0;
        var lines = new List<string>();
        foreach (var img in root.GetComponentsInChildren<Image>(includeInactive: true))
        {
            if (InAlwaysDarkOverlay(img.transform)) continue;
            if (img.sprite != null && !IsCardSprite(img.sprite)) continue;
            var c = img.color;
            if (c.r < 0.999f || c.g < 0.999f || c.b < 0.999f || c.a < 0.9f) continue;
            if (SkipAsWhiteSurface(img.transform)) continue;
            if (img.GetComponent<ThemedColor>() != null) continue;

            lines.Add($"    {TransformPath(img.transform, root.transform)} -> Surface");
            if (apply)
            {
                var binding = img.gameObject.AddComponent<ThemedColor>();
                var so = new SerializedObject(binding);
                so.FindProperty("role").enumValueIndex = (int)ThemeRole.Surface;
                so.FindProperty("target").objectReferenceValue = img;
                so.FindProperty("preserveAlpha").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            added++;
        }
        if (lines.Count > 0)
        {
            sb.AppendLine($"\n### {label} — white surfaces ({added})");
            foreach (var l in lines) sb.AppendLine(l);
        }
        return added;
    }

    private static bool IsCardSprite(Sprite s) =>
        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s)) == CardSpriteGuid;

    private static bool SkipAsWhiteSurface(Transform t)
    {
        string n = t.name;
        // Handle = the activation-switch knob (Bot.prefab names it Handle, not Knob).
        // Chip = dashboard bot-filter chips, runtime-stamped by DashboardPage —
        // their off-state fill is theme-routed in code instead of bound.
        // Default = the thread top-bar avatar placeholder (white is sprite tint).
        if (n == "Thumb" || n == "Knob" || n == "Handle" || n == "Chip" ||
            n == "Default" || n == "FullScreenImage" || n == "SuggestionCard") return true;
        if (n.Contains("Icon") || n.StartsWith("Emoji")) return true;
        for (var p = t.parent; p != null; p = p.parent)
            if (p.name == "Icon") return true;
        return false;
    }

    private static string TransformPath(Transform t, Transform stopAt)
    {
        var parts = new List<string>();
        for (var cur = t; cur != null && cur != stopAt; cur = cur.parent) parts.Add(cur.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
