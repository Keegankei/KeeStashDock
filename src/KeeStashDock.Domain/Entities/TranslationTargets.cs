namespace KeeStashDock.Domain.Entities;

/// <summary>Stable target identifiers independent of the installed UI language files.</summary>
public static class TranslationTargets
{
    public const string FollowInterface = "FollowInterface";
    public sealed record Language(string Tag, string Name);

    public static IReadOnlyList<Language> Languages { get; } = Array.AsReadOnly(new Language[]
    {
        new("en-US", "English"), new("zh-CN", "\u7b80\u4f53\u4e2d\u6587"), new("zh-TW", "\u7e41\u9ad4\u4e2d\u6587"),
        new("ja-JP", "\u65e5\u672c\u8a9e"), new("ko-KR", "\ud55c\uad6d\uc5b4"), new("de-DE", "Deutsch"),
        new("fr-FR", "Fran\u00e7ais"), new("es-ES", "Espa\u00f1ol"), new("pt-BR", "Portugu\u00eas"),
        new("it-IT", "Italiano"), new("nl-NL", "Nederlands"), new("pl-PL", "Polski"),
        new("uk-UA", "\u0423\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430"), new("ru-RU", "\u0420\u0443\u0441\u0441\u043a\u0438\u0439"), new("tr-TR", "T\u00fcrk\u00e7e"),
        new("ar-SA", "\u0627\u0644\u0639\u0631\u0628\u064a\u0629"), new("hi-IN", "\u0939\u093f\u0928\u094d\u0926\u0940"), new("id-ID", "Bahasa Indonesia"),
        new("th-TH", "\u0e44\u0e17\u0e22"), new("vi-VN", "Ti\u1ebfng Vi\u1ec7t"),
    });

    public static string Normalize(string? target) => Languages
        .FirstOrDefault(language => string.Equals(language.Tag, target?.Trim(), StringComparison.OrdinalIgnoreCase))?.Tag
        ?? FollowInterface;

    public static string Resolve(AppSettings settings, string? temporaryTarget = null)
    {
        string target = Normalize(temporaryTarget ?? settings.TranslationTargetLanguage);
        return target == FollowInterface ? settings.Language : target;
    }

    public static string Name(string tag) => Languages.FirstOrDefault(language => language.Tag == tag)?.Name ?? tag;

    // The bundled offline runtime currently has two directions. A compatible custom
    // local server is checked against /languages by TranslationClient at request time.
    public static bool IsOffered(string provider, string tag, bool managedOffline = true)
        => Languages.Any(language => language.Tag == tag)
            && (provider != "Offline" || !managedOffline || tag is "en-US" or "zh-CN");
}
