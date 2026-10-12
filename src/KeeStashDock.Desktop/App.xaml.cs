using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeeStashDock.Domain.Entities;
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
    private HotkeyManager _screenshotHotkey = null!;
    private readonly ScreenshotCaptureService _screenshotCapture = new();
    private readonly LocalOcrService _localOcr = new();
    private readonly Dictionary<long, ImageEntryWindow> _imageWindows = new();
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private bool _capturingScreenshot;
    private bool _preparingShutdown;
    private TextBox? _internalSelectionSource;

    private QuickPanelWindow? _panel;
    private WorkbenchWindow? _workbench;
    private bool _exitPending;
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
        sc.AddSingleton<DatabaseMaintenanceCoordinator>();
        sc.AddSingleton<Db>();
        sc.AddSingleton<MigrationRunner>();
        sc.AddSingleton<DatabaseSchemaValidator>();
        sc.AddSingleton<SnippetRepository>();
        sc.AddSingleton<CategoryRepository>();
        sc.AddSingleton<TagRepository>();
        sc.AddSingleton<GroupIoService>();
        sc.AddSingleton<SettingsStore>();
        sc.AddSingleton<BackupService>();
        sc.AddSingleton<IDatabaseFileOperations, PhysicalDatabaseFileOperations>();
        sc.AddSingleton<IApplicationRestarter, DesktopApplicationRestarter>();
        sc.AddSingleton<DatabaseImportService>();
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
        _tray.ShowPanelRequested += TogglePanel;
        _tray.ShowWorkbenchRequested += ShowWorkbench;
        _tray.ShowSettingsRequested += ShowSettings;
        _tray.ExitRequested += ExitApp;
        _tray.CaptureRequested += CaptureScreenshot;

        _hotkey = new HotkeyManager();
        bool hotkeyOk = _hotkey.Initialize(
            settings.Current.HotkeyModifiers, settings.Current.HotkeyVirtualKey);
        if (!hotkeyOk)
            Log.Warn("Global hotkey registration failed (combination in use by another app)");
        _hotkey.HotkeyPressed += TogglePanel;
        _screenshotHotkey = new HotkeyManager();
        bool screenshotHotkeyOk = settings.Current.ScreenshotHotkeyEnabled
            ? _screenshotHotkey.Initialize(settings.Current.ScreenshotHotkeyModifiers, settings.Current.ScreenshotHotkeyVirtualKey)
            : _screenshotHotkey.Initialize(0, 0);
        if (!screenshotHotkeyOk)
            _tray.ShowInfo("KeeStashDock", LocalizationService.Instance["Screenshot.HotkeyConflict"]);
        _screenshotHotkey.HotkeyPressed += CaptureScreenshot;

        // Selection popup: a mouse hook spots select-like gestures anywhere
        // on the desktop and offers a "copy to KeeStashDock" bubble beside
        // the selection.
        _selectionWatcher = new SelectionWatcher();
        _selectionPopup = new SelectionPopupWindow(settings);
        _translationCard = new TranslationResultWindow(settings);
        _translationCard.IsVisibleChanged += (_, _) =>
        {
            if (!_translationCard.IsVisible) _translationCancellation?.Cancel();
        };
        MiniToast.Settings = settings;
        DirectPaste.Settings = settings;
        DirectPaste.StartTracking();
        _clipboardWatcher = new ClipboardWatcher(
            settings, _services.GetRequiredService<SnippetService>());
        _selectionWatcher.SelectionLikely += (x, y) =>
        {
            if (_capturingScreenshot) return;
            _internalSelectionSource = null;
            _translationCancellation?.Cancel();
            if (!settings.Current.SelectionPopupEnabled) return;
            (_lastSelectionX, _lastSelectionY) = (x, y);
            _translationCard.HideCard();   // a new selection retires old results
            _selectionPopup.ShowAt(x, y);
        };
        _selectionWatcher.AnyMouseDown += (_, _) =>
        {
            _internalSelectionSource = null;
            _translationCancellation?.Cancel();
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
                onSettingsRequested: ShowSettings,
                onCaptureRequested: () => CaptureScreenshotFrom(_panel?.CurrentCategoryId, _panel?.CurrentExpirePolicy ?? ExpirePolicy.Never),
                onImageRequested: ShowImageEntry);
            return _panel;
        }
    }

    private SelectionWatcher _selectionWatcher = null!;
    private ClipboardWatcher? _clipboardWatcher;
    private SelectionPopupWindow _selectionPopup = null!;
    private TranslationResultWindow _translationCard = null!;
    private CancellationTokenSource? _translationCancellation;
    private int _lastSelectionX, _lastSelectionY;
    private bool? _selectionPopupApplied;

    private void ApplySelectionPopupSetting(SettingsStore settings)
    {
        bool enable = settings.Current.SelectionPopupEnabled && !_capturingScreenshot;
        if (!enable)
        {
            _translationCancellation?.Cancel();
            _translationCard.HideCard();
        }
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
            string defName = LocalizationService.Instance["Seed.DefaultGroup"];
            if (cats.FindByName(defName) is null)
                cats.Insert(new Domain.Entities.Category
                { Name = defName, Icon = "\uE8B7", SortOrder = 0 });

            if (cats.FindByName("Git BASH") is null)
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
            Log?.Info("Starter groups seeded (Default + Git BASH)");
        }
        catch (Exception ex)
        {
            // Seeding is convenience, never a startup blocker.
            Log?.Warn($"Starter-group seeding failed: {ex.Message}");
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
        string? text = await GetSelectedTextAsync();
        if (text is null) return;   // failure toasts already shown

        // Wash formatting: clipboard now holds ONLY plain text.
        try { ClipboardWriteGuard.Shared.Write(() => Clipboard.SetDataObject(text, copy: true)); } catch { }

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
        _translationCancellation?.Cancel();
        using var request = new CancellationTokenSource();
        _translationCancellation = request;
        try
        {
            string? text = await GetSelectedTextAsync(request.Token);
            if (text is null || request.IsCancellationRequested) return;

            _translationCard.ShowTranslating(_lastSelectionX, _lastSelectionY);
            var cfg = _services.GetRequiredService<SettingsStore>().Current;
            TranslationResult result = await TranslationService.TranslateDetailedAsync(
                text, cfg, request.Token);

            if (request.IsCancellationRequested ||
                !ReferenceEquals(_translationCancellation, request) ||
                !_translationCard.IsVisible) return;
            if (result.IsSuccess)
            {
                _translationCard.ShowResult(result.Text!);
            }
            else if (result.Failure != TranslationFailure.Cancelled)
            {
                // Only diagnostic categories are logged, never text or credentials.
                Log?.Warn($"Translation failed: provider={result.Provider}, " +
                    $"failure={result.Failure}, http={result.HttpStatusCode}");
                _translationCard.ShowError(TranslationService.FailureMessageKey(result));
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // A changed selection or dismissed request has no result to show.
        }
        finally
        {
            if (ReferenceEquals(_translationCancellation, request))
                _translationCancellation = null;
        }
    }

    /// <summary>Synthesizes Ctrl+C and reads the selection as plain text.
    /// Null (with the appropriate toast already shown) when nothing was
    /// selected or nothing text-like could be read.</summary>
    private static async Task<string?> GrabSelectionTextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        uint seqBefore = NativeMethods.GetClipboardSequenceNumber();

        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        bool copied = false;
        for (int i = 0; i < 50; i++)                  // up to ~2 s, exits early
        {
            await Task.Delay(40, cancellationToken);
            if (NativeMethods.GetClipboardSequenceNumber() != seqBefore)
            { copied = true; break; }
        }
        if (!copied)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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
            await Task.Delay(60, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
    {
        TextBox? source = _internalSelectionSource;
        if (source is null) return GrabSelectionTextAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string? selected = source.IsVisible ? source.SelectedText : null;
        return Task.FromResult(string.IsNullOrWhiteSpace(selected) ? null : selected);
    }

    private void ShowInternalSelection(TextBox source)
    {
        var settings = _services.GetRequiredService<SettingsStore>().Current;
        if (!settings.SelectionPopupEnabled || _capturingScreenshot || string.IsNullOrWhiteSpace(source.SelectedText)) return;
        _translationCancellation?.Cancel();
        _translationCard.HideCard();
        _internalSelectionSource = source;
        Rect caret = source.GetRectFromCharacterIndex(source.SelectionStart + source.SelectionLength);
        Point local = caret.IsEmpty ? Mouse.GetPosition(source) : caret.BottomRight;
        Point point = source.PointToScreen(local);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        (_lastSelectionX, _lastSelectionY) = ((int)point.X, (int)point.Y);
        _selectionPopup.ShowAt(_lastSelectionX, _lastSelectionY);
    }

    private void DismissInternalSelection()
    {
        if (_internalSelectionSource is null) return;
        _internalSelectionSource = null;
        _translationCancellation?.Cancel();
        _selectionPopup.HideBubble();
        _translationCard.HideCard();
    }

    private void ShowImageEntry(long id)
    {
        if (_imageWindows.TryGetValue(id, out var existing))
        { existing.Show(); existing.Activate(); return; }
        try
        {
            var snippets = _services.GetRequiredService<SnippetService>();
            Snippet? model = snippets.GetById(id);
            byte[]? png = snippets.GetImage(id);
            if (model is null || png is null) { MiniToast.Show(LocalizationService.Instance["Screenshot.ImageUnavailable"]); return; }
            var window = new ImageEntryWindow(snippets, _localOcr, model, png);
            window.SelectionRequested += ShowInternalSelection;
            window.DismissActions += DismissInternalSelection;
            window.Closed += (_, _) => _imageWindows.Remove(id);
            _imageWindows.Add(id, window);
            window.Show();
            window.Activate();
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or ArgumentException or NotSupportedException)
        { MiniToast.Show(LocalizationService.Instance["Screenshot.ImageUnavailable"]); }
    }

    private void CaptureScreenshot()
        => CaptureScreenshotFrom(_workbench?.IsActive == true ? _workbench.CurrentCategoryId : _panel?.CurrentCategoryId,
            _workbench?.IsActive == true ? ExpirePolicy.Never : _panel?.CurrentExpirePolicy ?? ExpirePolicy.Never);

    private async void CaptureScreenshotFrom(long? categoryId, ExpirePolicy expire)
    {
        if (_capturingScreenshot || _preparingShutdown || _shutdownCancellation.IsCancellationRequested ||
            !_services.GetRequiredService<DatabaseMaintenanceCoordinator>().CanStartOperation) return;
        _capturingScreenshot = true;
        var settings = _services.GetRequiredService<SettingsStore>();
        var visible = Windows.OfType<Window>().Where(window => window.IsVisible &&
            window is not SelectionPopupWindow && window is not TranslationResultWindow).ToArray();
        nint previousForeground = NativeMethods.GetForegroundWindow();
        bool saved = false;
        _translationCancellation?.Cancel();
        _internalSelectionSource = null;
        _selectionPopup.HideBubble();
        _translationCard.HideCard();
        _selectionWatcher.Stop();
        try
        {
            foreach (Window window in visible) window.Hide();
            // Let the desktop compositor remove our windows before freezing the screen.
            await Task.Delay(180, _shutdownCancellation.Token);
            CapturedScreenshot? captured = await _screenshotCapture.CaptureAsync(_shutdownCancellation.Token);
            if (captured is null) return;
            byte[] preview = ImagePresentation.Thumbnail(captured.PngBytes);
            var snippets = _services.GetRequiredService<SnippetService>();
            long id = snippets.AddImage(captured.PngBytes, preview, categoryId, expire);
            if (categoryId is long group) snippets.AssignCategory(id, group);
            saved = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        { MiniToast.Show(LocalizationService.Instance["Screenshot.CaptureFailed"]); }
        finally
        {
            if (!_shutdownCancellation.IsCancellationRequested)
            {
                foreach (Window window in visible)
                    if (Windows.OfType<Window>().Contains(window))
                    {
                        if (window is QuickPanelWindow quickPanel) quickPanel.ShowPanel();
                        else window.Show();
                    }
                if (previousForeground != nint.Zero) NativeMethods.SetForegroundWindow(previousForeground);
                if (settings.Current.SelectionPopupEnabled) _selectionWatcher.Start();
                if (saved) { Panel.ShowPanel(); MiniToast.Show(LocalizationService.Instance["Screenshot.Saved"]); }
            }
            _capturingScreenshot = false;
        }
    }

    private void TogglePanel()
    {
        if (_capturingScreenshot) return;
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
            _services.GetRequiredService<GroupIoService>(),
            onImageRequested: ShowImageEntry,
            onInternalSelection: ShowInternalSelection,
            onDismissSelection: DismissInternalSelection,
            onCaptureRequested: () => CaptureScreenshotFrom(_workbench?.CurrentCategoryId, ExpirePolicy.Never));
        _workbench.ShowAndActivate();
    }

    private void ShowSettings()
    {
        _settingsWin ??= new SettingsWindow(
            _services.GetRequiredService<SettingsStore>(),
            _services.GetRequiredService<BackupService>(),
            _services.GetRequiredService<DatabaseImportService>(),
            _services.GetRequiredService<IApplicationRestarter>(),
            _hotkey, _screenshotHotkey, prepareForShutdown: PrepareForShutdownAsync);
        _settingsWin.ShowAndActivate();
    }

    private async Task<bool> PrepareForShutdownAsync()
    {
        if (_preparingShutdown || _capturingScreenshot) return false;
        _preparingShutdown = true;
        try
        {
            if (_settingsWin is not null) await _settingsWin.CancelComponentOperationAsync();
            foreach (var imageWindow in _imageWindows.Values.ToArray())
            {
                imageWindow.Close();
                if (_imageWindows.ContainsValue(imageWindow)) return false;
                await imageWindow.RecognitionCompletion;
            }
            if (_workbench is not null && !await _workbench.RequestHideAsync()) return false;
            _translationCancellation?.Cancel();
            _selectionPopup.HideBubble();
            _translationCard.HideCard();
            return true;
        }
        finally { _preparingShutdown = false; }
    }

    private async void ExitApp()
    {
        if (_exitPending) return;
        _exitPending = true;
        Log?.Info("Exit requested from tray");
        try
        {
            if (!await PrepareForShutdownAsync()) return;
            _shutdownCancellation.Cancel();
            Shutdown();
        }
        finally
        {
            _exitPending = false;
        }
    }

    // ---------------------------------------------------------- lifecycle

    protected override void OnExit(ExitEventArgs e)
    {
        _translationCancellation?.Cancel();
        DirectPaste.StopTracking();
        _clipboardWatcher?.Dispose();
        _selectionWatcher?.Dispose();
        _hotkey?.Dispose();
        _screenshotHotkey?.Dispose();
        _shutdownCancellation.Cancel();
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
