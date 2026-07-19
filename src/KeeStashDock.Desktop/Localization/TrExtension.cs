using System.Windows.Data;
using System.Windows.Markup;

namespace KeeStashDock.Desktop.Localization;

/// <summary>
/// XAML markup extension for localized strings:  Text="{loc:Tr QuickPanel.Empty}"
///
/// It resolves to a Binding on LocalizationService.Instance[Key]; because the
/// service raises PropertyChanged("Item[]") on language switch, every control
/// using {loc:Tr} updates instantly - this single class IS the hot-switch UX.
/// Engineering rule: hard-coded UI strings are forbidden; every user-visible
/// string must go through Tr (enforced in code review).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
