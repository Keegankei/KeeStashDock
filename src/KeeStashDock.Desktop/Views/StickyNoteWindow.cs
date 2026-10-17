using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// A torn-out floating sticky note is a pixel twin of the quick-panel card
/// it came from: the same S.Card container, title/body/badge/time layout,
/// and resource keys, plus one extra control: a close "X" in the
/// top-right corner. The former header (opacity slider, copy button, pin
/// glyph) is gone with the reskin.
///
/// Interactions:
///   - drag anywhere on the note to move it;
///   - a CLICK (press and release without movement) copies the content -
///     the same primary gesture the card has inside the panel;
///   - the X or Esc closes the note.
/// Any number can coexist; each is session-scoped (closing the app closes
/// its notes). Built in pure code so tearing out N notes costs no XAML
/// plumbing.
/// </summary>
public sealed class StickyNoteWindow : Window
{
    private static int _spawnOffset;
    private readonly PanelShadowWindow _shadow;
    private readonly SettingsStore? _settings;
    private readonly ScaleTransform _entranceScale = new(1, 1);
    private bool _floating, _closed;

    /// <param name="item">The card being torn out (title / content / badge /
    /// time render exactly as the panel shows them).</param>
    /// <param name="cardOpacity">The panel's control-opacity setting (0.1-1);
    /// the note overrides B.CardBg with the same alpha so a translucent
    /// panel tears out an equally translucent note.</param>
    public StickyNoteWindow(ViewModels.SnippetItemViewModel item,
                            double cardOpacity = 1.0, bool floating = false,
                            SettingsStore? settings = null)
    {
        _floating = floating;
        _settings = settings;
        Localization.LocalizationService.Instance.ApplyFlowDirection(this);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = 280;
        MaxHeight = 340;
        SizeToContent = SizeToContent.Height;

        // Same translucency as the panel's cards (window-scoped override,
        // exactly the mechanism the panel itself uses).
        var cardColor = ((SolidColorBrush)Application.Current
            .FindResource("B.CardBg")).Color;
        byte ca = (byte)(Math.Clamp(cardOpacity, 0.1, 1.0) * 255);
        Resources["B.CardBg"] = new SolidColorBrush(
            Color.FromArgb(ca, cardColor.R, cardColor.G, cardColor.B));

        // Cascade spawn position so multiple notes don't stack exactly.
        var wa = SystemParameters.WorkArea;
        _spawnOffset = (_spawnOffset + 28) % 168;
        Left = wa.Right - Width - 60 - _spawnOffset;
        Top = wa.Top + 80 + _spawnOffset;

        var loc = Localization.LocalizationService.Instance;

        // ---- Row 0: title + the ONE addition, the close X ----------------
        var title = new TextBlock
        {
            Text = item.DisplayTitle,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "B.CardTextPrimary");

        var closeBtn = new Button
        {
            Content = "\uE711",   // X glyph
            Style = (Style)Application.Current.FindResource("S.IconButton"),
            ToolTip = loc["Note.Close"],
            VerticalAlignment = VerticalAlignment.Top,
        };
        closeBtn.Click += (_, _) => Close();

        var titleRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleRow.Children.Add(closeBtn);
        titleRow.Children.Add(title);

        // ---- Row 1: body - the card's preview text, un-truncated ---------
        var body = new TextBlock
        {
            Text = item.Model.Content,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4),
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "B.CardTextMuted");
        var bodyScroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // ---- Row 2: footer - type badge + relative time, as on the card --
        var badgeText = new TextBlock { Text = item.TypeBadge, FontSize = 10 };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "B.Accent");
        var badge = new Border
        {
            Style = (Style)Application.Current.FindResource("S.Badge"),
            Child = badgeText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var time = new TextBlock
        {
            Text = item.RelativeTime,
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        time.SetResourceReference(TextBlock.ForegroundProperty, "B.CardTextMuted");
        var footer = new StackPanel { Orientation = Orientation.Horizontal };
        footer.Children.Add(badge);
        footer.Children.Add(time);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleRow, 0);
        Grid.SetRow(bodyScroll, 1);
        Grid.SetRow(footer, 2);
        grid.Children.Add(titleRow);
        grid.Children.Add(bodyScroll);
        grid.Children.Add(footer);

        // The card container itself: the SAME S.Card style the panel uses
        // (background, border, radius, padding, shadow, hover tint). Only
        // the style's list margin is zeroed - a floating window has no
        // neighbors to keep distance from.
        var card = new Border
        {
            Style = (Style)Application.Current.FindResource("S.Card"),
            Margin = new Thickness(0),
            Effect = null,
            Child = grid,
        };
        Content = card;
        _shadow = new PanelShadowWindow(this, radius: 10);
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        if (_settings is not null) _settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            ResetEntrance();
            _shadow.Dispose();
            SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
            if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        };
        ApplyAppearance();

        // ---- Gestures -----------------------------------------------------
        // Drag anywhere (except the X); a press WITHOUT movement is a click
        // and copies - the card's primary gesture, preserved on the note.
        // DragMove() blocks until release, so comparing the window position
        // before/after cleanly separates the two.
        string content = item.Model.Content;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d &&
                FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(d) is not null)
                return;
            ResetEntrance();
            double x = Left, y = Top;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            if (Math.Abs(Left - x) < 2 && Math.Abs(Top - y) < 2)
            {
                try { ClipboardWriteGuard.Shared.Write(() => Clipboard.SetDataObject(content, copy: true)); } catch { }
                MiniToast.Show(loc["QuickPanel.Copied"]);
            }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    /// <summary>Tear-out entrance: places the note over the card it is
    /// being torn from (DIP screen coords), plays a short scale+fade "pop"
    /// so the card visually detaches instead of teleporting, and - because
    /// the tear happens MID-DRAG - immediately hands the mouse drag over to
    /// this window, so the note rides the cursor until release.</summary>
    public void ShowTornOutAt(double left, double top)
    {
        var wa = SystemParameters.WorkArea;
        Left = Math.Clamp(left, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        Top = Math.Clamp(top, wa.Top, wa.Bottom - 60);

        // WPF forbids RenderTransform on a WINDOW (CoerceRenderTransform
        // throws InvalidOperationException) - the scale must live on the
        // window's CONTENT; only Opacity may animate at window level.
        bool animate = _floating && FloatingCardMotion.AnimationsAllowed;
        Opacity = 1;
        _entranceScale.ScaleX = _entranceScale.ScaleY = animate ? 0.96 : 1;
        if (Content is FrameworkElement root)
        {
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = _entranceScale;
        }
        Show();
        if (animate)
        {
            new MotionValue(_entranceScale, ScaleTransform.ScaleXProperty).To(1);
            new MotionValue(_entranceScale, ScaleTransform.ScaleYProperty).To(1);
        }

        // Take over the in-progress drag. Queued at Input priority so the
        // window has a source; guarded in case the button was released in
        // the meantime. This programmatic DragMove raises no ButtonDown on
        // the note, so the click-to-copy gesture cannot misfire here.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                if (_closed || !IsVisible) return;
                Mouse.Capture(null);
                if (Mouse.LeftButton == MouseButtonState.Pressed)
                    try { DragMove(); } catch (InvalidOperationException) { }
            });
    }

    private void OnSettingsChanged()
    {
        if (!_closed && !Dispatcher.HasShutdownStarted) Dispatcher.Invoke(ApplyAppearance);
    }

    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e) => OnSettingsChanged();

    private void ApplyAppearance()
    {
        if (_closed) return;
        if (_settings is not null) _floating = _settings.Current.FloatingPanelEnabled;
        if (!_floating || !FloatingCardMotion.AnimationsAllowed) ResetEntrance();
        _shadow.UpdateAppearance(!SystemParameters.HighContrast, _floating, emphasized: true);
    }

    private void ResetEntrance()
    {
        _entranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _entranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _entranceScale.ScaleX = _entranceScale.ScaleY = 1;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T hit) return hit;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
