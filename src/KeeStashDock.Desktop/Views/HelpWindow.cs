using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// The user guide (Settings > About > "User guide"). Content lives in the
/// locale JSON files as Help.Content - one paragraph per feature, sections
/// separated by blank lines, "## " prefixing section headings - so
/// translators maintain it exactly like every other string and it follows
/// the app language (which defaults to the system language on first run).
/// One instance at a time; reopening focuses the existing window.
/// </summary>
public sealed class HelpWindow : Window
{
    private static HelpWindow? _open;

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
        Localization.LocalizationService.Instance.ApplyFlowDirection(this);
        Localization.LocalizationService.Instance.LanguageChanged +=
            () => Localization.LocalizationService.Instance.ApplyFlowDirection(this);
        var loc = Localization.LocalizationService.Instance;
        Title = loc["Help.Title"];
        Width = 520; Height = 620;
        MinWidth = 420; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "B.PanelBg");

        var doc = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 19,
        };
        doc.SetResourceReference(TextBlock.ForegroundProperty, "B.TextPrimary");

        // "## " lines become bold headings; everything else flows as body.
        foreach (string line in loc["Help.Content"].Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                doc.Inlines.Add(new Run(line[3..])
                { FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
            }
            else
            {
                doc.Inlines.Add(new Run(line));
            }
            doc.Inlines.Add(new LineBreak());
        }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(20, 14, 20, 18),
            Content = doc,
        };
    }
}
