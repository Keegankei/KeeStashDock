using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop;

/// <summary>
/// Composition root and application lifecycle.
///
/// Startup sequence (cold-start budget <= 1.5 s):
///   1. single-instance claim (bail out fast on second launch),
///   2. logger + settings + localization (cheap, synchronous),
///   3. database migration + housekeeping start,
///   4. tray icon + global hotkey,
///   5. NO window is created until first summon - the app boots stowed.
/// </summary>
public partial class App : Application
{
    /// <summary>Process-wide logger handle (also used by LocalizationService).</summary>
    public static FileLogger? Log { get; private set; }

    private ServiceProvider _services = null!;
    private SingleInstance _instance = null!;
    private TrayIconManager _tray = null!;
    private HotkeyManager _hotkey = null!;

    private QuickPanelWindow? _panel;
    private WorkbenchWindow? _workbench;
    private SettingsWindow? _settingsWin;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- 1. Single instance -----------------------------------------
        _instance = new SingleInstance();
        if (!_instance.TryClaim())
        {
            Shutdown();                      // existing instance was notified
            return;
        }
        _instance.ActivationRequested += () => Dispatcher.Invoke(TogglePanel);

        // ---- 2. Crash handlers ------------------------------------------
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        // ---- 3. Services ------------------------------------------------
        Log = new FileLogger(PortablePaths.LogsDir);
        Log.Info($"KeeStashDock starting (data root: {PortablePaths.DataRoot})");

        var sc = new ServiceCollection();
        sc.AddSingleton(Log);
        sc.AddSingleton<Db>();
        sc.AddSingleton<MigrationRunner>();
        sc.AddSingleton<SnippetRepository>();
        sc.AddSingleton<CategoryRepository>();
        sc.AddSingleton<TagRepository>();
        sc.AddSingleton<GroupIoService>();
        sc.AddSingleton<SettingsStore>();
        sc.AddSingleton<BackupService>();
        sc.AddSingleton<SnippetService>();
        sc.AddSingleton<SearchService>();
        sc.AddSingleton<HousekeepingService>();
        _services = sc.BuildServiceProvider();

        var settings = _services.GetRequiredService<SettingsStore>();
        settings.Load();

        // ---- 4. Localization (before ANY UI text is produced) -----------
        LocalizationService.Instance.SetLanguage(settings.Current.Language);

        // ---- 5. Database ------------------------------------------------
        _services.GetRequiredService<MigrationRunner>().Run();
        SeedStarterGroups(settings);
        _services.GetRequiredService<HousekeepingService>().Start();

        // ---- 6. Theme ----------------------------------------------------
        ThemeManager.Apply(settings.Current.Theme);

        // ---- 7. Shell: tray and global hotkey ----------------------------
        _tray = new TrayIconManager();
        _tray.ShowPanelRequested     += TogglePanel;
        _tray.ShowWorkbenchRequested += ShowWorkbench;
        _tray.ShowSettingsRequested  += ShowSettings;
        _tray.ExitRequested          += ExitApp;

        _hotkey = new HotkeyManager();
        bool hotkeyOk = _hotkey.Initialize(
            settings.Current.HotkeyModifiers, settings.Current.HotkeyVirtualKey);
        if (!hotkeyOk)
            Log.Warn("Global hotkey registration failed (combination in use by another app)");
        _hotkey.HotkeyPressed += TogglePanel;

        // Selection popup: a mouse hook spots select-like gestures anywhere
        // on the desktop and offers a "copy to KeeStashDock" bubble beside
        // the selection.
        _selectionWatcher = new SelectionWatcher();
        _selectionPopup = new SelectionPopupWindow(settings);
        _translationCard = new TranslationResultWindow(settings);
        MiniToast.Settings = settings;
        DirectPaste.Settings = settings;
        _clipboardWatcher = new ClipboardWatcher(
            settings, _services.GetRequiredService<SnippetService>());
        _selectionWatcher.SelectionLikely += (x, y) =>
        {
            if (!settings.Current.SelectionPopupEnabled) return;
            (_lastSelectionX, _lastSelectionY) = (x, y);
            _translationCard.HideCard();   // a new selection retires old results
            _selectionPopup.ShowAt(x, y);
        };
        _selectionWatcher.AnyMouseDown += (_, _) =>
        {
            _selectionPopup.HideBubble();
            _translationCard.HideCard();
        };
        _selectionPopup.CaptureRequested += CaptureSelectionAsync;
        _selectionPopup.TranslateRequested += TranslateSelectionAsync;
        ApplySelectionPopupSetting(settings);
        settings.Changed += () => ApplySelectionPopupSetting(settings);

