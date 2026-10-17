namespace KeeStashDock.Desktop;

/// <summary>Keeps the applied hook state consistent across capture and window visibility changes.</summary>
internal sealed class SelectionWatcherController(
    Action start, Action stop, Action hideBubble,
    Func<bool> popupEnabled, Func<bool> translationVisible)
{
    private bool? _applied;

    internal bool IsCapturingScreenshot { get; private set; }

    internal void Apply()
    {
        bool enable = !IsCapturingScreenshot && (popupEnabled() || translationVisible());
        if (!popupEnabled()) hideBubble();
        if (_applied == enable) return;
        _applied = enable;
        if (enable) start();
        else { stop(); hideBubble(); }
    }

    internal void Stop()
    {
        stop();
        _applied = false;
    }

    internal async Task<ScreenshotCaptureResult?> CaptureAsync(
        Func<Task<ScreenshotCaptureResult?>> capture,
        Action restoreWindows,
        CancellationToken shutdownToken)
    {
        if (IsCapturingScreenshot) return null;
        IsCapturingScreenshot = true;
        try
        {
            // Do not stop the hook directly: that leaves the applied-state cache
            // enabled, so restoration would incorrectly skip restarting it.
            Apply();
            return await capture();
        }
        finally
        {
            IsCapturingScreenshot = false;
            if (!shutdownToken.IsCancellationRequested)
            {
                try { restoreWindows(); }
                finally { Apply(); }
            }
        }
    }
}
