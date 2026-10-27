using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop.Views;

/// <summary>Explicit, non-recursive variable entry with a single time snapshot and no side effects on cancel.</summary>
internal sealed class TemplateFillWindow : Window
{
    private readonly TemplateDocument _document;
    private readonly DateTime _snapshot = DateTime.Now;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly TextBox _preview;
    private readonly TextBlock _status;
    private readonly Button _accept;
    public string? ResultText { get; private set; }

    public TemplateFillWindow(TemplateDocument document, string? actionLabel = null)
    {
        _document = document;
        var loc = LocalizationService.Instance;
        Title = loc["Template.Title"];
        Width = 600; Height = 640; MinWidth = 360; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "B.WindowBg");
        ThemeManager.TrackWindowChrome(this);
        loc.ApplyFlowDirection(this);

        var shell = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var title = new TextBlock { Text = loc["Template.Title"], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        TypographyManager.SetFontSize(title, 23);
        heading.Children.Add(title);
        heading.Children.Add(new TextBlock { Text = loc["Template.Hint"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); shell.Children.Add(heading);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        _accept = new Button { Content = actionLabel ?? loc["Template.Copy"], IsDefault = true, MinHeight = 36, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        _accept.SetResourceReference(StyleProperty, "S.ButtonPrimary");
        _accept.Click += (_, _) => { UpdatePreview(); if (ResultText is not null) DialogResult = true; };
        var cancel = new Button { Content = loc["Common.Cancel"], IsCancel = true, MinHeight = 36, Padding = new Thickness(14, 8, 14, 8) };
        cancel.SetResourceReference(StyleProperty, "S.Button");
        footer.Children.Add(_accept); footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
        var stack = new StackPanel();
        foreach (string variable in document.Variables)
        {
            stack.Children.Add(new TextBlock { Text = variable, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var input = new TextBox { Margin = new Thickness(0, 0, 0, 14), MinHeight = 36, MaxLength = 100_000 };
            input.SetResourceReference(StyleProperty, "S.Input");
            System.Windows.Automation.AutomationProperties.SetName(input, variable);
            _fields.Add(variable, input); stack.Children.Add(input);
        }
        stack.Children.Add(new TextBlock { Text = loc["Template.Preview"], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) });
        _preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _preview.SetResourceReference(StyleProperty, "S.Input");
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger");
        stack.Children.Add(_preview); stack.Children.Add(_status);
        shell.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = shell;
        foreach (TextBox input in _fields.Values) input.TextChanged += (_, _) => UpdatePreview();
        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, area.Width); MinHeight = Math.Min(MinHeight, area.Height);
            Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
            _fields.Values.FirstOrDefault()?.Focus();
        };
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            ResultText = _document.Render(_fields.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal), _snapshot);
            _preview.Text = ResultText;
            _status.Text = string.Empty;
            _accept.IsEnabled = !string.IsNullOrWhiteSpace(ResultText);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TemplateParseException)
        {
            ResultText = null;
            _preview.Text = string.Empty;
            _status.Text = LocalizationService.Instance["Template.OutputInvalid"];
            _accept.IsEnabled = false;
        }
    }
}
