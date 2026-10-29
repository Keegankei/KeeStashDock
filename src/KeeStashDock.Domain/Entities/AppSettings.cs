namespace KeeStashDock.Domain.Entities;

/// <summary>
/// Strongly-typed user settings, persisted as JSON in Data/settings.json.
/// Kept in Domain so both Core services and the Desktop UI can consume it
/// without circular references. Add new members with sensible defaults only -
/// missing JSON properties must always deserialize to a valid state
/// (forward/backward compatibility for the portable app folder).
/// </summary>
public sealed class AppSettings
{
    public const int DefaultTrashRetentionDays = 30;
    public const int MaxTrashRetentionDays = 3650;
    public const int DefaultBackupKeepCount = 7;
    public const int MaxBackupKeepCount = 100;
    public const int DefaultAutoBackupIntervalHours = 24;
    public const int MaxAutoBackupIntervalHours = 24 * 30;
    public const int DefaultAutoBackupIntervalMinutes = 24 * 60;
    public const int MaxAutoBackupIntervalMinutes = MaxAutoBackupIntervalHours * 60;
    public const uint DefaultScreenshotHotkeyModifiers = 0x0001; // MOD_ALT
    public const uint DefaultScreenshotHotkeyVirtualKey = 0x57; // 'W'
    public const double DefaultBackgroundOverlayOpacity = 0.20;
    public const string DefaultTranslationEngine = "Google";
    public const string DefaultSelectionBubbleStyle = "Default";
    public const string DefaultOfflineTranslationEndpoint = "http://127.0.0.1:5000";
    public const int DefaultFontSizePercent = 100;
    public const int MinFontSizePercent = 90;
    public const int MaxFontSizePercent = 150;

    // ---- General -------------------------------------------------------
    /// <summary>BCP-47 language tag of the active UI language. "en-US" is the product default.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Global interface text size relative to the original typography; icons remain unchanged.</summary>
    public int FontSizePercent { get; set; } = DefaultFontSizePercent;

    /// <summary>Launch KeeStashDock when Windows starts (HKCU Run key).</summary>
    public bool AutoStart { get; set; } = false;

    // ---- Hotkey --------------------------------------------------------
    // Stored as Win32 modifier flags + virtual-key code so the hotkey
    // manager can re-register without any parsing. Default: Alt+Q.
    public uint HotkeyModifiers { get; set; } = 0x0001;   // MOD_ALT
    public uint HotkeyVirtualKey { get; set; } = 0x51;    // 'Q'

    /// <summary>Global screenshot shortcut; the screenshot button remains available when disabled.</summary>
    public bool ScreenshotHotkeyEnabled { get; set; } = true;
    public uint ScreenshotHotkeyModifiers { get; set; } = DefaultScreenshotHotkeyModifiers;
    public uint ScreenshotHotkeyVirtualKey { get; set; } = DefaultScreenshotHotkeyVirtualKey;

    // ---- Quick panel ---------------------------------------------------
    /// <summary>Retired edge-summon setting, retained so older
    /// settings.json files continue to deserialize cleanly.</summary>
    public bool EdgeDockEnabled { get; set; } = false;

    /// <summary>Enabled by default: a panel dragged to a work-area edge retracts to a
    /// narrow handle while idle. Independent from the retired edge-summon flag.</summary>
    public bool PanelEdgeAutoHideEnabled { get; set; } = true;

    /// <summary>"Right" or "Left". This only selects the default
    /// position of the never-dragged panel (RTL languages default Left).</summary>
    public string DockEdge { get; set; } = "Right";

    /// <summary>Legacy quick-panel width in device-independent pixels;
    /// the three-state panel now sizes itself from the work area.</summary>
    public double PanelWidth { get; set; } = 320;

    // ---- Three-state panel ----------------------------------------------
    /// <summary>Seconds of "mouse away AND no keyboard focus" after which an
    /// UNLOCKED expanded panel collapses to the shrunk bar. User-tunable.</summary>
    public double CollapseDelaySeconds { get; set; } = 2.0;

    /// <summary>Seconds of hover/focus/proximity after which an UNLOCKED
    /// shrunk bar grows back to the expanded panel. User-tunable.</summary>
    public double ExpandDelaySeconds { get; set; } = 0.1;

    /// <summary>Persisted top-left corner of the draggable panel (DIPs).
    /// Null = never dragged; the panel computes a default near the dock edge.</summary>
    public double? PanelPosX { get; set; }
    public double? PanelPosY { get; set; }

    /// <summary>Physical monitor coordinates of the normal panel, never the hidden edge handle.</summary>
    public int? PanelDevicePosX { get; set; }
    public int? PanelDevicePosY { get; set; }

    /// <summary>User-resized panel size (edge-drag). Null = defaults
    /// (15% of work-area width x 50% of work-area height).</summary>
    public double? PanelUserWidth { get; set; }
    public double? PanelUserHeight { get; set; }

