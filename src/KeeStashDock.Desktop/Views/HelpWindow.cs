using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// The user guide (Settings > About > "User guide"). Content lives in the
/// locale JSON files as Help.Content - one paragraph per feature, sections
/// separated by blank lines, "## " prefixing section headings - so
/// translators maintain it exactly like every other string and it follows
/// the app language (which defaults to English on first run).
/// One instance at a time; reopening focuses the existing window.
/// </summary>
public sealed class HelpWindow : Window
{
    private static HelpWindow? _open;
    private readonly TextBlock _document;

    public static void ShowGuide(Window owner)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new HelpWindow { Owner = owner };
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private HelpWindow()
    {
        var loc = Localization.LocalizationService.Instance;
        Width = 520; Height = 620;
        MinWidth = 420; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "B.PanelBg");

        _document = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        TypographyManager.SetFontSize(_document, 12.5);
        _document.SetResourceReference(TextBlock.LineHeightProperty, "T.Line19");
        _document.SetResourceReference(TextBlock.ForegroundProperty, "B.TextPrimary");

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(20, 14, 20, 18),
            Content = _document,
        };
        RefreshContent();
        loc.LanguageChanged += RefreshContent;
        Closed += (_, _) => loc.LanguageChanged -= RefreshContent;
    }

    private void RefreshContent()
    {
        var loc = Localization.LocalizationService.Instance;
        loc.ApplyFlowDirection(this);
        Title = loc["Help.Title"];
        _document.Inlines.Clear();

        // "## " lines become bold headings; everything else flows as body.
        foreach (string line in loc["Help.Content"].Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = new Run(line[3..]) { FontWeight = FontWeights.SemiBold };
                heading.SetResourceReference(TextElement.FontSizeProperty, "T.Font13_5");
                _document.Inlines.Add(heading);
            }
            else
            {
                _document.Inlines.Add(new Run(line));
            }
            _document.Inlines.Add(new LineBreak());
        }
    }
}
