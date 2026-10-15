using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>
/// Opt-in, repeatable observations of real controls with synthetic data and a
/// virtual pointer. Timing results are diagnostic evidence, never test gates.
/// </summary>
internal static class FloatingPerformanceScenarios
{
    internal static void RecordIfRequested(string isolatedRoot)
    {
        string? output = Environment.GetEnvironmentVariable("KEESTASHDOCK_PERF_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        string draftRoot = Path.Combine(AppContext.BaseDirectory, "Data");
        Assert.IsFalse(Directory.Exists(draftRoot), "The performance fixture requires an unused portable draft directory.");
        Directory.CreateDirectory(draftRoot);
        var results = new List<object>();
        try
        {
            using var log = new FileLogger(Path.Combine(isolatedRoot, "performance-logs"));
            var db = new Db(Path.Combine(isolatedRoot, "performance.db"), new DatabaseMaintenanceCoordinator());
            new MigrationRunner(db, log).Run();
            var repository = new SnippetRepository(db);
            var snippets = new SnippetService(repository, log);
            for (int i = 0; i < 12; i++)
                snippets.Add($"Synthetic note {i + 1:00}\nA repeatable text card for measuring pointer-follow rendering.",
                    Domain.Entities.SnippetSource.Manual);

            foreach (Size requestedSize in new[] { new Size(400, 600), new Size(600, 900) })
            {
                var settings = new SettingsStore(Path.Combine(isolatedRoot, $"performance-{requestedSize.Width}.json"));
                settings.Current.PanelUserWidth = requestedSize.Width;
                settings.Current.PanelUserHeight = requestedSize.Height;
                settings.Current.CollapseDelaySeconds = 60;
                settings.Current.FloatingPanelEnabled = true;
                settings.Current.FloatingPanelFollowPointer = true;
                Point pointer = new(-1000, -1000);
                var panel = new QuickPanelWindow(snippets, new SearchService(repository), settings,
                    new CategoryRepository(db), () => { }, () => { }, perspectivePointer: () => pointer)
                { Title = "KeeStashDock isolated performance fixture" };
                try
                {
                    panel.ShowPanel();
                    Pump(TimeSpan.FromMilliseconds(500));
                    panel.UpdateLayout();
                    foreach (string scenario in new[] { "Idle", "Pointer moving", "Fixed moving", "Drag inspect" })
                    {
                        settings.Current.FloatingPanelFollowPointer = scenario != "Fixed moving";
                        settings.Save();
                        pointer = scenario == "Idle" ? new(-1000, -1000) : new(panel.ActualWidth / 2, 280);
                        panel.UpdatePerspectivePointer();
                        Pump(TimeSpan.FromMilliseconds(500));
                        if (scenario == "Drag inspect") Assert.IsTrue(panel.BeginInspection(pointer));
                        try { results.Add(Measure(panel, scenario, value => pointer = value)); }
                        finally { if (panel.IsInspecting) panel.EndInspection(); }
                    }
                }
                finally { panel.Close(); }
                Pump(TimeSpan.FromMilliseconds(200));
            }

            string destination = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, JsonSerializer.Serialize(new
            {
                RecordedUtc = DateTimeOffset.UtcNow,
                Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                RenderTier = RenderCapability.Tier >> 16,
                LogicalProcessorCount = Environment.ProcessorCount,
                SystemParameters.ClientAreaAnimation,
                SystemParameters.HighContrast,
                Notes = "Synthetic pointer; 12 synthetic snippets; 4 seconds per scenario after warm-up. " +
                    "CPU includes all process threads; UI rendering events are not GPU presentation times. " +
                    "Pointer metrics cover explicit 32 ms samples; normal product timers remain active.",
                Scenarios = results,
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        finally { Directory.Delete(draftRoot, recursive: false); }
    }

    private static object Measure(QuickPanelWindow panel, string scenario, Action<Point> movePointer)
    {
        var updateMilliseconds = new List<double>();
        var updateAllocations = new List<double>();
        var frameIntervals = new List<double>();
        var dispatcherDelays = new List<double>();
        var watch = Stopwatch.StartNew();
        TimeSpan? lastRenderingTime = null;
        double lastFrame = 0;
        int queuedProbe = 0;
        bool running = true;
        var surface = (PerspectiveSurface)panel.FindName("SpatialSurface");
        var observedYaws = new HashSet<double>();
        void OnRendering(object? sender, EventArgs args)
        {
            if (args is not RenderingEventArgs rendering || lastRenderingTime == rendering.RenderingTime) return;
            double now = watch.Elapsed.TotalMilliseconds;
            if (lastRenderingTime is not null) frameIntervals.Add(now - lastFrame);
            lastRenderingTime = rendering.RenderingTime;
            lastFrame = now;
        }
        var pointerTimer = new DispatcherTimer(DispatcherPriority.Input)
            { Interval = TimeSpan.FromMilliseconds(32) };
        pointerTimer.Tick += (_, _) =>
        {
            if (scenario != "Idle")
            {
                double phase = watch.Elapsed.TotalSeconds * Math.PI;
                movePointer(new Point(panel.ActualWidth * (0.5 + Math.Sin(phase) * 0.48),
                    260 + (panel.ActualHeight - 310) * (0.5 + Math.Cos(phase * 0.8) * 0.48)));
            }
            if (scenario == "Drag inspect")
            {
                double phase = watch.Elapsed.TotalSeconds * Math.PI;
                panel.MoveInspection(new Point(panel.ActualWidth * (0.5 + Math.Sin(phase) * 0.48),
                    panel.ActualHeight * (0.5 + Math.Cos(phase * 0.8) * 0.48)));
            }
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long began = Stopwatch.GetTimestamp();
            panel.UpdatePerspectivePointer();
            observedYaws.Add(surface.Yaw);
            updateMilliseconds.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
            updateAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        };
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        CompositionTarget.Rendering += OnRendering;
        pointerTimer.Start();
        using var latencyProbe = new System.Threading.Timer(_ =>
        {
            if (Interlocked.Exchange(ref queuedProbe, 1) != 0) return;
            long queuedAt = Stopwatch.GetTimestamp();
            panel.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (running) dispatcherDelays.Add(Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
                Volatile.Write(ref queuedProbe, 0);
            }));
        }, null, 0, 16);
        try { Pump(TimeSpan.FromSeconds(4)); }
        finally
        {
            running = false;
            latencyProbe.Change(Timeout.Infinite, Timeout.Infinite);
            pointerTimer.Stop();
            CompositionTarget.Rendering -= OnRendering;
        }
        if (scenario == "Drag inspect")
            Assert.IsTrue(observedYaws.Count > 10, "Inspection benchmark must actually vary the panel angle.");
        double elapsed = watch.Elapsed.TotalMilliseconds;
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        long totalAllocated = GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore;
        DpiScale dpi = VisualTreeHelper.GetDpi(panel);
        return new
        {
            Scenario = scenario,
            Width = panel.ActualWidth,
            Height = panel.ActualHeight,
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            panel.FloatingDepthEnabled,
            DistinctPanelAngles = observedYaws.Count,
            ElapsedMilliseconds = elapsed,
            CpuMilliseconds = cpu,
            CpuPercentOfOneCore = 100 * cpu / elapsed,
            TotalAllocatedBytes = totalAllocated,
            RenderEventCount = frameIntervals.Count + (lastRenderingTime is null ? 0 : 1),
            RenderIntervalsMilliseconds = Summarize(frameIntervals),
            InputDispatcherDelayMilliseconds = Summarize(dispatcherDelays),
            ExplicitPointerUpdateMilliseconds = Summarize(updateMilliseconds),
            ExplicitPointerUpdateAllocatedBytes = Summarize(updateAllocations),
        };
    }

    private static object Summarize(List<double> values)
    {
        if (values.Count == 0) return new { Count = 0, Mean = 0d, P50 = 0d, P95 = 0d, Max = 0d };
        values.Sort();
        return new
        {
            Count = values.Count,
            Mean = values.Average(),
            P50 = values[(int)Math.Floor((values.Count - 1) * 0.5)],
            P95 = values[(int)Math.Floor((values.Count - 1) * 0.95)],
            Max = values[^1],
        };
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        // End the measurement on time even when continuous rendering starves
        // lower-priority work; that starvation is itself measured above.
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
}