    // ---- Custom background ----------------------------------------------
    /// <summary>Absolute path of a user-chosen background image for the quick
    /// panel. Null/missing file = plain themed background.</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>Independent user-chosen Settings artwork. A custom null path explicitly hides artwork.</summary>
    public string? SettingsBackgroundImagePath { get; set; }
    public double SettingsBackgroundOverlayOpacity { get; set; } = DefaultBackgroundOverlayOpacity;
    /// <summary>Separately tracks a Settings scrim edit so old appearance overrides keep the theme's scrim.</summary>
    public bool SettingsBackgroundOverlayCustomized { get; set; }
    /// <summary>Independent Settings surface and blur edits; legacy global appearance overrides remain supported.</summary>
    public bool SettingsAppearanceCustomized { get; set; }

    /// <summary>Opacity (0.2-1.0) of the theme-colored scrim laid OVER the
    /// background image. Lower = more image shows through ("see-through"),
    /// higher = closer to the plain theme. Default 0.20 lets the wallpaper
    /// show through; the auto-contrast text keeps things readable.</summary>
    public double BackgroundOverlayOpacity { get; set; } = DefaultBackgroundOverlayOpacity;

    /// <summary>Frosted-glass: gaussian blur applied to the background IMAGE
    /// so content floats on a soft surface. Needs an image to act on.</summary>
    public bool FrostedGlassEnabled { get; set; }

    /// <summary>Frost blur radius in pixels (1-18). Default 4 (subtle).</summary>
    public double FrostedGlassRadius { get; set; } = 4;

    /// <summary>Opacity 0.1-1.0 of the panel controls (cards, input box,
    /// combos) so they float translucently over a background image.
    /// Default 0.9 - just enough see-through to feel layered.</summary>
    public double CardOpacity { get; set; } = 0.9;

    /// <summary>Opacity 0.1-1.0 of Settings surfaces, independent of quick-panel
    /// controls. Text remains opaque. Missing values preserve the opaque layout.</summary>
    public double SettingsSurfaceOpacity { get; set; } = 1.0;

    /// <summary>Blur only the Settings theme artwork, never text or controls.</summary>
    public bool SettingsFrostedGlassEnabled { get; set; }
    public double SettingsFrostedGlassRadius { get; set; } = 8;

    /// <summary>RETIRED (tear-out is now the drag gesture, no timing knob).
    /// Kept so existing settings.json files still deserialize cleanly.</summary>
    public double TearOutHoldSeconds { get; set; } = 1.0;

    /// <summary>Foreground scheme of the quick panel's text and buttons:
    /// "Auto" derives light/dark per surface from the wallpaper brightness;
    /// "Light"/"Dark" force the chrome scheme; the named presets (Ivory,
    /// Graphite, Amber, Mint, Sky, Rose) and "Custom" force ONE palette on
    /// all three surfaces, VSCode-theme style.</summary>
    public string PanelTextColorMode { get; set; } = "Auto";

    /// <summary>Hex color (#RRGGBB) used when PanelTextColorMode = "Custom".</summary>
    public string PanelTextCustomColor { get; set; } = "#FFFFFF";

    /// <summary>RETIRED (particle layer removed by design decision).
    /// Kept so existing settings.json files still deserialize cleanly.</summary>
    public bool PanelParticlesEnabled { get; set; }

    /// <summary>RETIRED (sheen/divider polish removed by design decision).
    /// Kept so existing settings.json files still deserialize cleanly.</summary>
    public bool PanelFancyEnabled { get; set; }

    /// <summary>Retired plain-paste setting, retained so existing
    /// settings.json files continue to deserialize cleanly.</summary>
    public bool PlainPasteEnabled { get; set; }

    /// <summary>Selection popup: a "copy to KeeStashDock" bubble appears
    /// beside text selections anywhere on the desktop; clicking it captures
    /// the selection as plain text into the panel's current group.</summary>
    public bool SelectionPopupEnabled { get; set; } = true;

    /// <summary>Selection-bubble actions: "Capture" copies the selection into
    /// KeeStashDock; "Translate" uses the configured translation target;
    /// "Both" offers independent capture and translation buttons.</summary>
    public string SelectionAction { get; set; } = "Capture";

    /// <summary>Visual dress of the selection bubble / toasts / translation
    /// card: "FollowTheme" derives glass from the text-color scheme;
    /// "Default" uses fixed soft monochrome surfaces and text.</summary>
    public string SelectionBubbleStyle { get; set; } = DefaultSelectionBubbleStyle;

    /// <summary>New installs default to Google. Existing choices
    /// are preserved. Cloud credentials are separate for each provider;
    /// Offline uses a locally installed translation service.</summary>
    public string TranslationEngine { get; set; } = DefaultTranslationEngine;

    /// <summary>FollowInterface preserves older behavior; explicit targets never change UI language.</summary>
    public string TranslationTargetLanguage { get; set; } = TranslationTargets.FollowInterface;

    /// <summary>Show the temporary target-language selector after OCR or translation completes.</summary>
    public bool TranslationResultShowTargetSelector { get; set; } = true;

    /// <summary>Show explicit group and save actions on completed OCR or translation results.</summary>
    public bool TranslationResultShowSavePanel { get; set; } = true;

