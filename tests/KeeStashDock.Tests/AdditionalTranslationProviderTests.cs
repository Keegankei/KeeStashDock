using System.Net;
using System.Text;
using KeeStashDock.Core.Services;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class AdditionalTranslationProviderTests
{
    private const string Languages = """[{"code":"en","targets":["en","zh"]},{"code":"zh","targets":["en","zh"]}]""";

    [TestMethod]
    public void AliyunSigning_MatchesOfficialRpcKnownVector()
    {
        // Published testid/testsecret example, independent of generated timestamps and request code:
        // https://www.alibabacloud.com/help/en/sdk/product-overview/rpc-mechanism
        var parameters = new Dictionary<string, string>
        {
            ["RegionId"] = "cn-beijing", ["Timestamp"] = "2023-03-13T08:34:30Z",
            ["AccessKeyId"] = "testid", ["Action"] = "DescribeDedicatedHosts", ["Format"] = "JSON",
            ["SignatureMethod"] = "HMAC-SHA1", ["SignatureNonce"] = "edb2b34af0af9a6d14deaf7c1a5315eb",
            ["SignatureVersion"] = "1.0", ["Version"] = "2014-05-26",
        };
        Assert.AreEqual("9NaGiOspFP5UPcwX8Iwt2YJXXuk=", AliyunRpcSigner.Sign("GET", parameters, "testsecret"));
    }

    [TestMethod]
    public async Task Aliyun_PostsSourceOnlyInBodyAndSignsEveryParameter()
    {
        const string source = "Hello & \u4e16\u754c + * ~\nnext=word";
        using var handler = new Handler(_ => Response("""{"Code":200,"Data":{"Translated":"translated"}}"""));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync(source, "zh-TW", "Aliyun",
            "testsecret", "testid");

        Assert.AreEqual("translated", result.Text);
        Captured request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("https://alimt.cn-hangzhou.aliyuncs.com", request.Uri.GetLeftPart(UriPartial.Authority));
        var query = ParseForm(request.Uri.Query.TrimStart('?'));
        var body = ParseForm(request.Body);
        Assert.AreEqual("TranslateGeneral", query["Action"]);
        Assert.AreEqual("2018-10-12", query["Version"]);
        Assert.AreEqual("testid", query["AccessKeyId"]);
        Assert.AreEqual("auto", body["SourceLanguage"]);
        Assert.AreEqual("zh-tw", body["TargetLanguage"]);
        Assert.AreEqual("general", body["Scene"]);
        Assert.AreEqual("text", body["FormatType"]);
        Assert.AreEqual(source, body["SourceText"]);
        Assert.IsFalse(query.ContainsKey("SourceText"));
        Assert.IsFalse(request.Uri.ToString().Contains("testsecret", StringComparison.Ordinal));
        Assert.IsFalse(request.Body.Contains("testsecret", StringComparison.Ordinal));
        Assert.AreEqual(query["Signature"], AliyunRpcSigner.Sign("POST", query.Concat(body), "testsecret"));
        body["SourceText"] = "tampered";
        Assert.AreNotEqual(query["Signature"], AliyunRpcSigner.Sign("POST", query.Concat(body), "testsecret"));
    }

    [TestMethod]
    [DataRow("10001", 200, TranslationFailure.Timeout)]
    [DataRow("10005", 200, TranslationFailure.UnsupportedLanguage)]
    [DataRow("10008", 200, TranslationFailure.TextTooLong)]
    [DataRow("10009", 200, TranslationFailure.Authentication)]
    [DataRow("10010", 200, TranslationFailure.ServiceNotActivated)]
    [DataRow("10013", 200, TranslationFailure.ServiceNotActivated)]
    [DataRow("SignatureDoesNotMatch", 400, TranslationFailure.Authentication)]
    [DataRow("InvalidAccessKeyId.NotFound", 404, TranslationFailure.Authentication)]
    [DataRow("Throttling.User", 400, TranslationFailure.RateLimited)]
    [DataRow("QuotaExceeded", 403, TranslationFailure.QuotaExceeded)]
    public async Task Aliyun_MapsApiErrorsBeforeHttpStatusWithoutExposingRemoteDetails(string code, int status,
        TranslationFailure failure)
    {
        using var handler = new Handler(_ => Response(
            "{\"Code\":\"" + code + "\",\"Message\":\"private provider details\"}", status));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Aliyun",
            "testsecret", "testid");
        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(result.Text);
        Assert.IsFalse(result.ToString().Contains("private provider details", StringComparison.Ordinal));
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("", "testid")]
    [DataRow("testsecret", "")]
    public async Task Aliyun_MissingCredentialsMakesNoRequest(string secret, string id)
    {
        using var handler = new Handler(_ => throw new AssertFailedException("No request expected."));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Aliyun", secret, id);
        Assert.AreEqual(TranslationFailure.MissingCredentials, result.Failure);
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:5000", "127.0.0.1", "/")]
    [DataRow("http://localhost:5000/service", "127.0.0.1", "/service/")]
    [DataRow("http://[::1]:5000/", "[::1]", "/")]
    public async Task Offline_UsesOnlyDedicatedLocalClientAndInstalledModels(string endpoint, string expectedHost,
        string expectedPath)
    {
        using var cloudHandler = new Handler(_ => throw new AssertFailedException("Cloud must never receive offline text."));
        using var localHandler = new Handler(request => Response(request.Method == HttpMethod.Get
            ? Languages : """{"translatedText":"\u672c\u5730\u7ed3\u679c"}"""));
        using var cloud = new HttpClient(cloudHandler);
        using var local = new HttpClient(localHandler);
        TranslationResult result = await new TranslationClient(cloud, localHttp: local).TranslateAsync(
            "Hello & +\nworld", "zh-CN", "Offline", "unused-secret", "unused-id", offlineEndpoint: endpoint);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("\u672c\u5730\u7ed3\u679c", result.Text);
        Assert.HasCount(0, cloudHandler.Requests);
        Assert.HasCount(2, localHandler.Requests);
        Assert.IsTrue(localHandler.Requests.All(request => request.Uri.Host == expectedHost));
        Assert.AreEqual(expectedPath + "languages", localHandler.Requests[0].Uri.AbsolutePath);
        Assert.AreEqual(string.Empty, localHandler.Requests[0].Body);
        Captured post = localHandler.Requests[1];
        Assert.AreEqual(expectedPath + "translate", post.Uri.AbsolutePath);
        var form = ParseForm(post.Body);
        Assert.AreEqual("Hello & +\nworld", form["q"]);
        Assert.AreEqual("auto", form["source"]);
        Assert.AreEqual("zh", form["target"]);
        Assert.AreEqual("text", form["format"]);
        Assert.IsFalse(post.Body.Contains("unused-", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("https://libretranslate.com")]
    [DataRow("http://192.168.0.1:5000")]
    [DataRow("http://127.0.0.1.example.com:5000")]
    [DataRow("http://localhost.example.com:5000")]
    [DataRow("http://127.0.0.1:5000@remote.example")]
    [DataRow("http://user:password@127.0.0.1:5000")]
    [DataRow("http://127.0.0.1:5000?redirect=https://remote.example")]
    [DataRow("http://127.0.0.1:5000#fragment")]
    [DataRow("http://2130706433:5000")]
    [DataRow("http://127.1:5000")]
    [DataRow("http://[::ffff:127.0.0.1]:5000")]
    [DataRow("http://127.0.0.1:0")]
    [DataRow("file:///127.0.0.1")]
    [DataRow("")]
    public async Task Offline_RejectsRemoteAndAmbiguousEndpointsBeforeAnyRequest(string endpoint)
    {
        using var handler = new Handler(_ => throw new AssertFailedException("No request expected."));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline",
            offlineEndpoint: endpoint);
        Assert.AreEqual(TranslationFailure.InvalidConfiguration, result.Failure);
        Assert.HasCount(0, handler.Requests);
    }

    [TestMethod]
    [DataRow("zh-TW")]
    [DataRow("ja-JP")]
    public async Task Offline_MissingTargetModelNeverSendsText(string tag)
    {
        using var handler = new Handler(_ => Response(Languages));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", tag, "Offline");
        Assert.AreEqual(TranslationFailure.LanguageModelUnavailable, result.Failure);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
    }

    [TestMethod]
    [DataRow("zt")]
    [DataRow("zh-Hant")]
    public async Task Offline_TraditionalChineseUsesAdvertisedTraditionalModel(string target)
    {
        using var handler = new Handler(request => Response(request.Method == HttpMethod.Get
            ? "[{\"code\":\"en\",\"targets\":[\"" + target + "\"]},{\"code\":\"" + target + "\",\"targets\":[\"en\"]}]"
            : """{"translatedText":"\u7e41\u9ad4\u4e2d\u6587"}"""));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-TW", "Offline");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(target, ParseForm(handler.Requests[1].Body)["target"]);
    }

    [TestMethod]
    public async Task Offline_ListedTargetWithoutIncomingModelNeverSendsText()
    {
        using var handler = new Handler(_ => Response("""[{"code":"en","targets":["en"]},{"code":"zh","targets":["en","zh"]}]"""));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline");
        Assert.AreEqual(TranslationFailure.LanguageModelUnavailable, result.Failure);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task Offline_RedirectIsRejectedBeforeSendingText()
    {
        using var handler = new Handler(_ =>
        {
            HttpResponseMessage response = Response(string.Empty, 307);
            response.Headers.Location = new Uri("https://remote.example/translate");
            return response;
        });
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline");
        Assert.AreEqual(TranslationFailure.InvalidConfiguration, result.Failure);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(string.Empty, handler.Requests[0].Body);
    }

    [TestMethod]
    public async Task Offline_StoppedServiceHasActionableFailure()
    {
        using var handler = new Handler(_ => throw new HttpRequestException("Connection refused"));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline");
        Assert.AreEqual(TranslationFailure.LocalServiceUnavailable, result.Failure);
    }

    [TestMethod]
    [DataRow("language_model_unavailable", TranslationFailure.LanguageModelUnavailable)]
    [DataRow("text_too_long", TranslationFailure.TextTooLong)]
    [DataRow("invalid_input", TranslationFailure.InvalidInput)]
    [DataRow("rate_limited", TranslationFailure.RateLimited)]
    [DataRow("provider_unavailable", TranslationFailure.LocalServiceUnavailable)]
    public async Task Offline_RunnerErrorsHaveTypedFailuresWithoutReturningInput(string code, TranslationFailure failure)
    {
        using var handler = new Handler(request => Response(request.Method == HttpMethod.Get ? Languages
            : "{\"code\":\"" + code + "\",\"error\":\"private input omitted\"}",
            request.Method == HttpMethod.Get ? 200 : 400));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline");
        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(result.Text);
        Assert.IsFalse(result.ToString().Contains("private input", StringComparison.Ordinal));
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task Offline_TimeoutCancelsLocalInferenceWithoutFallback()
    {
        using var handler = new SlowLocalHandler();
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http, TimeSpan.FromMilliseconds(30))
            .TranslateAsync("Hello", "zh-CN", "Offline").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(TranslationFailure.Timeout, result.Failure);
        Assert.AreEqual(2, handler.RequestCount);
        Assert.IsTrue(handler.InferenceCancelled);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[{\"code\":null}]")]
    public async Task Offline_InvalidLanguageMetadataCannotSendText(string metadata)
    {
        using var handler = new Handler(_ => Response(metadata));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline");
        Assert.IsFalse(result.IsSuccess);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(string.Empty, handler.Requests[0].Body);
    }

    [TestMethod]
    public async Task Offline_CancellationAfterModelCheckPreventsTextSubmission()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(_ => { cancellation.Cancel(); return Response(Languages); });
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync("Hello", "zh-CN", "Offline",
            cancellationToken: cancellation.Token);
        Assert.AreEqual(TranslationFailure.Cancelled, result.Failure);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("Aliyun")]
    [DataRow("Offline")]
    public async Task NewProviders_RejectOversizeInputBeforeRequests(string provider)
    {
        using var handler = new Handler(_ => throw new AssertFailedException("No request expected."));
        using var http = new HttpClient(handler);
        TranslationResult result = await new TranslationClient(http).TranslateAsync(new string('a', 5001), "zh-CN",
            provider, "testsecret", "testid");
        Assert.AreEqual(TranslationFailure.TextTooLong, result.Failure);
        Assert.HasCount(0, handler.Requests);
    }

    private static HttpResponseMessage Response(string body, int status = 200)
        => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> ParseForm(string form) => form.Split('&')
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    private sealed record Captured(HttpMethod Method, Uri Uri, string Body);

    private sealed class SlowLocalHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool InferenceCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Get) return Response(Languages);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { InferenceCancelled = true; throw; }
            throw new AssertFailedException("Inference must time out.");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!, request.Content is null
                ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
