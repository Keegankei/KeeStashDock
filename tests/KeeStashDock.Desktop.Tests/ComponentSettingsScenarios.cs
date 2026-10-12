using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal sealed class ComponentSettingsScenarios
{
    private readonly Dictionary<ComponentKind, ComponentStatus> _status = new();
    private TaskCompletionSource<ComponentInstallResult> _pending = NewRequest();
    private CancellationToken _token;
    private IProgress<ComponentInstallProgress>? _progress;
    private int _installCount;
    private int _starts, _stops;

    internal ComponentInstallerActions Actions => new(
        kind => _status.GetValueOrDefault(kind),
        (_, progress, token) =>
        {
            _installCount++;
            _token = token;
            _progress = progress;
            return _pending.Task;
        },
        _ => { _starts++; return Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.None)); },
        _ => { _stops++; return Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.None)); });

    internal void Verify(SettingsWindow window)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { VerifyCore(window); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private void VerifyCore(SettingsWindow window)
    {
        Button install = Named<Button>(window, "OcrSetupButton");
        Button cancel = Named<Button>(window, "ComponentCancelButton");
        TextBlock status = Named<TextBlock>(window, "ComponentOperationStatus");
        Assert.IsTrue(install.IsEnabled, "Component setup must remain available when the selection bubble is disabled.");
        Assert.IsFalse(Named<Button>(window, "OfflineStartButton").IsEnabled);
        Task first = window.InstallComponentAsync(ComponentKind.Ocr);
        Assert.IsFalse(install.IsEnabled);
        Assert.AreEqual(Visibility.Visible, cancel.Visibility);
        Assert.AreEqual(Visibility.Visible, Named<ProgressBar>(window, "ComponentProgress").Visibility);
        _progress!.Report(new(ComponentInstallPhase.Downloading));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.AreEqual(LocalizationService.Instance["Components.Phase.Downloading"], status.Text);
        Assert.AreSame(first, window.InstallComponentAsync(ComponentKind.OfflineTranslation));
        Assert.AreEqual(1, _installCount, "Repeated clicks must not launch a second installer.");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsTrue(_token.IsCancellationRequested);
        _pending.SetResult(new(ComponentInstallFailure.None));
        PumpUntilCompleted(first);
        Assert.AreEqual(LocalizationService.Instance["Components.Failure.Cancelled"], status.Text,
            "A late success must not overwrite a cancellation.");
        Assert.IsTrue(install.IsEnabled);

        _pending = NewRequest();
        Task successful = window.InstallComponentAsync(ComponentKind.OfflineTranslation);
        _status[ComponentKind.OfflineTranslation] = ComponentStatus.Ready;
        _pending.SetResult(new(ComponentInstallFailure.None));
        PumpUntilCompleted(successful);
        Assert.AreEqual(LocalizationService.Instance["Components.Status.Ready"], Named<TextBlock>(window, "OfflineComponentStatus").Text);
        Assert.IsTrue(Named<Button>(window, "OfflineStartButton").IsEnabled);
        Named<Button>(window, "OfflineStartButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Named<Button>(window, "OfflineStopButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(1, _starts);
        Assert.AreEqual(1, _stops);

        _pending = NewRequest();
        Task failed = window.InstallComponentAsync(ComponentKind.Ocr);
        _pending.SetResult(new(ComponentInstallFailure.Failed, "Synthetic installer diagnostic"));
        PumpUntilCompleted(failed);
        Assert.AreEqual(LocalizationService.Instance["Components.Failure.Failed"], status.Text);
        TextBox diagnostic = Named<TextBox>(window, "ComponentDiagnostic");
        Assert.AreEqual("Synthetic installer diagnostic", diagnostic.Text);
        Assert.IsTrue(diagnostic.IsReadOnly);
        Assert.AreEqual(Visibility.Visible, diagnostic.Visibility);
        Assert.IsTrue(install.IsEnabled, "A failure must allow an immediate retry.");

        _pending = NewRequest();
        Task restartRequired = window.InstallComponentAsync(ComponentKind.Ocr);
        _pending.SetResult(new(ComponentInstallFailure.RestartRequired));
        PumpUntilCompleted(restartRequired);
        Assert.AreEqual(LocalizationService.Instance["Components.Failure.RestartRequired"], status.Text);

        _pending = NewRequest();
        Task shuttingDown = window.InstallComponentAsync(ComponentKind.Ocr);
        Task cleanup = window.CancelComponentOperationAsync();
        Assert.AreSame(shuttingDown, cleanup);
        Assert.IsTrue(_token.IsCancellationRequested);
        Assert.IsFalse(cleanup.IsCompleted, "Application shutdown must wait for installer cleanup.");
        _pending.SetResult(new(ComponentInstallFailure.Cancelled));
        PumpUntilCompleted(cleanup);
        Assert.AreEqual(Visibility.Collapsed, diagnostic.Visibility);
    }

    private static TaskCompletionSource<ComponentInstallResult> NewRequest()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static T Named<T>(Window window, string name) where T : FrameworkElement
        => (T)window.FindName(name);

    private static void PumpUntilCompleted(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(task.IsCompleted, "The component UI operation did not finish.");
        task.GetAwaiter().GetResult();
    }
}
