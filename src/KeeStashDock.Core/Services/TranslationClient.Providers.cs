using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeeStashDock.Core.Services;

public sealed partial class TranslationClient
{
    private async Task<string?> AliyunAsync(string text, string tag, string accessKeyId,
        string accessKeySecret, CancellationToken cancellationToken)
    {
        string target = tag switch
        {
            "zh-CN" => "zh", "zh-TW" => "zh-tw", _ => BaseLanguage(tag),
        };
        var common = new Dictionary<string, string>
        {
            ["Action"] = "TranslateGeneral", ["Version"] = "2018-10-12", ["Format"] = "JSON",
            ["AccessKeyId"] = accessKeyId, ["SignatureMethod"] = "HMAC-SHA1", ["SignatureVersion"] = "1.0",
            ["SignatureNonce"] = Guid.NewGuid().ToString("N"),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        var body = new Dictionary<string, string>
        {
            ["FormatType"] = "text", ["Scene"] = "general", ["SourceLanguage"] = "auto",
            ["SourceText"] = text, ["TargetLanguage"] = target,
        };
        common["Signature"] = AliyunRpcSigner.Sign("POST", common.Concat(body), accessKeySecret);
        string query = string.Join("&", common.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://alimt.cn-hangzhou.aliyuncs.com/?" + query)
        {
            Content = new FormUrlEncodedContent(body),
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try { document = JsonDocument.Parse(responseBody); }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(FailureForStatus((int)response.StatusCode), (int)response.StatusCode);
        }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("TranslateGeneralResponse", out JsonElement wrapped)) root = wrapped;
            if (root.TryGetProperty("Code", out JsonElement code) && code.ToString() != "200")
                throw new ProviderException(AliyunFailure(code.ToString(), (int)response.StatusCode),
                    (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new ProviderException(FailureForStatus((int)response.StatusCode), (int)response.StatusCode);
            if (!root.TryGetProperty("Code", out code) || code.ToString() != "200")
                throw new ProviderException(TranslationFailure.InvalidResponse);
            return root.GetProperty("Data").GetProperty("Translated").GetString();
        }
    }

    private static TranslationFailure AliyunFailure(string code, int status)
    {
        if (code.StartsWith("Throttling", StringComparison.OrdinalIgnoreCase)) return TranslationFailure.RateLimited;
        if (code.StartsWith("InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("Signature", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("InvalidSecurityToken", StringComparison.OrdinalIgnoreCase))
            return TranslationFailure.Authentication;
        return code switch
        {
            "10001" => TranslationFailure.Timeout,
            "10003" or "10004" or "10006" => TranslationFailure.InvalidInput,
            "10005" => TranslationFailure.UnsupportedLanguage,
            "10008" => TranslationFailure.TextTooLong,
            "10009" => TranslationFailure.Authentication,
            "10010" or "10013" => TranslationFailure.ServiceNotActivated,
            "QuotaExhausted" or "QuotaExceeded" or "InsufficientBalance" => TranslationFailure.QuotaExceeded,
            _ => status == 200 ? TranslationFailure.ProviderUnavailable : FailureForStatus(status),
        };
    }

    private async Task<string?> OfflineAsync(string text, string tag, string endpoint,
        CancellationToken cancellationToken)
    {
        Uri baseUri = OfflineBaseUri(endpoint);
        using var languagesRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "languages"));
        using var languages = JsonDocument.Parse(
            await ReadLocalResponseAsync(languagesRequest, cancellationToken).ConfigureAwait(false));
        JsonElement root = languages.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw new ProviderException(TranslationFailure.InvalidResponse);

        string[] candidates = tag switch
        {
            "zh-TW" => ["zt", "zh-Hant", "zh_HANT", "zh-TW"],
            "zh-CN" => ["zh", "zh-Hans", "zh-CN"],
            _ => [tag, BaseLanguage(tag)],
        };
        string? target = candidates.Select(candidate => root.EnumerateArray()
            .Select(language => language.GetProperty("code").GetString())
            .FirstOrDefault(code => string.Equals(code, candidate, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(code => code is not null);
        if (target is null)
            throw new ProviderException(TranslationFailure.LanguageModelUnavailable);

        // A listed target without an incoming model cannot translate other languages.
        // Older compatible services may omit targets; their installed code list still applies.
        bool hasTargets = root.EnumerateArray().Any(language => language.TryGetProperty("targets", out _));
        if (hasTargets && !root.EnumerateArray().Any(language =>
            !string.Equals(language.GetProperty("code").GetString(), target, StringComparison.OrdinalIgnoreCase) &&
            language.TryGetProperty("targets", out JsonElement targets) &&
            targets.EnumerateArray().Any(code => string.Equals(code.GetString(), target, StringComparison.OrdinalIgnoreCase))))
            throw new ProviderException(TranslationFailure.LanguageModelUnavailable);

        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "translate"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text, ["source"] = "auto", ["target"] = target, ["format"] = "text",
            }),
        };
        using var result = JsonDocument.Parse(
            await ReadLocalResponseAsync(request, cancellationToken).ConfigureAwait(false));
        return result.RootElement.GetProperty("translatedText").GetString();
    }

    private static Uri OfflineBaseUri(string endpoint)
    {
        endpoint = endpoint?.Trim() ?? string.Empty;
        // Exact loopback names avoid DNS, numeric IP aliases, and accidental remote services.
        if (!Regex.IsMatch(endpoint, @"\Ahttps?://(?:127\.0\.0\.1|localhost|\[::1\])(?::[0-9]+)?(?:/[^?#]*)?\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
            endpoint.Any(char.IsControl) || endpoint.Contains('\\') ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Port == 0)
            throw new ProviderException(TranslationFailure.InvalidConfiguration);
        var builder = new UriBuilder(uri);
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) builder.Host = "127.0.0.1";
        builder.Path = builder.Path.TrimEnd('/') + "/";
        return builder.Uri;
    }

    private async Task<string> ReadLocalResponseAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Production supplies a separate no-proxy/no-redirect client. A redirect is an error,
        // including another loopback address, so private text cannot follow a remote Location.
        using HttpResponseMessage response = await _localHttp.SendAsync(request, cancellationToken).ConfigureAwait(false);
        int status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
            throw new ProviderException(TranslationFailure.InvalidConfiguration, status);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        // The bundled runner emits stable codes. Never return or log error messages,
        // which may quote the input text. Other LibreTranslate services use HTTP errors.
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("code", out JsonElement code) &&
                code.ValueKind == JsonValueKind.String)
            {
                TranslationFailure? failure = code.GetString() switch
                {
                    "language_model_unavailable" => TranslationFailure.LanguageModelUnavailable,
                    "text_too_long" => TranslationFailure.TextTooLong,
                    "invalid_input" => TranslationFailure.InvalidInput,
                    "rate_limited" => TranslationFailure.RateLimited,
                    "provider_unavailable" => TranslationFailure.LocalServiceUnavailable,
                    _ => null,
                };
                if (failure is not null) throw new ProviderException(failure.Value, status);
            }
        }
        catch (JsonException) when (!response.IsSuccessStatusCode) { }
        if (!response.IsSuccessStatusCode)
            throw new ProviderException(status switch
            {
                400 => TranslationFailure.UnsupportedLanguage,
                401 or 403 => TranslationFailure.Authentication,
                413 => TranslationFailure.TextTooLong,
                429 => TranslationFailure.RateLimited,
                _ => TranslationFailure.LocalServiceUnavailable,
            }, status);
        return body;
    }
}
