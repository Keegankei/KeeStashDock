using System.Net.Http;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>Desktop compatibility facade for the testable translation client.</summary>
public static class TranslationService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        // Provider redirects must not forward selected text or credentials to
        // another origin. Bing session cookies remain scoped by the handler.
        AllowAutoRedirect = false,
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = 2 * 1024 * 1024,
    };

    private static readonly HttpClient LocalHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = 2 * 1024 * 1024,
    };

    private static readonly TranslationClient Client = new(Http, localHttp: LocalHttp);

    public static Task<TranslationResult> TranslateDetailedAsync(string text, string localeTag,
        string engine, string apiKey = "", string appId = "",
        CancellationToken cancellationToken = default,
        string offlineEndpoint = AppSettings.DefaultOfflineTranslationEndpoint)
        => Client.TranslateAsync(text, localeTag, engine, apiKey, appId, cancellationToken, offlineEndpoint);

    public static Task<TranslationResult> TranslateDetailedAsync(string text, AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        string key = settings.TranslationEngine switch
        {
            "Baidu" => settings.BaiduTranslationApiKey,
            "DeepL" => settings.TranslationApiKey,
            "Aliyun" => settings.AliyunAccessKeySecret,
            _ => string.Empty,
        };
        string id = settings.TranslationEngine switch
        {
            "Baidu" => settings.TranslationAppId,
            "Aliyun" => settings.AliyunAccessKeyId,
            _ => string.Empty,
        };
        return TranslateDetailedAsync(text, settings.Language, settings.TranslationEngine,
            key, id, cancellationToken, settings.OfflineTranslationEndpoint);
    }

    internal static string FailureMessageKey(TranslationResult result)
    {
        // Anonymous web-session failures cannot be fixed by entering a user key.
        if (result.Failure == TranslationFailure.Authentication &&
            result.Provider is not ("DeepL" or "Baidu" or "Aliyun"))
            return "Translate.Error.ProviderUnavailable";
        return result.Failure switch
        {
            TranslationFailure.Timeout => "Translate.Error.Timeout",
            TranslationFailure.Network => "Translate.Error.Network",
            TranslationFailure.Authentication => "Translate.Error.Authentication",
            TranslationFailure.MissingCredentials => "Translate.Error.MissingCredentials",
            TranslationFailure.RateLimited => "Translate.Error.RateLimited",
            TranslationFailure.QuotaExceeded => "Translate.Error.QuotaExceeded",
            TranslationFailure.TextTooLong => "Translate.Error.TextTooLong",
            TranslationFailure.UnsupportedLanguage => "Translate.Error.UnsupportedLanguage",
            TranslationFailure.InvalidConfiguration => "Translate.Error.InvalidConfiguration",
            TranslationFailure.LocalServiceUnavailable => "Translate.Error.LocalServiceUnavailable",
            TranslationFailure.LanguageModelUnavailable => "Translate.Error.LanguageModelUnavailable",
            TranslationFailure.ServiceNotActivated => "Translate.Error.ServiceNotActivated",
            TranslationFailure.ProviderUnavailable or TranslationFailure.InvalidResponse
                or TranslationFailure.UnsupportedProvider => "Translate.Error.ProviderUnavailable",
            _ => "Translate.Failed",
        };
    }

    public static async Task<string?> TranslateAsync(string text, string localeTag,
        string engine, string apiKey = "", CancellationToken cancellationToken = default)
        => (await TranslateDetailedAsync(text, localeTag, engine, apiKey,
            cancellationToken: cancellationToken).ConfigureAwait(false)).Text;
}
