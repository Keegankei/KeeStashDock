using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Updates;

namespace KeeStashDock.Desktop.Views;

/// <summary>Explicit, cancellable release discovery and staging. Installation is a separate user action.</summary>
internal sealed class UpdateWindow : Window
{
    private readonly ReleaseUpdateService _service;
    private readonly bool _ownsService;
    private readonly string _installationDirectory;
    private readonly string _stagingDirectory;
    private readonly Func<StagedUpdate, Task<bool>> _restart;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 10) };
    private readonly TextBox _notes = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private readonly ProgressBar _progress = new() { Height = 5, Minimum = 0, Maximum = 100, Margin = new(0, 10, 0, 10) };
    private readonly Button _check = new(), _download = new(), _install = new(), _cancel = new(), _release = new();
    private CancellationTokenSource? _request;
    private ReleaseUpdateInfo? _available;
    private StagedUpdate? _staged;
    private bool _closed;
    private bool _installing;
    private bool _checked;
    private bool _restartAttempted;
    private Task _stageCleanup = Task.CompletedTask;
    private string _statusKey = "Updates.Check";

    internal UpdateWindow(Func<StagedUpdate, Task<bool>> restart,
        ReleaseUpdateService? service = null, string? installationDirectory = null, string? stagingDirectory = null)
    {
        _restart = restart;
        _ownsService = service is null;
        _service = service ?? new ReleaseUpdateService();
        _installationDirectory = Path.GetFullPath(installationDirectory ?? AppContext.BaseDirectory);
        _stagingDirectory = Path.GetFullPath(stagingDirectory ?? DesktopUpdateCoordinator.UpdatesDirectory);
        NameScope.SetNameScope(this, new NameScope());
        _status.Name = "UpdateStatus"; _notes.Name = "ReleaseNotes";
        _check.Name = "CheckUpdateButton"; _download.Name = "DownloadUpdateButton";
        _install.Name = "InstallUpdateButton"; _cancel.Name = "CancelUpdateButton"; _release.Name = "OpenReleaseButton";
        foreach (FrameworkElement element in new FrameworkElement[] { _status, _notes, _check, _download, _install, _cancel, _release })
            RegisterName(element.Name, element);
        Width = 620; Height = 520; MinWidth = 460; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "B.PanelBg");
        SetResourceReference(ForegroundProperty, "B.TextPrimary");
        var body = new DockPanel { Margin = new(22) };
        var heading = new StackPanel();
        var version = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold };
        var target = new TextBlock { Name = "UpdateTarget", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        RegisterName(target.Name, target);
        heading.Children.Add(version); heading.Children.Add(target); heading.Children.Add(_status); heading.Children.Add(_progress);
        DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        var buttons = new WrapPanel { Margin = new(0, 14, 0, 0) };
        foreach (var button in new[] { _check, _download, _install, _cancel, _release })
        {
            button.SetResourceReference(StyleProperty, "S.Button");
            button.Margin = new(0, 0, 8, 8); button.Padding = new(12, 7, 12, 7);
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        body.Children.Add(_notes); Content = body;
        _check.Click += async (_, _) => await CheckAsync();
        _download.Click += async (_, _) => await DownloadAsync();
        _cancel.Click += (_, _) => _request?.Cancel();
        _install.Click += async (_, _) =>
        {
            if (_staged is null || _installing) return;
            if (MessageBox.Show(this, L("Updates.Confirm") + "\n\n" + _installationDirectory,
                L("Updates.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            _installing = true; SetStatus("Updates.Preparing"); Refresh();
            try
            {
                // The helper may retain transaction references even when startup
                // fails. Once passed across this boundary, recovery owns it.
                _restartAttempted = true;
                if (!await _restart(_staged)) SetStatus("Updates.HelperFailed");
            }
            catch (Exception ex) { ShowFailure(ex); }
            finally { _installing = false; Refresh(); }
        };
        _release.Click += (_, _) => OpenRelease();
        Loaded += async (_, _) => { if (!_checked) { _checked = true; await CheckAsync(); } };
        Closing += (_, e) => { if (_installing) e.Cancel = true; else _request?.Cancel(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _ = DiscardUnattemptedStageAsync();
            if (_ownsService) _service.Dispose();
            LocalizationService.Instance.LanguageChanged -= Relocalize;
        };
        LocalizationService.Instance.LanguageChanged += Relocalize;
        ThemeManager.TrackWindowChrome(this);
        Relocalize();

        void Relocalize()
        {
            Title = L("Updates.Title"); LocalizationService.Instance.ApplyFlowDirection(this);
            version.Text = string.Format(L("Updates.Current"), DesktopUpdateCoordinator.CurrentVersion);
            target.Text = string.Format(L("Updates.Target"), _installationDirectory);
            _check.Content = L("Updates.Check"); _download.Content = L("Updates.Download");
            _install.Content = L("Updates.Restart"); _cancel.Content = L("Updates.Cancel"); _release.Content = L("Updates.OpenRelease");
            if (_available is { FromReleasePage: true }) _notes.Text = L("Updates.ReleasePageNotes");
            RenderStatus(); Refresh();
        }
    }

    internal void CancelOperation() => _request?.Cancel();
    internal bool HasPendingWork => _request is not null || !_stageCleanup.IsCompleted;
    private static string L(string key) => LocalizationService.Instance[key];
    private void SetStatus(string key) { _statusKey = key; RenderStatus(); }
    private void RenderStatus()
    {
        _status.Text = _statusKey == "Updates.Available" && _available is not null
            ? (_available.ArchiveSize is long size
                ? string.Format(L(_statusKey), _available.Version, (size / 1048576d).ToString("0.0"))
                : string.Format(L("Updates.AvailableUnknownSize"), _available.Version))
            : L(_statusKey);
        _check.Content = L(_statusKey is "Updates.Network" or "Updates.RateLimited" or "Updates.Failed"
            or "Updates.MissingAssets" or "Updates.Invalid" or "Updates.Cancelled" or "Updates.HelperFailed"
            ? "Updates.Retry" : "Updates.Check");
    }
    private void Refresh()
    {
        bool idle = _request is null && !_installing;
        _check.IsEnabled = idle;
        _download.IsEnabled = idle && _available is not null && _staged is null
            && File.Exists(Path.Combine(_installationDirectory, UpdatePackageManifest.FileName));
        _install.IsEnabled = idle && _staged is not null;
        _cancel.Visibility = _request is null ? Visibility.Collapsed : Visibility.Visible;
        _progress.Visibility = _request is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CheckAsync()
    {
        if (_closed || _installing || _request is not null) return;
        using var request = new CancellationTokenSource(); _request = request;
        _available = null; _notes.Text = "";
        SetStatus("Updates.Checking"); _progress.IsIndeterminate = true; Refresh();
        try
        {
            await DiscardUnattemptedStageAsync();
            request.Token.ThrowIfCancellationRequested();
            ReleaseUpdateInfo? available = await _service.CheckAsync(DesktopUpdateCoordinator.CurrentVersion, request.Token,
                new Progress<UpdateCheckPhase>(_ =>
                {
                    if (!_closed && !request.IsCancellationRequested && ReferenceEquals(_request, request))
                        SetStatus("Updates.CheckingReleasePage");
                }));
            request.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            _available = available;
            SetStatus(_available is null ? "Updates.UpToDate" : "Updates.Available");
            if (_available is not null)
            {
                _notes.Text = _available.FromReleasePage ? L("Updates.ReleasePageNotes") : _available.Notes;
                if (!File.Exists(Path.Combine(_installationDirectory, UpdatePackageManifest.FileName)))
                    SetStatus("Updates.Manual");
            }
        }
        catch (OperationCanceledException) { if (!_closed) SetStatus("Updates.Cancelled"); }
        catch (Exception ex) { if (!_closed) ShowFailure(ex); }
        finally { _request = null; if (!_closed) Refresh(); }
    }

    private async Task DownloadAsync()
    {
        if (_closed || _installing || _available is null || _request is not null) return;
        using var request = new CancellationTokenSource(); _request = request;
        SetStatus("Updates.Downloading"); _progress.IsIndeterminate = false; _progress.Value = 0; Refresh();
        try
        {
            StagedUpdate staged = await _service.DownloadAsync(_available, _stagingDirectory,
                new Progress<double>(value =>
                {
                    if (!_closed && !request.IsCancellationRequested && ReferenceEquals(_request, request))
                        _progress.Value = value * 100;
                }), request.Token);
            if (_closed || request.IsCancellationRequested)
            {
                await DiscardStageAsync(staged);
                request.Token.ThrowIfCancellationRequested();
                return;
            }
            _staged = staged;
            _restartAttempted = false;
            SetStatus("Updates.Ready");
        }
        catch (OperationCanceledException) { if (!_closed) SetStatus("Updates.Cancelled"); }
        catch (Exception ex) { if (!_closed) ShowFailure(ex); }
        finally { _request = null; if (!_closed) Refresh(); }
    }

    private Task DiscardUnattemptedStageAsync()
    {
        StagedUpdate? staged = _staged;
        bool retainForRecovery = _restartAttempted;
        _staged = null;
        _restartAttempted = false;
        return staged is not null && !retainForRecovery ? DiscardStageAsync(staged) : Task.CompletedTask;
    }

    private Task DiscardStageAsync(StagedUpdate stage) => _stageCleanup = Task.Run(() =>
        ReleaseUpdateService.DiscardStage(stage, _stagingDirectory));

    private void ShowFailure(Exception ex)
    {
        App.Log?.Warn("Update operation failed: " + ex.GetType().Name);
        string key = ex is UpdateException failure ? failure.Failure switch
        {
            UpdateFailure.Network => "Updates.Network",
            UpdateFailure.RateLimited => "Updates.RateLimited",
            UpdateFailure.MissingAssets => "Updates.MissingAssets",
            UpdateFailure.InvalidMetadata or UpdateFailure.UnsupportedVersion or UpdateFailure.UnsafePackage
                or UpdateFailure.ChecksumMismatch => "Updates.Invalid",
            _ => "Updates.Failed"
        } : "Updates.Failed";
        SetStatus(key);
    }

    private void OpenRelease()
    {
        try { Process.Start(new ProcessStartInfo(ReleaseUpdateService.LatestReleaseUrl) { UseShellExecute = true }); }
        catch (Exception ex) { ShowFailure(ex); }
    }
}
