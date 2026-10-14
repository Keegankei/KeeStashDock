using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Runs inside the existing isolated STA Application with HTTP fixtures only.</summary>
internal static class UpdateWindowScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        // A bare STA test is not a running WPF input dispatch. Install the same
        // context a real routed click provides before exercising async handlers.
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { VerifyOnDispatcher(isolatedRoot); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void VerifyOnDispatcher(string isolatedRoot)
    {
        string priorLanguage = LocalizationService.Instance.CurrentLanguage;
        LocalizationService.Instance.SetLanguage("en-US");
        string installation = Path.Combine(isolatedRoot, "update-installation");
        Directory.CreateDirectory(installation);
        try
        {
            VerifyNoUpdate(installation);
            VerifyAvailableAndManualInstall(installation);
            VerifyCancellationAndClose(installation);
            VerifyUnusedStageCleanup(installation, isolatedRoot);
        }
        finally { LocalizationService.Instance.SetLanguage(priorLanguage); }
    }

    private static void VerifyNoUpdate(string installation)
    {
        using var handler = new ReplyHandler((_, _) => Task.FromResult(Metadata(DesktopUpdateCoordinator.CurrentVersion)));
        using var http = new HttpClient(handler);
        using var service = new ReleaseUpdateService(http);
        var window = new UpdateWindow(_ => throw new AssertFailedException("A check must never invoke installation."), service, installation);
        try
        {
            Assert.AreEqual(0, handler.Requests, "Constructing the dialog cannot start network access.");
            window.Show();
            PumpUntil(() => Status(window) == L("Updates.UpToDate"));
            Assert.AreEqual(1, handler.Requests, "Opening the requested update dialog performs one check.");
            Assert.IsFalse(ActionButton(window, "DownloadUpdateButton").IsEnabled);
            Assert.IsFalse(ActionButton(window, "InstallUpdateButton").IsEnabled);
            window.Hide(); window.Show();
            Drain();
            Assert.AreEqual(1, handler.Requests, "Showing the same dialog again must not duplicate its automatic check.");
            ActionButton(window, "CheckUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => handler.Requests == 2 && ActionButton(window, "CheckUpdateButton").IsEnabled);
        }
        finally { CloseAndDrain(window); }
    }

    private static void VerifyAvailableAndManualInstall(string installation)
    {
        string version = NewerVersion();
        const string notes = "Example release preview\n\nA simulated release response for documentation and UI tests.\n\n- Faster startup\n- Small fixes and reliability improvements";
        using var handler = new ReplyHandler((_, _) => Task.FromResult(Metadata(version, notes)));
        using var http = new HttpClient(handler);
        using var service = new ReleaseUpdateService(http);
        var window = new UpdateWindow(_ => throw new AssertFailedException("A check must never invoke installation."), service, installation);
        try
        {
            window.Show();
            PumpUntil(() => Status(window) == L("Updates.Manual"));
            Assert.IsFalse(ActionButton(window, "DownloadUpdateButton").IsEnabled,
                "A legacy installation without an ownership manifest must use manual upgrade.");
            Assert.AreEqual(notes, ((TextBox)window.FindName("ReleaseNotes")).Text);

            // A valid synthetic ownership manifest enables the separate explicit
            // download action. The fixture does not download or execute a file.
            byte[] app = Encoding.UTF8.GetBytes("Fixture application, never executed.");
            File.WriteAllBytes(Path.Combine(installation, "KeeStashDock.exe"), app);
            var manifest = new UpdatePackageManifest(1, DesktopUpdateCoordinator.CurrentVersion,
                [new("KeeStashDock.exe", app.Length, Convert.ToHexStringLower(SHA256.HashData(app)))]);
            File.WriteAllText(Path.Combine(installation, UpdatePackageManifest.FileName),
                JsonSerializer.Serialize(manifest, UpdatePackageManifest.JsonOptions));
            ActionButton(window, "CheckUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ActionButton(window, "DownloadUpdateButton").IsEnabled);
            Assert.IsTrue(Status(window).Contains(version, StringComparison.Ordinal));
            Assert.IsFalse(ActionButton(window, "InstallUpdateButton").IsEnabled, "Discovery alone cannot enable restart/install.");
            Assert.AreEqual(notes, ((TextBox)window.FindName("ReleaseNotes")).Text);
            Assert.AreEqual(2, handler.Requests, "Checks must not fetch assets automatically.");
            SavePreview(window);
        }
        finally { CloseAndDrain(window); }
    }

    private static void VerifyCancellationAndClose(string installation)
    {
        foreach (bool close in new[] { false, true })
        {
            var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken seen = default;
            using var handler = new ReplyHandler((_, token) => { seen = token; return reply.Task; });
            using var http = new HttpClient(handler);
            using var service = new ReleaseUpdateService(http);
            var window = new UpdateWindow(_ => Task.FromResult(false), service, installation);
            try
            {
                window.Show();
                PumpUntil(() => handler.Requests == 1);
                Assert.IsFalse(ActionButton(window, "CheckUpdateButton").IsEnabled);
                ActionButton(window, "CheckUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, handler.Requests, "A second check cannot overlap the pending request.");
                string before = Status(window);
                if (close) window.Close();
                else ActionButton(window, "CancelUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(seen.IsCancellationRequested);
                // This deliberately uncooperative handler returns after cancellation.
                // It must not replace the cancelled/closed state with an available release.
                reply.SetResult(Metadata(NewerVersion(), "Late response must not become visible."));
                if (!close)
                {
                    PumpUntil(() => Status(window) == L("Updates.Cancelled") && ActionButton(window, "CheckUpdateButton").IsEnabled);
                    Assert.IsFalse(ActionButton(window, "DownloadUpdateButton").IsEnabled);
                }
                else
                {
                    PumpUntil(() => !window.HasPendingWork);
                    Assert.IsFalse(window.IsVisible);
                    Assert.AreEqual(before, Status(window), "A late response cannot mutate a closed window.");
                }
                Assert.AreEqual("", ((TextBox)window.FindName("ReleaseNotes")).Text);
            }
            finally
            {
                reply.TrySetCanceled();
                CloseAndDrain(window);
            }
        }
    }

    private static void SavePreview(UpdateWindow window)
    {
        string? destination = Environment.GetEnvironmentVariable("KEESTASHDOCK_DOC_IMAGES");
        if (string.IsNullOrWhiteSpace(destination)) return;
        string directory = Path.GetFullPath(destination);
        Directory.CreateDirectory(directory);
        var target = (TextBlock)window.FindName("UpdateTarget");
        string realFixtureLabel = target.Text;
        try
        {
            // Replace only the fixture's displayed path, never its I/O destination.
            // The screenshot contains no maintainer username or temporary path.
            target.Text = string.Format(L("Updates.Target"), @"C:\Apps\KeeStashDock (example)");
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            int width = (int)Math.Ceiling(content.ActualWidth + 44);
            int height = (int)Math.Ceiling(content.ActualHeight + 44);
            var visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle((Brush)window.FindResource("B.PanelBg"), null, new Rect(0, 0, width, height));
                drawing.DrawRectangle(new VisualBrush(content), null,
                    new Rect(22, 22, content.ActualWidth, content.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, "update-check.png"));
            png.Save(output);
        }
        finally { target.Text = realFixtureLabel; }
    }

    private static void VerifyUnusedStageCleanup(string installation, string isolatedRoot)
    {
        string version = NewerVersion();
        byte[] app = Encoding.UTF8.GetBytes("Synthetic application, never executed.");
        var manifest = new UpdatePackageManifest(1, version,
            [new("KeeStashDock.exe", app.Length, Convert.ToHexStringLower(SHA256.HashData(app)))]);
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream file = zip.CreateEntry("KeeStashDock.exe").Open()) file.Write(app);
            using (Stream file = zip.CreateEntry(UpdatePackageManifest.FileName).Open())
                file.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, UpdatePackageManifest.JsonOptions));
        }
        byte[] archive = bytes.ToArray();
        string asset = $"KeeStashDock-{version}-win-x64.zip";
        string prefix = ReleaseUpdateService.RepositoryUrl + "/releases/download/v" + version + "/";
        foreach (bool close in new[] { false, true })
        {
            string staging = Path.Combine(isolatedRoot, "unused-update-" + close);
            using var handler = new ReplyHandler((request, _) =>
            {
                string uri = request.RequestUri!.AbsoluteUri;
                HttpResponseMessage response = uri == ReleaseUpdateService.ReleaseApiUrl
                    ? Metadata(version, archiveSize: archive.Length)
                    : uri == prefix + asset + ".sha256"
                        ? new(HttpStatusCode.OK) { Content = new StringContent(Convert.ToHexStringLower(SHA256.HashData(archive)) + "  " + asset) }
                        : uri == prefix + asset
                            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }
                            : throw new AssertFailedException("Unexpected fixture endpoint: " + uri);
                return Task.FromResult(response);
            }, metadataOnly: false);
            using var http = new HttpClient(handler);
            using var service = new ReleaseUpdateService(http);
            var window = new UpdateWindow(_ => throw new AssertFailedException("Cleanup must never install."),
                service, installation, staging);
            try
            {
                window.Show();
                PumpUntil(() => ActionButton(window, "DownloadUpdateButton").IsEnabled);
                ActionButton(window, "DownloadUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => ActionButton(window, "InstallUpdateButton").IsEnabled);
                Assert.AreEqual(1, Directory.GetDirectories(staging).Length);
                if (close) window.Close();
                else ActionButton(window, "CheckUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => Directory.GetFileSystemEntries(staging).Length == 0);
                if (!close) PumpUntil(() => ActionButton(window, "DownloadUpdateButton").IsEnabled);
                Assert.IsTrue(File.Exists(Path.Combine(installation, "KeeStashDock.exe")), "Cleanup cannot touch the installation.");
            }
            finally { CloseAndDrain(window); }
        }
    }

    private static string NewerVersion()
    {
        Version current = ReleaseUpdateService.ParseVersion(DesktopUpdateCoordinator.CurrentVersion);
        return $"{current.Major}.{current.Minor + 1}.0";
    }
    private static HttpResponseMessage Metadata(string version, string notes = "", long archiveSize = 85_000_000)
    {
        string asset = $"KeeStashDock-{version}-win-x64.zip";
        string prefix = ReleaseUpdateService.RepositoryUrl + "/releases/download/v" + version + "/";
        string json = JsonSerializer.Serialize(new
        {
            tag_name = "v" + version,
            html_url = ReleaseUpdateService.RepositoryUrl + "/releases/tag/v" + version,
            draft = false, prerelease = false, body = notes,
            assets = new[]
            {
                new { name = asset, browser_download_url = prefix + asset, size = archiveSize },
                new { name = asset + ".sha256", browser_download_url = prefix + asset + ".sha256", size = 98L },
            },
        });
        return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
    private static Button ActionButton(UpdateWindow window, string name) => (Button)window.FindName(name);
    private static string Status(UpdateWindow window) => ((TextBlock)window.FindName("UpdateStatus")).Text;
    private static string L(string key) => LocalizationService.Instance[key];
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void CloseAndDrain(UpdateWindow window)
    {
        window.Close();
        PumpUntil(() => !window.HasPendingWork);
        Drain();
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(condition(), "The update dialog did not settle into the expected state.");
    }

    private sealed class ReplyHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply,
        bool metadataOnly = true) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (metadataOnly) Assert.AreEqual(ReleaseUpdateService.ReleaseApiUrl, request.RequestUri!.AbsoluteUri,
                "UI discovery fixtures must never request downloadable assets or another endpoint.");
            Requests++;
            return reply(request, cancellationToken);
        }
    }
}
