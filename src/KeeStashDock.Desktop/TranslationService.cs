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

    public static async Task<TranslationResult> TranslateDetailedAsync(string text, string localeTag,
        string engine, string apiKey = "", string appId = "",
        CancellationToken cancellationToken = default,
        string offlineEndpoint = AppSettings.DefaultOfflineTranslationEndpoint)
    {
        var components = new ComponentInstallService();
        return await TranslateWithOfflineStartupAsync(
            () => Client.TranslateAsync(text, localeTag, engine, apiKey, appId, cancellationToken, offlineEndpoint),
            components.StartOfflineAsync,
            () => components.GetStatus(ComponentKind.OfflineTranslation) == ComponentStatus.Ready,
            engine == "Offline" && IsManagedOfflineEndpoint(offlineEndpoint), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<TranslationResult> TranslateWithOfflineStartupAsync(
        Func<Task<TranslationResult>> translate,
        Func<CancellationToken, Task<ComponentInstallResult>> start,
        Func<bool> isInstalled, bool allowStartup, CancellationToken cancellationToken)
    {
        // Keep an already working compatible server intact. Start our installed engine
        // only after a connection failure, never after an HTTP response or a provider error.
        TranslationResult result = await translate().ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            return new(result.Provider, null, TranslationFailure.Cancelled);
        if (!allowStartup || result.Failure != TranslationFailure.LocalServiceUnavailable ||
            result.HttpStatusCode is not null || !isInstalled())
            return result;
        ComponentInstallResult started = await start(cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
            return new(result.Provider, null, started.Failure == ComponentInstallFailure.Cancelled
                ? TranslationFailure.Cancelled : TranslationFailure.LocalServiceUnavailable);
        return await translate().ConfigureAwait(false);
    }

    internal static bool IsManagedOfflineEndpoint(string endpoint)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp && uri.Host == "127.0.0.1" && uri.Port == 5000
            && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);

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