    /// <summary>Bubble surface opacity, 0.5..1.0. At 1.0 the glass goes
    /// fully opaque - the cure when the desktop bleeds through the text.</summary>
    public double BubbleOpacity { get; set; } = 1.0;

    /// <summary>Clipboard history collection master switch (opt-IN: default
    /// off - watching the clipboard is a privacy decision the user makes).</summary>
    public bool ClipboardWatchEnabled { get; set; } = false;


    /// <summary>Direct paste: clicking a card also pastes it into the
    /// window that was in the foreground before the panel (opt-in).</summary>
    public bool DirectPasteEnabled { get; set; } = false;

    /// <summary>Scheduled automatic database backups (Data\Backups).</summary>
    public bool AutoBackupEnabled { get; set; } = true;

    /// <summary>Hours between automatic backups. Default: daily.</summary>
    public int AutoBackupIntervalHours { get; set; } = DefaultAutoBackupIntervalHours;

    /// <summary>Automatic database snapshot interval, in minutes. The legacy
    /// hours field is read only for migration when this field is absent.</summary>
    public int AutoBackupIntervalMinutes { get; set; } = DefaultAutoBackupIntervalMinutes;

    /// <summary>User-provided DeepL key, never reused for another provider.</summary>
    public string TranslationApiKey { get; set; } = "";

    /// <summary>User-provided Baidu translation application ID.</summary>
    public string TranslationAppId { get; set; } = "";

    /// <summary>User-provided Baidu secret, separate from the DeepL key.</summary>
    public string BaiduTranslationApiKey { get; set; } = "";

    /// <summary>User-provided Alibaba Cloud AccessKey ID for TranslateGeneral.</summary>
    public string AliyunAccessKeyId { get; set; } = "";

    /// <summary>Alibaba Cloud secret, never reused for another provider.</summary>
    public string AliyunAccessKeySecret { get; set; } = "";

    /// <summary>Loopback-only LibreTranslate-compatible local service address.</summary>
    public string OfflineTranslationEndpoint { get; set; } = DefaultOfflineTranslationEndpoint;

    /// <summary>Optional root containing OcrModels and OcrEngine. Null keeps the existing portable/user lookup.</summary>
    public string? OcrInstallDirectory { get; set; }

    /// <summary>Optional offline translation runtime directory. Changing it never moves or deletes existing components.</summary>
    public string? OfflineTranslationInstallDirectory { get; set; }

    // ---- Appearance ----------------------------------------------------
    /// <summary>Application theme: "Light", "Dark", or "System".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Empty retains the original appearance; otherwise builtin:id or custom:id.</summary>
    public string ThemePresetId { get; set; } = "";

    /// <summary>Preserves a user wallpaper independently of a preset. Applying a full preset disables this override without deleting its path.</summary>
    public bool ThemeUseCustomBackground { get; set; } = true;

    /// <summary>Missing values preserve the theme's workspace artwork in Settings.</summary>
    public bool ThemeUseCustomSettingsBackground { get; set; }

    /// <summary>Manual appearance edits override a selected preset while preserving its complete light/dark colors.</summary>
    public bool ThemeAppearanceCustomized { get; set; }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();

    /// <summary>Opt-in floating panel depth and interaction animations.
    /// Missing settings preserve the classic appearance.</summary>
    public bool FloatingPanelEnabled { get; set; } = false;

    /// <summary>Enable deliberate drag inspection in floating mode. False keeps a fixed
    /// perspective; the existing preference is retained for compatibility.</summary>
    public bool FloatingPanelFollowPointer { get; set; } = true;

    /// <summary>Drag inspection preset: Right, Middle, or Custom. The legacy
    /// FollowPointer preference now selects deliberate drag inspection.</summary>
    public string FloatingInspectionGesture { get; set; } = "Right";

    /// <summary>Custom inspection mouse button: Left, Right, Middle, XButton1, or XButton2.</summary>
    public string FloatingInspectionButton { get; set; } = "Left";

    /// <summary>Custom modifier combination, using Control, Alt and Shift
    /// joined with '+', or None. Alt+Left is the initial custom gesture.</summary>
    public string FloatingInspectionModifiers { get; set; } = "Alt";

    /// <summary>One-shot flag: the starter groups ("Default" + "Git BASH"
    /// with its pre-filled high-frequency git commands) have been created.
    /// Prevents re-seeding after the user renames or deletes them.</summary>
    public bool DefaultGroupsSeeded { get; set; }

    // ---- Data ----------------------------------------------------------
    /// <summary>Optional absolute snapshot folder. Empty uses the Backups
    /// folder beside the active database; changing it preserves old files.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Days a snippet stays in the recycle bin before physical purge.
    /// Zero is invalid and normalizes to <see cref="DefaultTrashRetentionDays"/>.</summary>
    public int TrashRetentionDays { get; set; } = DefaultTrashRetentionDays;

    /// <summary>Number of automatic database snapshots to keep. Zero is invalid
    /// and normalizes to <see cref="DefaultBackupKeepCount"/>.</summary>
    public int BackupKeepCount { get; set; } = DefaultBackupKeepCount;
}
