using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Sends each translation only to the selected provider. The injectable HTTP
/// client permits deterministic checks without translating users' private text.
/// Cloud providers use web sessions or personal keys; Offline uses a loopback service.
/// Public web endpoints have no availability guarantee and can change or throttle.
/// </summary>
public sealed partial class TranslationClient
{
    private const string BingHost = "https://cn.bing.com";
    private readonly HttpClient _http;
    private readonly HttpClient _localHttp;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _offlineTimeout;

    public TranslationClient(HttpClient http, TimeSpan? requestTimeout = null, HttpClient? localHttp = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _localHttp = localHttp ?? http;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        _offlineTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        if (_requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public async Task<TranslationResult> TranslateAsync(string text, string localeTag,
        string engine, string apiKey = "", string appId = "",
        CancellationToken cancellationToken = default, string offlineEndpoint = "http://127.0.0.1:5000")
    {
        engine ??= string.Empty;
        if (cancellationToken.IsCancellationRequested)
            return Failed(engine, TranslationFailure.Cancelled);
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(localeTag))
            return Failed(engine, TranslationFailure.InvalidInput);
        if (engine is not ("Bing" or "Baidu" or "Google" or "Lingva" or "DeepL" or "Aliyun" or "Offline"))
            return Failed(engine, TranslationFailure.UnsupportedProvider);

        // Use the anonymous Bing and Baidu standard-tier limits. Never truncate
        // or silently split text into additional requests against the user's quota.
        int textLimit = engine is "Bing" or "Baidu" ? 1000 : 5000;
        if (text.Length > textLimit)
            return Failed(engine, TranslationFailure.TextTooLong);
        apiKey = apiKey?.Trim() ?? string.Empty;
        appId = appId?.Trim() ?? string.Empty;
        if (((engine is "Baidu" or "DeepL" or "Aliyun") && apiKey.Length == 0) ||
            ((engine is "Baidu" or "Aliyun") && appId.Length == 0))
            return Failed(engine, TranslationFailure.MissingCredentials);
        if ((engine is "Baidu" or "DeepL" or "Aliyun") && (apiKey.Any(char.IsControl) || appId.Any(char.IsControl)))
            return Failed(engine, TranslationFailure.Authentication);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(engine == "Offline" ? _offlineTimeout : _requestTimeout);
        try
        {
            string? translated = engine switch
            {
                "Bing" => await BingAsync(text, localeTag, timeout.Token).ConfigureAwait(false),
                "Baidu" => await BaiduAsync(text, localeTag, appId, apiKey, timeout.Token).ConfigureAwait(false),
                "Google" => await GoogleAsync(text, localeTag, timeout.Token).ConfigureAwait(false),
                "Lingva" => await LingvaAsync(text, localeTag, timeout.Token).ConfigureAwait(false),
                "DeepL" => await DeepLAsync(text, localeTag, apiKey, timeout.Token).ConfigureAwait(false),
                "Aliyun" => await AliyunAsync(text, localeTag, appId, apiKey, timeout.Token).ConfigureAwait(false),
                "Offline" => await OfflineAsync(text, localeTag, offlineEndpoint, timeout.Token).ConfigureAwait(false),
                _ => null,
            };
            timeout.Token.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(translated)
                ? Failed(engine, TranslationFailure.InvalidResponse)
                : new TranslationResult(engine, translated);
        }
        catch (OperationCanceledException)
        {
            return Failed(engine, cancellationToken.IsCancellationRequested
                ? TranslationFailure.Cancelled : TranslationFailure.Timeout);
        }
        catch (ProviderException exception)
        {
            return Failed(engine, exception.Failure, exception.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Failed(engine, engine == "Offline" ? TranslationFailure.LocalServiceUnavailable : TranslationFailure.Network);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or RegexMatchTimeoutException or FormatException)
        {
            return Failed(engine, TranslationFailure.InvalidResponse);
        }
    }

    private async Task<string?> BingAsync(string text, string tag, CancellationToken cancellationToken)
    {
        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, BingHost + "/translator");
        string page = await ReadResponseAsync(pageRequest, cancellationToken).ConfigureAwait(false);
        string helper = Match(page, @"params_AbusePreventionHelper\s*=\s*(\[[^;\r\n]+\])");
        string ig = Match(page, "IG:\\s*\"([^\"]+)\"");
        string iid = Match(page, "data-iid=\"([^\"]+)\"");
        if (helper.Length == 0 || ig.Length == 0 || iid.Length == 0)
            throw new ProviderException(TranslationFailure.InvalidResponse);

        using var tokenDocument = JsonDocument.Parse(helper);
        JsonElement parts = tokenDocument.RootElement;
        if (parts.GetArrayLength() < 2 || parts[0].ValueKind != JsonValueKind.Number ||
            parts[1].ValueKind != JsonValueKind.String)
            throw new ProviderException(TranslationFailure.InvalidResponse);
        string? token = parts[1].GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new ProviderException(TranslationFailure.InvalidResponse);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            BingHost + "/ttranslatev3?isVertical=1&IG=" + Uri.EscapeDataString(ig) +
            "&IID=" + Uri.EscapeDataString(iid));
        request.Headers.Referrer = new Uri(BingHost + "/translator");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fromLang"] = "auto-detect",
            ["to"] = tag switch { "zh-CN" => "zh-Hans", "zh-TW" => "zh-Hant", _ => BaseLanguage(tag) },
            ["text"] = text,
            ["key"] = parts[0].GetRawText(),
            ["token"] = token,
        });
        using var result = JsonDocument.Parse(
            await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false));
        if (result.RootElement.ValueKind == JsonValueKind.Object &&
            result.RootElement.TryGetProperty("statusCode", out JsonElement status) &&
            status.TryGetInt32(out int statusCode))
            throw new ProviderException(FailureForStatus(statusCode), statusCode);
        return result.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString();
    }

    private async Task<string?> BaiduAsync(string text, string tag, string appId,
        string apiKey, CancellationToken cancellationToken)
    {
        string? language = tag switch
        {
            "zh-CN" => "zh", "zh-TW" => "cht", "en-US" => "en", "ja-JP" => "jp",
            "ko-KR" => "kor", "fr-FR" => "fra", "es-ES" => "spa", "th-TH" => "th",
            "ar-SA" => "ara", "ru-RU" => "ru", "pt-BR" => "pt", "de-DE" => "de",
            "it-IT" => "it", "nl-NL" => "nl", "pl-PL" => "pl", "uk-UA" => "ukr",
            "vi-VN" => "vie", "tr-TR" => "tr", "hi-IN" => "hi", "id-ID" => "id",
            _ => null,
        };
        if (language is null) throw new ProviderException(TranslationFailure.UnsupportedLanguage);
        string salt = Guid.NewGuid().ToString("N");
        // MD5 is required by Baidu's request-signing protocol, not used for storage.
        string sign = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(appId + text + salt + apiKey)));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://fanyi-api.baidu.com/api/trans/vip/translate");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = text, ["from"] = "auto", ["to"] = language,
            ["appid"] = appId, ["salt"] = salt, ["sign"] = sign,
        });
        using var document = JsonDocument.Parse(
            await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false));
        if (document.RootElement.TryGetProperty("error_code", out JsonElement error))
        {
            TranslationFailure failure = error.ToString() switch
            {
                "52001" => TranslationFailure.Timeout,
                "52003" or "54001" or "58000" or "58002" or "90107" => TranslationFailure.Authentication,
                "54000" => TranslationFailure.InvalidInput,
                "54003" or "54005" => TranslationFailure.RateLimited,
                "54004" => TranslationFailure.QuotaExceeded,
                "58001" => TranslationFailure.UnsupportedLanguage,
                _ => TranslationFailure.ProviderUnavailable,
            };
            throw new ProviderException(failure);
        }
        return string.Join("\n", document.RootElement.GetProperty("trans_result").EnumerateArray()
            .Select(item => item.GetProperty("dst").GetString()));
    }

    private async Task<string?> GoogleAsync(string text, string tag, CancellationToken cancellationToken)
    {
        string language = tag is "zh-CN" or "zh-TW" ? tag : BaseLanguage(tag);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl=" +
            Uri.EscapeDataString(language) + "&q=" + Uri.EscapeDataString(text));
        using var document = JsonDocument.Parse(
            await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false));
        var translated = new StringBuilder();
        foreach (JsonElement chunk in document.RootElement[0].EnumerateArray())
        {
            if (chunk.ValueKind == JsonValueKind.Array && chunk.GetArrayLength() > 0 &&
                chunk[0].ValueKind == JsonValueKind.String)
                translated.Append(chunk[0].GetString());
        }
        return translated.ToString();
    }

    private async Task<string?> LingvaAsync(string text, string tag, CancellationToken cancellationToken)
    {
        string language = tag switch { "zh-CN" => "zh", "zh-TW" => "zh_HANT", _ => BaseLanguage(tag) };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://lingva.ml/api/v1/auto/" + Uri.EscapeDataString(language) + "/" + Uri.EscapeDataString(text));
        using var document = JsonDocument.Parse(
            await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false));
        return document.RootElement.GetProperty("translation").GetString();
    }

    private async Task<string?> DeepLAsync(string text, string tag, string apiKey, CancellationToken cancellationToken)
    {
        string language = tag switch
        {
            "zh-CN" => "ZH-HANS", "zh-TW" => "ZH-HANT", "pt-BR" => "PT-BR",
            _ => BaseLanguage(tag).ToUpperInvariant(),
        };
        string host = apiKey.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com" : "https://api.deepl.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, host + "/v2/translate");
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text, ["target_lang"] = language,
        });
        using var document = JsonDocument.Parse(
            await ReadResponseAsync(request, cancellationToken).ConfigureAwait(false));
        return document.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();
    }

    private async Task<string> ReadResponseAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ProviderException(FailureForStatus((int)response.StatusCode), (int)response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BaseLanguage(string tag) => tag.Split('-')[0].ToLowerInvariant();

    private static string Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)).Groups[1].Value;

    private static TranslationFailure FailureForStatus(int statusCode) => statusCode switch
    {
        400 => TranslationFailure.InvalidInput,
        401 or 403 => TranslationFailure.Authentication,
        413 => TranslationFailure.TextTooLong,
        429 => TranslationFailure.RateLimited,
        456 => TranslationFailure.QuotaExceeded,
        _ => TranslationFailure.ProviderUnavailable,
    };

    private static TranslationResult Failed(string provider, TranslationFailure failure, int? statusCode = null)
        => new(provider, null, failure, statusCode);

    private sealed class ProviderException(TranslationFailure failure, int? statusCode = null) : Exception
    {
        public TranslationFailure Failure { get; } = failure;
        public int? StatusCode { get; } = statusCode;
    }
}
