using System.Windows;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop.Views;

internal sealed record ComponentInstallerActions(
    Func<ComponentKind, ComponentStatus> Status,
    Func<ComponentKind, IProgress<ComponentInstallProgress>, CancellationToken, Task<ComponentInstallResult>> Install,
    Func<CancellationToken, Task<ComponentInstallResult>> Start,
    Func<CancellationToken, Task<ComponentInstallResult>> Stop);

public partial class SettingsWindow
{
    private ComponentInstallerActions _components = null!;
    private ComponentInstallService? _componentService;
    private CancellationTokenSource? _componentCancellation;
    private Task _componentOperation = Task.CompletedTask;

    private void ConfigureComponents(ComponentInstallerActions? actions)
    {
        if (actions is null)
        {
            var service = _componentService = new ComponentInstallService(_settings);
            actions = new(service.GetStatus,
                (kind, progress, token) => service.InstallAsync(kind, progress, token),
                service.StartOfflineAsync, service.StopOfflineAsync);
        }
        _components = actions;
        RefreshComponents();
    }

    private void RefreshComponents()
    {
        var loc = LocalizationService.Instance;
        ComponentStatus ocr = _components.Status(ComponentKind.Ocr);
        ComponentStatus offline = _components.Status(ComponentKind.OfflineTranslation);
        OcrComponentStatus.Text = loc[$"Components.Status.{ocr}"];
        OfflineComponentStatus.Text = loc[$"Components.Status.{offline}"];
        bool idle = _componentCancellation is null;
        bool canRun = idle && _folderCancellation is null;
        OcrSetupButton.IsEnabled = canRun;
        OfflineSetupButton.IsEnabled = canRun;
        OfflineStartButton.IsEnabled = canRun && offline == ComponentStatus.Ready;
        OfflineStopButton.IsEnabled = canRun && offline == ComponentStatus.Ready;
        ComponentCancelButton.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        ComponentProgress.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        ComponentOperationPanel.Visibility = idle && string.IsNullOrWhiteSpace(ComponentOperationStatus.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        RefreshFolderAvailability();
    }

    private async void OnOcrSetupClick(object sender, RoutedEventArgs e)
        => await InstallComponentAsync(ComponentKind.Ocr);

    private async void OnOfflineSetupClick(object sender, RoutedEventArgs e)
        => await InstallComponentAsync(ComponentKind.OfflineTranslation);

    private async void OnOfflineStartClick(object sender, RoutedEventArgs e)
        => await RunComponentOperationAsync(_components.Start);

    private async void OnOfflineStopClick(object sender, RoutedEventArgs e)
        => await RunComponentOperationAsync(_components.Stop);

    private void OnCancelComponentClick(object sender, RoutedEventArgs e)
        => _componentCancellation?.Cancel();

    internal Task InstallComponentAsync(ComponentKind kind)
        => RunComponentOperationAsync(token => _components.Install(kind,
            new Progress<ComponentInstallProgress>(progress =>
            {
                if (_componentCancellation is not null && _componentCancellation.Token == token &&
                    !token.IsCancellationRequested)
                    ComponentOperationStatus.Text = LocalizationService.Instance[$"Components.Phase.{progress.Phase}"];
            }), token));

    private Task RunComponentOperationAsync(Func<CancellationToken, Task<ComponentInstallResult>> operation)
    {
        if (_componentCancellation is not null) return _componentOperation;
        if (_folderCancellation is not null) return _folderOperation;
        var request = new CancellationTokenSource();
        _componentCancellation = request;
        ComponentOperationStatus.Text = LocalizationService.Instance["Components.Phase.Checking"];
        ComponentDiagnostic.Text = string.Empty;
        ComponentDiagnostic.Visibility = Visibility.Collapsed;
        RefreshComponents();
        return _componentOperation = CompleteComponentOperationAsync(operation, request);
    }

    private async Task CompleteComponentOperationAsync(
        Func<CancellationToken, Task<ComponentInstallResult>> operation, CancellationTokenSource request)
    {
        try
        {
            ComponentInstallResult result = await operation(request.Token);
            if (request.IsCancellationRequested) result = new(ComponentInstallFailure.Cancelled);
            ComponentOperationStatus.Text = LocalizationService.Instance[
                result.IsSuccess ? "Components.Completed" : $"Components.Failure.{result.Failure}"];
            if (!result.IsSuccess && result.Failure != ComponentInstallFailure.Cancelled && !string.IsNullOrWhiteSpace(result.Detail))
            {
                ComponentDiagnostic.Text = result.Detail;
                ComponentDiagnostic.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            ComponentOperationStatus.Text = LocalizationService.Instance["Components.Failure.Cancelled"];
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ComponentOperationStatus.Text = LocalizationService.Instance["Components.Failure.Failed"];
            ComponentDiagnostic.Text = exception.Message;
            ComponentDiagnostic.Visibility = Visibility.Visible;
        }
        finally
        {
            _componentCancellation = null;
            request.Dispose();
            RefreshComponents();
        }
    }

    /// <summary>Exit, restart and database replacement wait for the app-owned setup worker.
    /// An elevated Windows installer may finish independently after cancellation.</summary>
    public Task CancelComponentOperationAsync()
    {
        _componentCancellation?.Cancel();
        _folderCancellation?.Cancel();
        return _folderOperation.IsCompleted ? _componentOperation : Task.WhenAll(_componentOperation, _folderOperation);
    }
}
