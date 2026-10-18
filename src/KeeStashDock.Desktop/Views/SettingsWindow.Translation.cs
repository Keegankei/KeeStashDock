using System.Windows.Controls;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private bool _updatingTranslationTargets;

    private void RefreshTranslationTargets()
    {
        var cfg = _settings.Current;
        var loc = LocalizationService.Instance;
        string selected = TranslationTargets.Normalize(cfg.TranslationTargetLanguage);
        bool managed = TranslationService.IsManagedOfflineEndpoint(cfg.OfflineTranslationEndpoint);
        _updatingTranslationTargets = true;
        try
        {
            TranslationTargetCombo.Items.Clear();
            TranslationTargetCombo.Items.Add(new ComboBoxItem
            {
                Tag = TranslationTargets.FollowInterface,
                Content = loc["Settings.TranslationTarget.FollowInterface"],
            });
            foreach (var language in TranslationTargets.Languages.Where(language =>
                         TranslationTargets.IsOffered(cfg.TranslationEngine, language.Tag, managed) || language.Tag == selected))
                TranslationTargetCombo.Items.Add(new ComboBoxItem
                {
                    Tag = language.Tag, Content = language.Name,
                    IsEnabled = TranslationTargets.IsOffered(cfg.TranslationEngine, language.Tag, managed),
                });
            TranslationTargetCombo.SelectedItem = TranslationTargetCombo.Items.OfType<ComboBoxItem>()
                .First(item => (string)item.Tag == selected);
            string effective = TranslationTargets.Resolve(cfg);
            TranslationTargetStatus.Text = string.Format(loc["Settings.TranslationTarget.Effective"], TranslationTargets.Name(effective));
            if (!TranslationTargets.IsOffered(cfg.TranslationEngine, effective, managed))
                TranslationTargetStatus.Text += " " + loc["Translate.Error.LanguageModelUnavailable"];
        }
        finally { _updatingTranslationTargets = false; }
    }

    private void OnTranslationTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _updatingTranslationTargets || TranslationTargetCombo.SelectedItem is not ComboBoxItem { Tag: string target }) return;
        CancelTranslationTest();
        _settings.Current.TranslationTargetLanguage = target;
        _settings.Save();
        RefreshTranslationTargets();
    }
}
