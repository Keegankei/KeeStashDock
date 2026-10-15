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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == PackageVerification.Argument)
        {
            Shutdown(await PackageVerification.RunAsync(e.Args));
            return;
        }
        LocalizationService.Instance.SetLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name);
        if (await DesktopUpdateCoordinator.RunHelperIfRequestedAsync(e.Args))
        {
            Shutdown(Environment.ExitCode);
            return;
        }
        if (!DesktopUpdateCoordinator.CheckRecoveryBeforeStartup(e.Args))
        {
            Shutdown();
            return;
        }

        if (!DesktopApplicationRestarter.WaitForPreviousInstance(e.Args))
        {
            Shutdown();
            return;
        }

        // ---- 1. Single instance -----------------------------------------
        _instance = new SingleInstance();
        if (!_instance.TryClaim())
        {
            Shutdown();                      // existing instance was notified
            return;
        }
        _instance.ActivationRequested += () => Dispatcher.Invoke(ShowPanel);

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
        _tray.CheckUpdatesRequested += ShowUpdates;
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

        InitializeSelectionServices(settings);

        Log.Info("Startup complete (stowed state)");
        DesktopUpdateCoordinator.AcknowledgeStartup(e.Args);
        if (await DesktopUpdateCoordinator.WaitForCompletionAsync(e.Args, _shutdownCancellation.Token))
            _tray.ShowInfo("KeeStashDock", LocalizationService.Instance["Updates.Completed"]);
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

    private void TogglePanel()
    {
        if (_exitPending) return;
        if (_capturingScreenshot) return;
        if (Panel.IsVisible && !Panel.IsEdgeDockHidden) Panel.HidePanel();
        else ShowPanel();
    }

    private void ShowPanel()
    {
        if (_exitPending) return;
        // A second executable launch summons the panel; it must not hide an
        // already visible panel or replace its remembered direct-paste target.
        if (_capturingScreenshot) return;
        if (!Panel.IsVisible) DirectPaste.RememberForeground();
        Panel.ShowPanel();
    }

    private void ShowWorkbench()
    {
        if (_exitPending) return;
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
        if (_exitPending) return;
        if (_settingsWin is null)
        {
            _settingsWin = new SettingsWindow(
                _services.GetRequiredService<SettingsStore>(),
                _services.GetRequiredService<BackupService>(),
                _services.GetRequiredService<DatabaseImportService>(),
                _services.GetRequiredService<IApplicationRestarter>(),
                _hotkey, _screenshotHotkey, prepareForShutdown: PrepareForShutdownAsync);
            _settingsWin.CheckUpdatesRequested += ShowUpdates;
        }
        _settingsWin.ShowAndActivate();
    }

    private void ShowComponentSettings()
    {
        ShowSettings();
        _settingsWin?.ShowComponents();
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
        _updateWindow?.CancelOperation();
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
