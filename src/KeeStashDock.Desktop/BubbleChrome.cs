using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

internal sealed record BubblePalette(Color Text, Color Muted, Color Accent, Color OnAccent,
    Color Surface, Color Input, Color Border, Color Hover, Color Pressed, Color Highlight,
    bool HighContrast, double Opacity);

/// <summary>Lightweight shared geometry and local control colors for transient surfaces.</summary>
internal static class BubbleChrome
{
    private sealed record Frame(Border Rim, Border Highlight);
    private static readonly ConditionalWeakTable<Border, Frame> Frames = new();
    private static readonly ControlTemplate ActionTemplate = CreateActionTemplate();

    internal static void Decorate(Border surface, UIElement content, double radius = 18)
    {
        surface.Child = null;
        Thickness padding = surface.Padding;
        surface.Padding = new Thickness(0);
        surface.CornerRadius = new CornerRadius(radius);
        surface.UseLayoutRounding = true;
        surface.SnapsToDevicePixels = true;
        var grid = new Grid();
        grid.Children.Add(new Border { Padding = padding, Child = content });
        var rim = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(radius - 1),
            BorderThickness = new Thickness(1), IsHitTestVisible = false };
        var highlight = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(radius, 1, radius, 0), IsHitTestVisible = false };
        grid.Children.Add(rim); grid.Children.Add(highlight);
        surface.Child = grid;
        Frames.Remove(surface);
        Frames.Add(surface, new(rim, highlight));
    }

    internal static BubblePalette Apply(Border surface, AppSettings settings)
    {
        BubblePalette palette = GlassBubble.Resolve(settings);
        GlassBubble.Apply(palette, surface);
        if (Frames.TryGetValue(surface, out Frame? frame))
        {
            frame.Rim.Visibility = frame.Highlight.Visibility = palette.HighContrast ? Visibility.Collapsed : Visibility.Visible;
            Color sheen = palette.Highlight; sheen.A = 110;
            var rim = new LinearGradientBrush(sheen, Colors.Transparent, 90); rim.Freeze();
            frame.Rim.BorderBrush = rim;
            var highlight = new LinearGradientBrush(Colors.Transparent, palette.Highlight, 0);
            highlight.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            highlight.GradientStops[1].Offset = .5;
            highlight.Freeze();
            frame.Highlight.Background = highlight;
        }
        return palette;
    }

    internal static void ApplyResources(FrameworkElement scope, BubblePalette palette)
    {
        foreach ((string key, Color color) in new[]
        {
            ("B.TextPrimary", palette.Text), ("B.TextMuted", palette.Muted),
            ("B.InputText", palette.Text), ("B.InputTextMuted", palette.Muted),
            ("B.InputBg", palette.Input), ("B.CardBg", palette.Surface),
            ("B.CardHover", palette.Hover), ("B.Border", palette.Border),
            ("B.AccentSoft", palette.Hover), ("B.Accent", palette.Accent),
            ("B.OnAccent", palette.OnAccent), ("B.Focus", palette.Accent),
            ("B.ScrollThumb", palette.Muted), ("B.Disabled", palette.Muted)
        }) scope.Resources[key] = Brush(color);
        if (palette.HighContrast) scope.Resources[typeof(ComboBoxItem)] = HighContrastChoiceStyle();
        else scope.Resources.Remove(typeof(ComboBoxItem));
    }

    internal static void ApplyButton(Button button, BubblePalette palette, bool primary = false, bool compact = false)
    {
        // Template triggers target its border, so local backgrounds cannot suppress feedback.
        button.Template = ActionTemplate;
        button.Cursor = Cursors.Hand;
        button.Foreground = Brush(primary ? palette.OnAccent : palette.Text);
        button.Background = Brush(primary ? palette.Accent : palette.Input);
        button.BorderBrush = Brush(primary ? palette.Accent : palette.Border);
        button.BorderThickness = new Thickness(1);
        button.MinHeight = compact ? 30 : 36;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Resources["Bubble.ActionHover"] = Brush(palette.HighContrast ? primary ? palette.Accent : palette.Input :
            primary ? GlassBubble.Mix(palette.Accent, palette.OnAccent, .12) : palette.Hover);
        button.Resources["Bubble.ActionPressed"] = Brush(palette.HighContrast ? primary ? palette.Accent : palette.Input :
            primary ? GlassBubble.Mix(palette.Accent, palette.OnAccent, .22) : palette.Pressed);
        button.Resources["Bubble.ActionFocus"] = Brush(palette.Accent);
        button.Resources["Bubble.ActionHoverText"] = Brush(primary ? palette.OnAccent : palette.Text);
        button.Resources["Bubble.ActionDisabled"] = palette.HighContrast ? .6d : .45d;
    }

    internal static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private static Style HighContrastChoiceStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.WindowTextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SystemColors.WindowBrush));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        var textStyle = new Style(typeof(TextBlock), Application.Current.TryFindResource(typeof(TextBlock)) as Style);
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
            { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ComboBoxItem), 1) }));
        var contentStyle = new Style(typeof(ContentPresenter));
        contentStyle.Resources[typeof(TextBlock)] = textStyle;
        presenter.SetValue(FrameworkElement.StyleProperty, contentStyle);
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border }));
        foreach (DependencyProperty property in new[] { ComboBoxItem.IsHighlightedProperty, ComboBoxItem.IsSelectedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, SystemColors.HighlightBrush));
            trigger.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush));
            style.Triggers.Add(trigger);
        }
        return style;
    }

    private static ControlTemplate CreateActionTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "ActionChrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter), "ActionContent");
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach ((DependencyProperty property, string resource) in new[]
        { (UIElement.IsMouseOverProperty, "Bubble.ActionHover"), (Button.IsPressedProperty, "Bubble.ActionPressed") })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(resource), "ActionChrome"));
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Bubble.ActionFocus"), "ActionChrome"));
            trigger.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty,
                new DynamicResourceExtension("Bubble.ActionHoverText"), "ActionContent"));
            template.Triggers.Add(trigger);
        }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Bubble.ActionFocus"), "ActionChrome"));
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "ActionChrome"));
        template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, new DynamicResourceExtension("Bubble.ActionDisabled"), "ActionContent"));
        template.Triggers.Add(disabled);
        return template;
    }
}
