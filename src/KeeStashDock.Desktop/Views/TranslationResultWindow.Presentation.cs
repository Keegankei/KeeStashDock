using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop.Views;

public sealed partial class TranslationResultWindow
{
    private readonly Button _copyResult;
    private Border _bodyCard = null!, _sourceCard = null!, _toolsCard = null!, _headerBadge = null!;
    private TextBlock _heading = null!, _headerIcon = null!;
    private WrapPanel _recoveryActions = null!;
    private readonly StackPanel _groupTools;
    private readonly Grid _toolFields;
    private string _headingKey = "Translate.Button";

    private void BuildPresentation()
    {
        _heading = new TextBlock
        {
            Name = "ResultHeading", FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 12, 0)
        };
        TypographyManager.SetFontSize(_heading, 14);
        _headerIcon = new TextBlock
        {
            Text = "\uE774", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _headerBadge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Child = _headerIcon };
        ConfigureIconButton(_closeButton, "ResultCloseButton");
        ConfigureIconButton(_copyResult, "ResultCopyButton");
        ConfigureIconButton(_copySource, "ResultCopySourceButton");
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_heading, 1); Grid.SetColumn(_closeButton, 2);
        header.Children.Add(_headerBadge); header.Children.Add(_heading); header.Children.Add(_closeButton);

        TypographyManager.SetFontSize(_source, 12);
        TypographyManager.SetFontSize(_targetLabel, 11);
        TypographyManager.SetFontSize(_groupLabel, 11);
        TypographyManager.SetFontSize(_saveStatus, 11);
        _targetLabel.FontWeight = _groupLabel.FontWeight = FontWeights.SemiBold;
        _sourceScroll.VerticalAlignment = VerticalAlignment.Center;
        _bodyScroll.VerticalAlignment = VerticalAlignment.Center;
        Grid TextAndCopy(UIElement text, Button copy)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            copy.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(copy, 1);
            row.Children.Add(text); row.Children.Add(copy);
            return row;
        }
        var original = TextAndCopy(_sourceScroll, _copySource);
        _sourceCard = Section("ResultSourceCard", original);
        _sourcePanel.Children.Add(_sourceCard);

        var result = new StackPanel();
        result.Children.Add(TextAndCopy(_bodyScroll, _copyResult)); result.Children.Add(_hint);
        _bodyCard = Section("ResultBodyCard", result);

        _toolsCard = Section("ResultToolsCard", _resultTools);
        _toolsCard.Margin = new Thickness(0, 8, 0, 0);
        _toolsCard.Visibility = Visibility.Collapsed;
        _recoveryActions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _recoveryActions.Children.Add(_retryButton); _recoveryActions.Children.Add(_settingsButton);

        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(header);
        stack.Children.Add(_sourcePanel);
        stack.Children.Add(_bodyCard);
        stack.Children.Add(_toolsCard);
        stack.Children.Add(_recoveryActions);
        BubbleChrome.Decorate(_surface, stack, radius: 16);
        RefreshCopyAction();
    }

    private static Border Section(string name, UIElement child) => new()
    {
        Name = name, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 8, 10, 8), Child = child
    };

    private static void ConfigureIconButton(Button button, string name)
    {
        button.Name = name;
        button.Width = button.Height = 30;
        button.MinWidth = button.MinHeight = 30;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Top;
    }

    private void ApplyLanguage()
    {
        var loc = LocalizationService.Instance;
        loc.ApplyFlowDirection(this);
        void Label(Button button, string key)
        {
            string label = loc[key];
            var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Foreground)) { Source = button });
            TypographyManager.SetFontSize(text, 12);
            button.Content = text;
            AutomationProperties.SetName(button, label);
        }
        void Icon(Button button, string glyph, string key)
        {
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Foreground)) { Source = button });
            button.Content = icon;
            button.ToolTip = loc[key];
            AutomationProperties.SetName(button, loc[key]);
        }
        Icon(_copySource, "\uE8C8", "Screenshot.CopySource");
        Label(_retryButton, "Screenshot.RetryTranslation");
        Label(_settingsButton, "Screenshot.OpenComponents");
        Label(_saveSource, "Result.SaveSource");
        Label(_saveTranslation, "Result.SaveTranslation");
        Label(_saveBoth, "Result.SaveBoth");
        Icon(_closeButton, "\uE711", "Screenshot.CloseResult");
        Icon(_copyResult, "\uE8C8", "Translate.CopyHint");
        _targetLabel.Text = loc["Result.TemporaryTarget"];
        _groupLabel.Text = loc["Result.SaveGroup"];
        AutomationProperties.SetName(_targetCombo, _targetLabel.Text);
        AutomationProperties.SetName(_groupCombo, _groupLabel.Text);
        RefreshHeading();
        _body.ToolTip = loc["Translate.CopyHint"];
        if (_hint.Visibility == Visibility.Visible) _hint.Text = loc["Screenshot.CopyFailed"];
        RefreshToolLayout();
        ClampIntoWorkArea();
    }

    private void RefreshHeading()
    {
        _heading.Text = LocalizationService.Instance[_headingKey];
        _headerIcon.Text = _headingKey == "Screenshot.ActionExtract" ? "\uE8A5" : "\uE774";
    }

    private void ApplyStyle()
    {
        var palette = BubbleChrome.Apply(_surface, _settings.Current);
        BubbleChrome.ApplyResources(this, palette);
        static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
        }
        var foreground = Brush(palette.Text);
        var muted = Brush(palette.Muted);
        _body.Foreground = _bodyGlow.Foreground = _heading.Foreground = foreground;
        _source.Foreground = _hint.Foreground = _targetLabel.Foreground = _groupLabel.Foreground = muted;
        _saveStatus.Foreground = foreground;
        _hint.Opacity = 1;
        _headerIcon.Foreground = Brush(palette.HighContrast ? palette.Text : palette.Accent);
        _headerBadge.Background = Brush(palette.HighContrast ? palette.Surface : palette.Hover);
        _bodyCard.Background = Brush(palette.Input);
        _sourceCard.Background = _toolsCard.Background = Brush(palette.Surface);
        _bodyCard.BorderBrush = _sourceCard.BorderBrush = _toolsCard.BorderBrush = Brush(palette.Border);
        foreach (Button button in new[] { _retryButton, _settingsButton, _saveSource, _saveBoth })
            BubbleChrome.ApplyButton(button, palette);
        BubbleChrome.ApplyButton(_saveTranslation, palette, primary: true);
        BubbleChrome.ApplyButton(_closeButton, palette, compact: true);
        BubbleChrome.ApplyButton(_copyResult, palette, compact: true);
        BubbleChrome.ApplyButton(_copySource, palette, compact: true);
        GlassBubble.SetGlowEnabled(_bodyGlow, palette.Glow);
    }

    private void RefreshCopyAction() => _copyResult.Visibility =
        _copyable && !_inProgress ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshToolLayout() => _toolFields?.InvalidateMeasure();

    private sealed class ResultToolFieldsGrid : Grid
    {
        private readonly StackPanel _target, _group;

        internal ResultToolFieldsGrid(StackPanel target, StackPanel group)
        {
            (_target, _group) = (target, group);
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition());
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(target); Children.Add(group);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            bool both = _target.Visibility == Visibility.Visible && _group.Visibility == Visibility.Visible;
            double preferredWidth = TypographyManager.Scale(260);
            bool columns = both && availableSize.Width >= preferredWidth;
            ColumnDefinitions[1].Width = columns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            SetColumn(_group, columns ? 1 : 0);
            SetRow(_group, both && !columns ? 1 : 0);
            _target.Margin = columns ? new Thickness(0, 0, 6, 0) : new Thickness(0);
            _group.Margin = columns ? new Thickness(6, 0, 0, 0) : new Thickness(0, both ? 8 : 0, 0, 0);
            Size desired = base.MeasureOverride(availableSize);
            // SizeToContent must retain room for both selectors even with short
            // labels or narrower fallback fonts. Deciding from ActualWidth after
            // arranging caused a font-dependent collapse into an extra row.
            return columns ? new Size(Math.Max(preferredWidth, desired.Width), desired.Height) : desired;
        }
    }

    private void CopyResultAndDismiss()
    {
        if (!TryCopyResult(text => Clipboard.SetDataObject(text, copy: true))) return;
        HideCard();
        MiniToast.Show(LocalizationService.Instance["QuickPanel.Copied"]);
    }
}
