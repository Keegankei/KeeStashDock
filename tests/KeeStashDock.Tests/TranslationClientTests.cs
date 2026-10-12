using System.Net;
using System.Security.Cryptography;
using System.Text;
using KeeStashDock.Core.Services;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class TranslationClientTests
{
    [TestMethod]
    public async Task Bing_UsesChinaSessionAndPreservesSpecialCharactersInPostBody()
    {
        using var handler = new FakeHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Response("""
                    <script>var params_AbusePreventionHelper = [12345,"test-session-token",3600];
                    var config={IG:"test-session-id"};</script><div data-iid="translator.5023"></div>
                    """)
                : Response("""[{"translations":[{"text":"translated","to":"zh-Hant"}]}]""")));
        using var http = new HttpClient(handler);
        var client = new TranslationClient(http);

        TranslationResult result = await client.TranslateAsync("Hello & world=+\nnext", "zh-TW", "Bing");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Bing", result.Provider);
        Assert.AreEqual("translated", result.Text);
        Assert.HasCount(2, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request => request.Url.Host == "cn.bing.com"));
        Assert.AreEqual("/translator", handler.Requests[0].Url.AbsolutePath);
        Assert.AreEqual(string.Empty, handler.Requests[0].Body);
        CapturedRequest translation = handler.Requests[1];
        Assert.AreEqual("/ttranslatev3", translation.Url.AbsolutePath);
        Assert.AreEqual("application/x-www-form-urlencoded", translation.ContentType);
        Dictionary<string, string> form = ParseForm(translation.Body);
        Assert.AreEqual("Hello & world=+\nnext", form["text"]);
        Assert.AreEqual("zh-Hant", form["to"]);
        Assert.AreEqual("auto-detect", form["fromLang"]);
        Assert.AreEqual("12345", form["key"]);
        Assert.AreEqual("test-session-token", form["token"]);
    }

    [TestMethod]
    public async Task ChangedBingPage_FailsBeforeSendingSelectedText()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Response("<html>Sign in or retry later</html>")));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "zh-CN", "Bing");

        Assert.AreEqual(TranslationFailure.InvalidResponse, result.Failure);
        Assert.IsNull(result.Text);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual(string.Empty, handler.Requests[0].Body);
    }

    [TestMethod]
    public async Task Baidu_SignsOriginalUtf8TextAndPostsWithoutSendingTheSecret()
    {
        const string apiKey = "synthetic-test-key";
        const string appId = "test-app-id";
        const string source = "Hello & \u4e16\u754c + world";
        using var handler = new FakeHandler((_, _) => Task.FromResult(
            Response("""{"trans_result":[{"dst":"first"},{"dst":"second"}]}""")));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync(
            source, "ja-JP", "Baidu", apiKey, appId);

        Assert.AreEqual("first\nsecond", result.Text);
        CapturedRequest request = handler.Requests.Single();
        Assert.AreEqual("fanyi-api.baidu.com", request.Url.Host);
        Assert.AreEqual(string.Empty, request.Url.Query);
        Dictionary<string, string> form = ParseForm(request.Body);
        Assert.AreEqual(source, form["q"]);
        Assert.AreEqual("jp", form["to"]);
        string expected = Convert.ToHexStringLower(MD5.HashData(
            Encoding.UTF8.GetBytes(appId + source + form["salt"] + apiKey)));
        Assert.AreEqual(expected, form["sign"]);
        Assert.IsFalse(request.Body.Contains(apiKey, StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, request.Authorization);
    }

    [TestMethod]
    [DataRow("54001", TranslationFailure.Authentication)]
    [DataRow("52003", TranslationFailure.Authentication)]
    [DataRow("54003", TranslationFailure.RateLimited)]
    [DataRow("54004", TranslationFailure.QuotaExceeded)]
    [DataRow("58001", TranslationFailure.UnsupportedLanguage)]
    public async Task Baidu_MapsProviderErrorsWithoutReturningRawErrorBodies(string code, TranslationFailure failure)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(
            Response("{\"error_code\":\"" + code + "\",\"error_msg\":\"provider details\"}")));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "zh-CN",
            "Baidu", "synthetic-test-key", "test-app-id");

        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(result.Text);
        Assert.HasCount(1, handler.Requests);
        Assert.IsFalse(result.ToString().Contains("provider details", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("Baidu", "", "test-app-id", TranslationFailure.MissingCredentials)]
    [DataRow("Baidu", "synthetic-test-key", "", TranslationFailure.MissingCredentials)]
    [DataRow("DeepL", "", "", TranslationFailure.MissingCredentials)]
    [DataRow("DeepL", "bad\r\nkey", "", TranslationFailure.Authentication)]
    [DataRow("unknown", "", "", TranslationFailure.UnsupportedProvider)]
    public async Task InvalidConfiguration_DoesNotSendText(string engine, string key, string appId,
        TranslationFailure expected)
    {
        using var handler = new FakeHandler((_, _) => throw new AssertFailedException("No HTTP call expected."));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "zh-CN", engine, key, appId);

        Assert.AreEqual(expected, result.Failure);
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    [DataRow(401, TranslationFailure.Authentication)]
    [DataRow(403, TranslationFailure.Authentication)]
    [DataRow(429, TranslationFailure.RateLimited)]
    [DataRow(503, TranslationFailure.ProviderUnavailable)]
    public async Task HttpFailure_ReportsTheChosenProviderAndNeverFallsBack(int status, TranslationFailure failure)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Response("rejected", (HttpStatusCode)status)));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "zh-CN", "Google");

        Assert.AreEqual("Google", result.Provider);
        Assert.AreEqual(failure, result.Failure);
        Assert.AreEqual(status, result.HttpStatusCode);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual("translate.googleapis.com", handler.Requests[0].Url.Host);
    }

    [TestMethod]
    [DataRow("<html>unavailable</html>")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("null")]
    public async Task MalformedResponses_ReturnTypedFailures(string response)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Response(response)));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "zh-CN", "Google");

        Assert.AreEqual(TranslationFailure.InvalidResponse, result.Failure);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task NetworkFailure_IsDistinguishedFromMalformedResponses()
    {
        using var handler = new FakeHandler((_, _) => throw new HttpRequestException("synthetic connection failure"));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "zh-CN", "Google");
        Assert.AreEqual(TranslationFailure.Network, result.Failure);
    }

    [TestMethod]
    public async Task Timeout_StopsTheInFlightRequestAndReturnsAResult()
    {
        using var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response("{}");
        });
        using var http = new HttpClient(handler);
        var client = new TranslationClient(http, TimeSpan.FromMilliseconds(30));

        TranslationResult result = await client.TranslateAsync("Hello world", "zh-CN", "Bing")
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(TranslationFailure.Timeout, result.Failure);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CallerCancellation_ReturnsCancelledWithoutThrowing()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new FakeHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response("{}");
        });
        using var http = new HttpClient(handler);
        var client = new TranslationClient(http);

        TranslationResult result = await client.TranslateAsync("Hello world", "zh-CN", "Google",
            cancellationToken: cancellation.Token);
        TranslationResult alreadyCancelled = await client.TranslateAsync("Hello world", "zh-CN", "Google",
            cancellationToken: cancellation.Token);

        Assert.AreEqual(TranslationFailure.Cancelled, result.Failure);
        Assert.AreEqual(TranslationFailure.Cancelled, alreadyCancelled.Failure);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task TextLimits_RejectExcessInputWithoutTruncationOrRequests()
    {
        using var handler = new FakeHandler((_, _) => throw new AssertFailedException("No HTTP call expected."));
        using var http = new HttpClient(handler);
        var client = new TranslationClient(http);

        TranslationResult empty = await client.TranslateAsync(" \t", "zh-CN", "Bing");
        TranslationResult tooLong = await client.TranslateAsync(new string('a', 1001), "zh-CN", "Bing");

        Assert.AreEqual(TranslationFailure.InvalidInput, empty.Failure);
        Assert.AreEqual(TranslationFailure.TextTooLong, tooLong.Failure);
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    [DataRow("synthetic-key:fx", "api-free.deepl.com")]
    [DataRow("synthetic-key", "api.deepl.com")]
    public async Task DeepL_UsesThePersonalKeyWithTheCorrectEndpoint(string key, string host)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Response("""{"translations":[{"text":"translated"}]}""")));
        using var http = new HttpClient(handler);

        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello world", "pt-BR", "DeepL", key);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(host, handler.Requests.Single().Url.Host);
        Assert.AreEqual("DeepL-Auth-Key " + key, handler.Requests.Single().Authorization);
        Assert.AreEqual("PT-BR", ParseForm(handler.Requests.Single().Body)["target_lang"]);
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> ParseForm(string body) => body.Split('&')
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    private sealed record CapturedRequest(HttpMethod Method, Uri Url, string Body, string? ContentType, string Authorization);

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Content?.Headers.ContentType?.MediaType, request.Headers.Authorization?.ToString() ?? string.Empty));
            return await respond(request, cancellationToken);
        }
    }
}
