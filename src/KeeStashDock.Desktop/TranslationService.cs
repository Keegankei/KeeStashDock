using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace KeeStashDock.Desktop;

/// <summary>
/// Translates selected text into the application's current UI language.
/// Google, Bing, and Lingva use keyless public endpoints and auto-detect the
/// source language. DeepL uses the user's own API key and selects the Free or
/// Pro endpoint from the key suffix. Network, authentication, throttling, and
/// provider changes are expected failure modes and must surface as a calm
/// in-bubble error instead of crashing the application.
/// </summary>
public static class TranslationService
{
    private static readonly HttpClient Http = new()
    { Timeout = TimeSpan.FromSeconds(8) };

    public static Task<string?> TranslateAsync(
        string text, string localeTag, string engine, string apiKey = "")
        => engine switch
    {
        "Bing"   => BingAsync(text, localeTag),
        "Lingva" => LingvaAsync(text, localeTag),
        "DeepL"  => DeepLAsync(text, localeTag, apiKey),
        _        => GoogleAsync(text, localeTag),
    };

    // ------------------------------------------------------------- Google

    /// <summary>App locale tag -> Google translate language code. Chinese is
    /// the only family where the region changes the SCRIPT, so it keeps the
    /// full tag; everything else wants the bare code.</summary>
    private static string GoogleLang(string tag) => tag switch
    {
        "zh-CN" => "zh-CN",
        "zh-TW" => "zh-TW",
        _ => tag.Split('-')[0],
    };

    private static async Task<string?> GoogleAsync(string text, string tag)
    {
        try
        {
            string url = "https://translate.googleapis.com/translate_a/single" +
                         "?client=gtx&sl=auto&dt=t" +
                         "&tl=" + Uri.EscapeDataString(GoogleLang(tag)) +
                         "&q=" + Uri.EscapeDataString(text);
            string json = await Http.GetStringAsync(url).ConfigureAwait(false);

            // Shape: [ [ ["chunk-translated","chunk-source",...], ... ], ...]
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            foreach (var chunk in doc.RootElement[0].EnumerateArray())
                if (chunk.ValueKind == JsonValueKind.Array &&
                    chunk.GetArrayLength() > 0 &&
                    chunk[0].ValueKind == JsonValueKind.String)
                    sb.Append(chunk[0].GetString());
            string result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch { return null; }
    }

    // --------------------------------------------------------------- Bing

    private static string BingLang(string tag) => tag switch
    {
        "zh-CN" => "zh-Hans",
        "zh-TW" => "zh-Hant",
        _ => tag.Split('-')[0],
    };

    private static async Task<string?> BingAsync(string text, string tag)
    {
        try
        {
            // The Edge browser's public translator token: a short-lived JWT
            // fetched fresh per request (cheap, and dodges expiry handling).
            string token = (await Http.GetStringAsync(
                "https://edge.microsoft.com/translate/auth")
                .ConfigureAwait(false)).Trim();

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.cognitive.microsofttranslator.com/translate" +
                "?api-version=3.0&to=" + Uri.EscapeDataString(BingLang(tag)));
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new[] { new { Text = text } }),
                Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            // Shape: [ { "translations": [ { "text": "..." } ] } ]
            using var doc = JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            string? result = doc.RootElement[0]
                .GetProperty("translations")[0].GetProperty("text").GetString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------- Lingva

    private static string LingvaLang(string tag) => tag switch
    {
        "zh-CN" => "zh",
        "zh-TW" => "zh_HANT",
        _ => tag.Split('-')[0],
    };

    private static async Task<string?> LingvaAsync(string text, string tag)
    {
        try
        {
            string url = "https://lingva.ml/api/v1/auto/" +
                         Uri.EscapeDataString(LingvaLang(tag)) + "/" +
                         Uri.EscapeDataString(text);
            using var doc = JsonDocument.Parse(
                await Http.GetStringAsync(url).ConfigureAwait(false));
            string? result = doc.RootElement.GetProperty("translation").GetString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch { return null; }
    }

    // -------------------------------------------------------------- DeepL

    private static string DeepLLang(string tag) => tag switch
    {
        "zh-CN" => "ZH-HANS",
        "zh-TW" => "ZH-HANT",
        "pt-BR" => "PT-BR",
        _ => tag.Split('-')[0].ToUpperInvariant(),
    };

    private static async Task<string?> DeepLAsync(string text, string tag,
                                                  string apiKey)
    {
        apiKey = apiKey.Trim();
        if (apiKey.Length == 0) return null;   // key required for this engine
        try
        {
            // Free-tier keys end with ":fx" and live on api-free.deepl.com.
            string host = apiKey.EndsWith(":fx", StringComparison.Ordinal)
                ? "https://api-free.deepl.com" : "https://api.deepl.com";
            using var req = new HttpRequestMessage(HttpMethod.Post,
                host + "/v2/translate");
            req.Headers.TryAddWithoutValidation(
                "Authorization", "DeepL-Auth-Key " + apiKey);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["target_lang"] = DeepLLang(tag),
            });
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            // Shape: { "translations": [ { "text": "..." } ] }
            using var doc = JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            string? result = doc.RootElement
                .GetProperty("translations")[0].GetProperty("text").GetString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch { return null; }
    }
}
