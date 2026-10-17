using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Records actual controls and commands in the supplied disposable fixture only.</summary>
internal static class StandardPreviewRecorder
{
    internal static void Record(QuickPanelWindow panel, string directory)
    {
        const string note = "An idea worth keeping\nReady for the next step.";
        const int delayMilliseconds = 200, padding = 32;
        var viewModel = (QuickPanelViewModel)panel.DataContext;
        var input = (TextBox)panel.FindName("QuickBox");
        string originalInput = viewModel.QuickInput;
        string originalFilter = viewModel.Filter;
        if (originalInput.Length != 0 || File.Exists(PortablePaths.DraftFile))
            throw new InvalidOperationException("The documentation recorder requires an empty fixture draft.");

        var encoder = new GifBitmapEncoder();
        var backdrop = new SolidColorBrush(Color.FromRgb(230, 235, 243));
        backdrop.Freeze();
        BitmapPalette? palette = null;
        long? createdId = null;
        try
        {
            viewModel.Filter = string.Empty;
            if (viewModel.Items.Any(item => item.Model.Content == note))
                throw new InvalidOperationException("The documentation note must not already exist in the fixture.");

            Hold(5);
            for (int frame = 1; frame <= 12; frame++)
            {
                // SetCurrentValue keeps the real TextBox binding intact. The
                // normal binding invokes DraftChanged, just as ordinary typing.
                int length = (int)Math.Ceiling(note.Length * frame / 12d);
                input.SetCurrentValue(TextBox.TextProperty, note[..length]);
                input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Hold(1);
            }
            Hold(3);

            viewModel.QuickAddCommand.Execute(null);
            viewModel.Reload();
            createdId = viewModel.Items.Single(item => item.Model.Content == note).Id;
            Hold(7);

            // These are the same commands bound to each visible card's pin
            // and trash buttons. Copy is deliberately never invoked.
            viewModel.TogglePinCommand.Execute(CreatedCard());
            Hold(6);
            viewModel.TogglePinCommand.Execute(CreatedCard());
            Hold(4);
            viewModel.MoveToTrashCommand.Execute(CreatedCard());
            createdId = null;
            Hold(3);

            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            Directory.CreateDirectory(directory);
            using var output = File.Create(Path.Combine(directory, "standard-panel-workflow.gif"));
            PerspectivePreviewRecorder.WriteAnimationMetadata(encoded.ToArray(), output, delayMilliseconds / 10);
        }
        finally
        {
            if (createdId is long id)
            {
                viewModel.Filter = string.Empty;
                viewModel.Reload();
                if (viewModel.Items.FirstOrDefault(item => item.Id == id) is { } created)
                    viewModel.MoveToTrashCommand.Execute(created);
            }
            viewModel.QuickInput = originalInput;
            viewModel.Filter = originalFilter;
            // The panel owns DraftGuard. Clearing the input above uses its
            // normal debounce to remove this fixture's temporary draft; its
            // normal Dispose will also flush before the scenario removes Data.
            Pump(TimeSpan.FromMilliseconds(800));
            if (File.Exists(PortablePaths.DraftFile))
                throw new InvalidOperationException("The documentation fixture did not clear its draft.");
        }

        SnippetItemViewModel CreatedCard() => viewModel.Items.Single(item => item.Id == createdId);

        void Hold(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                Pump(TimeSpan.FromMilliseconds(delayMilliseconds));
                panel.UpdateLayout();
                int width = (int)Math.Ceiling(panel.ActualWidth) + 2 * padding;
                int height = (int)Math.Ceiling(panel.ActualHeight) + 2 * padding;
                var shadow = panel.OwnedWindows.OfType<PanelShadowWindow>().Single();
                var visual = new DrawingVisual();
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
                    if (shadow.IsVisible)
                        drawing.DrawImage(PerspectivePreviewRecorder.Snapshot(shadow),
                            new Rect(0, 0, shadow.ActualWidth, shadow.ActualHeight));
                    drawing.DrawImage(PerspectivePreviewRecorder.Snapshot(panel),
                        new Rect(padding, padding, panel.ActualWidth, panel.ActualHeight));
                }
                var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(visual);
                palette ??= new BitmapPalette(rendered, 256);
                var indexed = new FormatConvertedBitmap(rendered, PixelFormats.Indexed8, palette, 0);
                encoder.Frames.Add(BitmapFrame.Create(indexed));
            }
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(8),
        };
        timer.Tick += (_, _) => { if (elapsed.Elapsed >= duration) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
}