        Log.Info("Startup complete (stowed state)");
    }

    // ------------------------------------------------------------ windows

    /// <summary>Lazily creates the quick panel on first summon (fast cold start).</summary>
    private QuickPanelWindow Panel
    {
        get
        {
            _panel ??= new QuickPanelWindow(
                _services.GetRequiredService<SnippetService>(),
                _services.GetRequiredService<SearchService>(),
                _services.GetRequiredService<SettingsStore>(),
                _services.GetRequiredService<CategoryRepository>(),
                onExpandRequested: ShowWorkbench,
                onSettingsRequested: ShowSettings);
            return _panel;
        }
    }

    private SelectionWatcher _selectionWatcher = null!;
    private ClipboardWatcher? _clipboardWatcher;
    private SelectionPopupWindow _selectionPopup = null!;
    private TranslationResultWindow _translationCard = null!;
    private int _lastSelectionX, _lastSelectionY;
    private bool? _selectionPopupApplied;

    private void ApplySelectionPopupSetting(SettingsStore settings)
    {
        bool enable = settings.Current.SelectionPopupEnabled;
        if (_selectionPopupApplied == enable) return;
        _selectionPopupApplied = enable;
        if (enable) _selectionWatcher.Start();
        else { _selectionWatcher.Stop(); _selectionPopup.HideBubble(); }
    }

    /// <summary>One-shot starter content (runs after migrations, before any
    /// UI): a localized "Default" group plus a "Git BASH" group pre-filled
    /// with high-frequency git one-liners (untitled on purpose - the command
    /// itself is the card title, and commands are language-neutral).
    /// Guarded by a settings flag, NOT by "table is empty": a user who
    /// deletes the starter groups must never see them respawn. Name-checked
    /// too, so upgrading users who already made a same-named group don't get
    /// duplicates.</summary>
    private void SeedStarterGroups(SettingsStore settings)
    {
        if (settings.Current.DefaultGroupsSeeded) return;
        try
        {
            var cats = _services.GetRequiredService<CategoryRepository>();
            var existing = cats.GetAll().Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string defName = LocalizationService.Instance["Seed.DefaultGroup"];
            if (!existing.Contains(defName))
                cats.Insert(new Domain.Entities.Category
                { Name = defName, Icon = "\uE8B7", SortOrder = 0 });

            if (!existing.Contains("Git BASH"))
            {
                long gitId = cats.Insert(new Domain.Entities.Category
                { Name = "Git BASH", Icon = "\uE756", SortOrder = 1 });

                var snippets = _services.GetRequiredService<SnippetService>();
                string[] gitCmds =
                {
                    "git status",
                    "git add .",
                    "git commit -m \"update\"",
                    "git push",
                    "git pull --rebase",
                    "git log --oneline -10",
                    "git branch -a",
                    "git checkout -b feature/",
                    "git stash",
                    "git stash pop",
                    "git diff",
                    "git reset --hard HEAD",
                    "git cherry-pick ",
                    "git remote -v",
                };
                // Reversed so the list shows them in reading order (the
                // panel sorts by recency, newest first).
                foreach (string cmd in gitCmds.Reverse())
                    snippets.Add(cmd, Domain.Entities.SnippetSource.Import,
                                 categoryId: gitId);
            }

            settings.Current.DefaultGroupsSeeded = true;
            settings.Save();
            Log.Info("Starter groups seeded (Default + Git BASH)");
        }
        catch (Exception ex)
        {
            // Seeding is convenience, never a startup blocker.
            Log.Warn($"Starter-group seeding failed: {ex.Message}");
        }
    }

    /// <summary>Bubble clicked: synthesize Ctrl+C into the still-focused
    /// source app, VERIFY via the clipboard sequence number that a copy
    /// actually happened (the gesture heuristic can misfire on plain drags),
    /// wash the formatting off, stash into the panel's current group.
    ///
    /// Rich-text hardening (bug: browser PAGE selections toasted "no
    /// selection" while input-box text worked). Two distinct failure modes
    /// were being conflated:
    ///   1. Browsers write HTML + RTF + text and can take well over the old
    ///      600 ms - the wait window is now 2 s with early exit;
    ///   2. worse, the sequence number often bumps while the source app
    ///      STILL HOLDS the clipboard open for the remaining formats, so the
    ///      first GetText() threw CLIPBRD_E_CANT_OPEN, was caught into "",
    ///      and misreported as "no selection". Reading now retries, and if a
    ///      copy verifiably happened (seq changed) but text stays
    ///      unreachable, falls back to the CF_HTML fragment stripped to
    ///      plain text; only then does it report "capture failed" - a
    ///      DIFFERENT message from "no selection".</summary>
    private async void CaptureSelectionAsync()
    {
        string? text = await GrabSelectionTextAsync();
        if (text is null) return;   // failure toasts already shown

        // Wash formatting: clipboard now holds ONLY plain text.
        try { Clipboard.SetDataObject(text, copy: true); } catch { }

        long? category = _panel?.CurrentCategoryId;
        long id = _services.GetRequiredService<SnippetService>()
            .Add(text, Domain.Entities.SnippetSource.Clipboard, categoryId: category);
        if (category is long cat)
            _services.GetRequiredService<SnippetService>().AssignCategory(id, cat);

        MiniToast.Show(LocalizationService.Instance["Toast.Captured"]);
    }

    /// <summary>Selection translation grabs the selection with the
    /// same Ctrl+C flow as capture, then shows the glass result card in an
    /// immediate "translating..." state so the click always visibly lands,
    /// swapping in the translation (or a calm error) when the service
    /// answers. Nothing is stored and the clipboard is NOT washed - the
    /// user asked to READ this text, not to keep it.</summary>
    private async void TranslateSelectionAsync()
    {
        string? text = await GrabSelectionTextAsync();
        if (text is null) return;   // failure toasts already shown

        _translationCard.ShowTranslating(_lastSelectionX, _lastSelectionY);
        var cfg = _services.GetRequiredService<SettingsStore>().Current;
        string? translated = await TranslationService.TranslateAsync(
            text, cfg.Language, cfg.TranslationEngine, cfg.TranslationApiKey);

        if (!_translationCard.IsVisible) return;   // dismissed while waiting
        if (translated is null) _translationCard.ShowError();
        else _translationCard.ShowResult(translated);
    }

    /// <summary>Synthesizes Ctrl+C and reads the selection as plain text.
    /// Null (with the appropriate toast already shown) when nothing was
    /// selected or nothing text-like could be read.</summary>
    private static async Task<string?> GrabSelectionTextAsync()
    {
        uint seqBefore = NativeMethods.GetClipboardSequenceNumber();

        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        bool copied = false;
        for (int i = 0; i < 50; i++)                  // up to ~2 s, exits early
        {
            await Task.Delay(40);
            if (NativeMethods.GetClipboardSequenceNumber() != seqBefore)
            { copied = true; break; }
        }
        if (!copied)
        {
            // Sequence never moved: nothing was selected (or the page blocks
            // copying) - genuinely "no selection".
            MiniToast.Show(LocalizationService.Instance["Toast.NoSelection"]);
            return null;
        }

        // A copy DID happen. Read with retries: the source may still hold
        // the clipboard open while writing its remaining formats.
        string text = string.Empty;
        for (int i = 0; i < 12 && text.Length == 0; i++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data?.GetData(DataFormats.UnicodeText) is string t &&
                    !string.IsNullOrWhiteSpace(t))
                {
                    text = t;
                    break;
                }
                if (data is not null)
                {
                    // Formats are readable but none is plain text: some web
                    // apps put ONLY HTML on the clipboard - strip it down.
                    if (data.GetDataPresent(DataFormats.Html) &&
                        data.GetData(DataFormats.Html) is string html)
                    {
                        string stripped = HtmlClipboardToPlainText(html);
                        if (!string.IsNullOrWhiteSpace(stripped))
                        {
                            text = stripped;
                            break;
                        }
                    }
                    if (!data.GetDataPresent(DataFormats.UnicodeText))
                        break;   // image/file-only clipboard: retrying won't help
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            { /* clipboard transiently locked by the source app - retry */ }
            await Task.Delay(60);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // The copy happened but yielded nothing text-like - tell the
            // truth instead of blaming the (real) selection.
            MiniToast.Show(LocalizationService.Instance["Toast.CaptureFailed"]);
            return null;
        }

        return text;
    }

    /// <summary>Last-resort plain text from a CF_HTML clipboard payload:
    /// takes the StartFragment..EndFragment slice, turns the common block
    /// terminators into newlines, drops every remaining tag, and decodes
    /// entities. Not a general HTML renderer - good enough for the "web app
    /// copied HTML only" fallback path.</summary>
    private static string HtmlClipboardToPlainText(string cfHtml)
    {
        string html = cfHtml;
        int start = cfHtml.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end = cfHtml.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            html = cfHtml[(start + "<!--StartFragment-->".Length)..end];

        html = System.Text.RegularExpressions.Regex.Replace(
            html, @"<(br|/p|/div|/li|/tr|/h[1-6])\s*/?>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(
            html, @"<script[\s\S]*?</script>|<style[\s\S]*?</style>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        html = System.Net.WebUtility.HtmlDecode(html);
        html = System.Text.RegularExpressions.Regex.Replace(html, "[ \\t]+\\n", "\n");
        html = System.Text.RegularExpressions.Regex.Replace(html, "\\n{3,}", "\n\n");
        return html.Trim();
    }

    private void TogglePanel()
    {
        if (Panel.IsVisible) Panel.HidePanel();
        else
        {
            // Whatever the user was working in right now is where a card
            // click should paste back to.
            DirectPaste.RememberForeground();
            Panel.ShowPanel();
        }
    }

    private void ShowWorkbench()
    {
        _workbench ??= new WorkbenchWindow(
            _services.GetRequiredService<SnippetService>(),
            _services.GetRequiredService<SearchService>(),
            _services.GetRequiredService<CategoryRepository>(),
            _services.GetRequiredService<TagRepository>(),
            _services.GetRequiredService<SettingsStore>(),
            _services.GetRequiredService<GroupIoService>());
        _workbench.ShowAndActivate();
    }

    private void ShowSettings()
    {
        _settingsWin ??= new SettingsWindow(
            _services.GetRequiredService<SettingsStore>(),
            _services.GetRequiredService<BackupService>(),
            _hotkey);
        _settingsWin.ShowAndActivate();
    }

    private void ExitApp()
    {
        Log?.Info("Exit requested from tray");
        Shutdown();
    }

    // ---------------------------------------------------------- lifecycle

    protected override void OnExit(ExitEventArgs e)
    {
        _clipboardWatcher?.Dispose();
        _selectionWatcher?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        (_services?.GetService<HousekeepingService>())?.Dispose();
        _instance?.Dispose();
        Log?.Info("Shutdown");
        Log?.Dispose();
        base.OnExit(e);
    }

    // ------------------------------------------------------ crash handling

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log?.Error("Unhandled UI exception", e.Exception);
        Log?.Flush();
        var loc = LocalizationService.Instance;
        // Friendly dialog instead of the default silent process death; drafts
        // are already on disk thanks to DraftGuard, so "continue" is safe.
        var result = MessageBox.Show(
            loc["Error.Crash.Message"], loc["Error.Crash.Title"],
            MessageBoxButton.YesNo, MessageBoxImage.Error);
        e.Handled = result == MessageBoxResult.Yes;   // Yes = try to continue
        if (!e.Handled) Shutdown();
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Log?.Error("Fatal non-UI exception", e.ExceptionObject as Exception);
        Log?.Flush();
    }
}
