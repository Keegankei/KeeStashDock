using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeeStashDock.Infrastructure.Updates;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class ReleaseUpdateServiceTests
{
    private const string Version = "1.3.0";
    private const string ArchiveName = "KeeStashDock-1.3.0-win-x64.zip";
    private const string ReleaseUrl = ReleaseUpdateService.RepositoryUrl + "/releases/tag/v1.3.0";
    private const string ArchiveUrl = ReleaseUpdateService.RepositoryUrl + "/releases/download/v1.3.0/" + ArchiveName;

    [TestMethod]
    public async Task CheckUsesStableVersionOrderingAndOfficialMatchingAssets()
    {
        using var http = Client(_ => Json(Metadata()));
        using var service = new ReleaseUpdateService(http);
        ReleaseUpdateInfo? result = await service.CheckAsync("1.2.9+buildhash");
        Assert.IsNotNull(result);
        Assert.AreEqual(Version, result.Version);
        Assert.AreEqual(new Uri(ArchiveUrl), result.ArchiveUrl);
        Assert.IsNull(await service.CheckAsync("1.3.0.0"));
        Assert.IsNull(await service.CheckAsync("1.10.0"), "Version comparison must be numeric.");
    }

    [TestMethod]
    public async Task DraftsAndPrereleasesAreIgnored()
    {
        foreach (bool draft in new[] { false, true })
        {
            using var http = Client(_ => Json(Metadata(draft: draft, prerelease: !draft)));
            using var service = new ReleaseUpdateService(http);
            Assert.IsNull(await service.CheckAsync("1.2.1"));
        }
    }

    [TestMethod]
    public async Task MissingAssetAndUntrustedAssetAreDistinctFromNoUpdate()
    {
        using var missingClient = Client(_ => Json(Metadata(includeChecksum: false)));
        using var missing = new ReleaseUpdateService(missingClient);
        var missingError = await Assert.ThrowsAsync<UpdateException>(() => missing.CheckAsync("1.2.1"));
        Assert.AreEqual(UpdateFailure.MissingAssets, missingError.Failure);

        using var unsafeClient = Client(_ => Json(Metadata(archiveUrl: "https://untrusted.invalid/app.zip")));
        using var unsafeService = new ReleaseUpdateService(unsafeClient);
        var unsafeError = await Assert.ThrowsAsync<UpdateException>(() => unsafeService.CheckAsync("1.2.1"));
        Assert.AreEqual(UpdateFailure.InvalidMetadata, unsafeError.Failure);
    }

    [TestMethod]
    public async Task RateLimitNetworkAndMalformedMetadataAreNotReportedAsUpToDate()
    {
        using var limitedClient = Client(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var limited = new ReleaseUpdateService(limitedClient);
        Assert.AreEqual(UpdateFailure.RateLimited,
            (await Assert.ThrowsAsync<UpdateException>(() => limited.CheckAsync("1.2.1"))).Failure);
        using var invalidClient = Client(_ => Json("{ broken"));
        using var invalid = new ReleaseUpdateService(invalidClient);
        Assert.AreEqual(UpdateFailure.InvalidMetadata,
            (await Assert.ThrowsAsync<UpdateException>(() => invalid.CheckAsync("1.2.1"))).Failure);
        using var unavailableClient = Client(_ => throw new HttpRequestException("Fixture network failure"));
        using var unavailable = new ReleaseUpdateService(unavailableClient);
        Assert.AreEqual(UpdateFailure.Network,
            (await Assert.ThrowsAsync<UpdateException>(() => unavailable.CheckAsync("1.2.1"))).Failure);
    }

    [TestMethod]
    public async Task RateLimitedApiUsesVerifiedOfficialPageWithoutDownloadingArchive_AndBacksOff()
    {
        var requests = new List<(string Url, string Method)>();
        using var http = Client(request =>
        {
            Assert.IsNull(request.Headers.Authorization, "The public fallback must not require credentials.");
            requests.Add((request.RequestUri!.AbsoluteUri, request.Method.Method));
            return FallbackReply(request);
        });
        using var service = new ReleaseUpdateService(http);
        var phases = new List<UpdateCheckPhase>();
        ReleaseUpdateInfo? result = await service.CheckAsync("1.2.2", progress: new CheckProgress(phases.Add));
        Assert.IsNotNull(result);
        Assert.AreEqual(Version, result.Version);
        Assert.IsTrue(result.FromReleasePage);
        Assert.AreEqual(200L, result.ArchiveSize);
        Assert.AreEqual(new Uri(ReleaseUrl), result.ReleaseUrl);
        CollectionAssert.AreEqual(new[] { UpdateCheckPhase.ReleasePageFallback }, phases);
        Assert.AreEqual(5, requests.Count);
        Assert.IsTrue(requests.Contains((ArchiveUrl, "HEAD")));
        Assert.IsFalse(requests.Contains((ArchiveUrl, "GET")), "A check may inspect headers, but cannot download a ZIP.");
        Assert.AreSame(result, await service.CheckAsync("1.2.2"));
        Assert.AreEqual(5, requests.Count, "Repeated checks reuse verified results for 30 seconds.");
        Assert.IsNotNull(await service.CheckAsync("1.2.1"));
        Assert.AreEqual(1, requests.Count(item => item.Url == ReleaseUpdateService.ReleaseApiUrl),
            "A different comparison must still respect the API rate-limit backoff.");
    }

    [TestMethod]
    public async Task OfficialRedirectCanConfirmCurrentVersionWithoutAssetRequests()
    {
        var requests = new List<string>();
        using var http = Client(request => { requests.Add(request.RequestUri!.AbsoluteUri); return FallbackReply(request); });
        using var service = new ReleaseUpdateService(http);
        Assert.IsNull(await service.CheckAsync(Version));
        Assert.AreEqual(3, requests.Count);
        Assert.IsFalse(requests.Any(url => url.Contains("/releases/download/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ApiConnectionFailureCanUseSameOfficialFallback()
    {
        using var http = Client(request => request.RequestUri!.AbsoluteUri == ReleaseUpdateService.ReleaseApiUrl
            ? throw new HttpRequestException("Fixture API connection failure") : FallbackReply(request));
        using var service = new ReleaseUpdateService(http);
        Assert.IsTrue((await service.CheckAsync("1.2.2"))!.FromReleasePage);
    }

    [TestMethod]
    [DataRow("https://attacker.invalid/releases/tag/v1.3.0")]
    [DataRow("https://github.com/Other/Repository/releases/tag/v1.3.0")]
    [DataRow("http://github.com/Keegankei/KeeStashDock/releases/tag/v1.3.0")]
    [DataRow("https://github.com/Keegankei/KeeStashDock/releases/tag/v1.3.0-rc.1")]
    [DataRow("https://github.com/Keegankei/KeeStashDock/releases/tag/v1.3.0?redirect=other")]
    [DataRow("https://github.com/Keegankei/KeeStashDock/releases/tag/v1.3.0%2Fextra")]
    [DataRow("https://github.com/login")]
    public async Task UnsafeOrUnsupportedReleaseRedirectIsRejectedBeforeFollowing(string destination)
    {
        var requests = new List<string>();
        using var http = Client(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.AbsoluteUri == ReleaseUpdateService.LatestReleaseUrl
                ? Redirect(destination) : FallbackReply(request);
        });
        using var service = new ReleaseUpdateService(http);
        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync("1.2.2"));
        Assert.IsTrue(error.Failure is UpdateFailure.InvalidMetadata or UpdateFailure.UnsupportedVersion);
        CollectionAssert.AreEqual(new[] { ReleaseUpdateService.ReleaseApiUrl, ReleaseUpdateService.LatestReleaseUrl }, requests);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MissingOrInvalidFallbackChecksumNeverEnablesAnUpdate(bool missing)
    {
        var requests = new List<string>();
        using var http = Client(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            return url == ArchiveUrl + ".sha256"
                ? new HttpResponseMessage(missing ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                    { Content = new StringContent("not the expected checksum") }
                : FallbackReply(request);
        });
        using var service = new ReleaseUpdateService(http);
        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync("1.2.2"));
        Assert.AreEqual(missing ? UpdateFailure.MissingAssets : UpdateFailure.InvalidMetadata, error.Failure);
        Assert.IsFalse(requests.Contains(ArchiveUrl), "A bad checksum cannot fall through to a download or another source.");
    }

    [TestMethod]
    public async Task FailedChecksBackOffAndCanceledCachedCheckStillHonorsCancellation()
    {
        int requests = 0;
        using var http = Client(_ => { requests++; return new(HttpStatusCode.TooManyRequests); });
        using var service = new ReleaseUpdateService(http);
        await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync("1.2.2"));
        Assert.AreEqual(2, requests);
        await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync("1.2.2"));
        Assert.AreEqual(2, requests, "A rapid retry must not produce a rate-limit storm.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.CheckAsync("1.2.2", cancelled.Token));
    }

    [TestMethod]
    public async Task FallbackChecksumBodyHonorsCheckDeadlineAndUserCancellation()
    {
        foreach (bool userCancels in new[] { false, true })
        {
            var stalled = new StallingStream();
            using var http = Client(request => request.RequestUri!.AbsoluteUri == ArchiveUrl + ".sha256"
                ? new(HttpStatusCode.OK) { Content = new StreamContent(stalled) } : FallbackReply(request));
            using var service = new ReleaseUpdateService(http,
                checkTimeout: userCancels ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(180));
            using var cancellation = new CancellationTokenSource();
            Task<ReleaseUpdateInfo?> check = service.CheckAsync("1.2.2", cancellation.Token);
            await stalled.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (userCancels)
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => check);
            }
            else
                Assert.AreEqual(UpdateFailure.Network, (await Assert.ThrowsAsync<UpdateException>(() => check)).Failure);
        }
    }

    [TestMethod]
    public async Task StalledApiBodyFallsBackBeforeTheOverallCheckDeadline()
    {
        using var http = Client(request => request.RequestUri!.AbsoluteUri == ReleaseUpdateService.ReleaseApiUrl
            ? new(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) } : FallbackReply(request));
        using var service = new ReleaseUpdateService(http, checkTimeout: TimeSpan.FromSeconds(1));
        ReleaseUpdateInfo? result = await service.CheckAsync("1.2.2");
        Assert.IsNotNull(result);
        Assert.IsTrue(result.FromReleasePage, "A stalled API body must leave time for the official page fallback.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task FallbackRejectsMissingOrOversizedArchiveHeaders(bool missing)
    {
        using var http = Client(request =>
        {
            if (request.RequestUri!.AbsoluteUri != ArchiveUrl) return FallbackReply(request);
            Assert.AreEqual(HttpMethod.Head, request.Method);
            var response = new HttpResponseMessage(missing ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = ReleaseUpdateService.MaximumArchiveBytes + 1;
            return response;
        });
        using var service = new ReleaseUpdateService(http);
        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => service.CheckAsync("1.2.2"));
        Assert.AreEqual(missing ? UpdateFailure.MissingAssets : UpdateFailure.InvalidMetadata, error.Failure);
    }

    [TestMethod]
    public async Task FallbackWithoutArchiveLengthDoesNotReadItsBodyOrInventASize()
    {
        var archiveBody = new StallingStream();
        using var http = Client(request => request.RequestUri!.AbsoluteUri == ArchiveUrl
            ? new(HttpStatusCode.OK) { Content = new StreamContent(archiveBody) } : FallbackReply(request));
        using var service = new ReleaseUpdateService(http);
        ReleaseUpdateInfo? result = await service.CheckAsync("1.2.2");
        Assert.IsNotNull(result);
        Assert.IsNull(result.ArchiveSize);
        Assert.IsFalse(archiveBody.ReadStarted.Task.IsCompleted, "A HEAD check cannot read ZIP bytes.");
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage FallbackReply(HttpRequestMessage request)
    {
        string url = request.RequestUri!.AbsoluteUri;
        if (url == ReleaseUpdateService.ReleaseApiUrl)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds().ToString());
            return response;
        }
        if (url == ReleaseUpdateService.LatestReleaseUrl) return Redirect(ReleaseUrl);
        if (url == ReleaseUrl) return new(HttpStatusCode.OK) { Content = new StringContent("Fixture release page") };
        if (url == ArchiveUrl + ".sha256") return new(HttpStatusCode.OK)
            { Content = new StringContent(new string('a', 64) + "  " + ArchiveName) };
        if (url == ArchiveUrl)
        {
            Assert.AreEqual(HttpMethod.Head, request.Method);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = 200;
            return response;
        }
        throw new AssertFailedException("Unexpected fallback request: " + url);
    }

    private sealed class CheckProgress(Action<UpdateCheckPhase> report) : IProgress<UpdateCheckPhase>
    { public void Report(UpdateCheckPhase value) => report(value); }

    [TestMethod]
    public async Task ValidPackageIsStagedAndEveryDeclaredFileVerified()
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive);
        var progress = new List<double>();
        StagedUpdate result = await fixture.Service.DownloadAsync(Info(archive), fixture.Root,
            new InlineProgress(progress.Add));
        Assert.AreEqual(Version, result.Manifest.Version);
        Assert.AreEqual("Fixture application", File.ReadAllText(Path.Combine(result.PackageDirectory, "KeeStashDock.exe")));
        Assert.AreEqual("Fixture license", File.ReadAllText(Path.Combine(result.PackageDirectory, "Licenses", "test.txt")));
        result.Manifest.VerifyFiles(result.PackageDirectory);
        Assert.AreEqual(1, progress.Last());
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(result.PackageDirectory)!, "download.zip")));
    }

    [TestMethod]
    public async Task HashMismatchAndCancellationLeaveNoUsableStage()
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive, digest: new string('0', 64));
        var error = await Assert.ThrowsAsync<UpdateException>(() => fixture.Service.DownloadAsync(Info(archive), fixture.Root));
        Assert.AreEqual(UpdateFailure.ChecksumMismatch, error.Failure);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(fixture.Root).Length);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.DownloadAsync(Info(archive), fixture.Root,
            cancellationToken: canceled.Token));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(fixture.Root).Length);
    }

    [TestMethod]
    public async Task DiscardRemovesOnlyTheVerifiedStageAndLeavesItsRootAndOtherStages()
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive);
        StagedUpdate discarded = await fixture.Service.DownloadAsync(Info(archive), fixture.Root);
        StagedUpdate retained = await fixture.Service.DownloadAsync(Info(archive), fixture.Root);
        Assert.IsTrue(ReleaseUpdateService.DiscardStage(discarded, fixture.Root));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(discarded.PackageDirectory)));
        Assert.IsTrue(Directory.Exists(fixture.Root));
        retained.Manifest.VerifyFiles(retained.PackageDirectory);
        Assert.IsTrue(ReleaseUpdateService.DiscardStage(discarded, fixture.Root), "Repeated cleanup is harmless.");
        Assert.IsFalse(ReleaseUpdateService.DiscardStage(retained, retained.PackageDirectory),
            "A caller cannot choose an arbitrary parent as a staging root.");
        Assert.IsTrue(Directory.Exists(retained.PackageDirectory));
    }

    [TestMethod]
    public async Task FinishedStageCanBeDiscardedWhenCancellationWinsAtTheDeliveryBoundary()
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive);
        using var cancellation = new CancellationTokenSource();
        StagedUpdate stage = await fixture.Service.DownloadAsync(Info(archive), fixture.Root,
            new InlineProgress(value => { if (value == 1) cancellation.Cancel(); }), cancellation.Token);
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsTrue(ReleaseUpdateService.DiscardStage(stage, fixture.Root));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(fixture.Root).Length,
            "A completed download that loses the UI cancellation race must not leak a package.");
    }

    [TestMethod]
    [DataRow("extra-file")]
    [DataRow("extra-directory")]
    [DataRow("changed-file")]
    [DataRow("changed-manifest")]
    [DataRow("outside-package")]
    [DataRow("invalid-stage-name")]
    public async Task DiscardPreservesCustomChangedOrMisplacedStageContent(string mutation)
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive);
        StagedUpdate stage = await fixture.Service.DownloadAsync(Info(archive), fixture.Root);
        string work = Path.GetDirectoryName(stage.PackageDirectory)!;
        switch (mutation)
        {
            case "extra-file": File.WriteAllText(Path.Combine(stage.PackageDirectory, "my-note.txt"), "Keep this"); break;
            case "extra-directory": Directory.CreateDirectory(Path.Combine(stage.PackageDirectory, "my-folder")); break;
            case "changed-file": File.WriteAllText(Path.Combine(stage.PackageDirectory, "KeeStashDock.exe"), "Changed"); break;
            case "changed-manifest":
                File.WriteAllText(Path.Combine(stage.PackageDirectory, UpdatePackageManifest.FileName),
                    JsonSerializer.Serialize(stage.Manifest with { Version = "1.4.0" }, UpdatePackageManifest.JsonOptions));
                break;
            case "outside-package": File.WriteAllText(Path.Combine(work, "transaction.json"), "Retain for recovery"); break;
            case "invalid-stage-name":
                string moved = Path.Combine(fixture.Root, "my-package");
                Directory.Move(work, moved);
                stage = stage with { PackageDirectory = Path.Combine(moved, "package") };
                break;
        }
        Assert.IsFalse(ReleaseUpdateService.DiscardStage(stage, fixture.Root));
        Assert.IsTrue(File.Exists(Path.Combine(stage.PackageDirectory, "KeeStashDock.exe")),
            "Validation must finish before deleting any staged files.");
        Assert.IsTrue(File.Exists(Path.Combine(stage.PackageDirectory, UpdatePackageManifest.FileName)));
    }

    [TestMethod]
    [DataRow("../escape.dll")]
    [DataRow("/absolute.dll")]
    [DataRow("C:/absolute.dll")]
    [DataRow("inside\\escape.dll")]
    [DataRow("file.dll:payload")]
    [DataRow("Data/settings.json")]
    [DataRow("settings.json")]
    [DataRow("Backups/saved.db")]
    [DataRow("folder./app.dll")]
    [DataRow("NUL.dll")]
    [DataRow("COM1.txt")]
    public async Task UnsafeArchivePathsAreRejectedBeforeInstallation(string path)
    {
        byte[] archive = Package(extraPath: path);
        using var fixture = new StagingFixture(archive);
        var error = await Assert.ThrowsAsync<UpdateException>(() => fixture.Service.DownloadAsync(Info(archive), fixture.Root));
        Assert.AreEqual(UpdateFailure.UnsafePackage, error.Failure);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(fixture.Root).Length);
    }

    [TestMethod]
    public async Task DuplicateLinkConflictingAndUndeclaredEntriesAreRejected()
    {
        foreach (byte[] archive in new[]
        {
            Package(extraPath: "keestashdock.EXE"),
            Package(extraPath: "linked.dll", symlink: true),
            Package(extraPath: "Licenses"),
            Package(extraPath: "undeclared.dll"),
            Package(wrongFileHash: true),
            Package(manifestVersion: "1.3.1"),
        })
        {
            using var fixture = new StagingFixture(archive);
            var error = await Assert.ThrowsAsync<UpdateException>(() => fixture.Service.DownloadAsync(Info(archive), fixture.Root));
            Assert.AreEqual(UpdateFailure.UnsafePackage, error.Failure);
        }
    }

    [TestMethod]
    public async Task ExcessiveExpandedEntryIsRejectedWithoutExtractingIt()
    {
        byte[] archive = Package();
        int central = FindSignature(archive, [0x50, 0x4B, 0x01, 0x02]);
        Assert.IsTrue(central > 0);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(central + 24, 4),
            (uint)UpdatePackageManifest.MaximumFileBytes + 1);
        using var fixture = new StagingFixture(archive);
        var error = await Assert.ThrowsAsync<UpdateException>(() => fixture.Service.DownloadAsync(Info(archive), fixture.Root));
        Assert.AreEqual(UpdateFailure.UnsafePackage, error.Failure);
    }

    [TestMethod]
    public async Task RedirectOutsideExpectedGithubAssetHostsIsNeverRequested()
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive, archiveRedirect: "https://attacker.invalid/update.zip");
        var error = await Assert.ThrowsAsync<UpdateException>(() => fixture.Service.DownloadAsync(Info(archive), fixture.Root));
        Assert.AreEqual(UpdateFailure.InvalidMetadata, error.Failure);
        Assert.IsFalse(fixture.Requests.Any(uri => uri.Host == "attacker.invalid"));
    }

    [TestMethod]
    public async Task ExpectedGithubAssetRedirectIsAllowedAndStillHashChecked()
    {
        byte[] archive = Package();
        using var fixture = new StagingFixture(archive,
            archiveRedirect: "https://release-assets.githubusercontent.com/github-production-release-asset/fixture?token=fixture");
        StagedUpdate result = await fixture.Service.DownloadAsync(Info(archive), fixture.Root);
        Assert.IsTrue(File.Exists(Path.Combine(result.PackageDirectory, "KeeStashDock.exe")));
        Assert.IsTrue(fixture.Requests.Any(uri => uri.Host == "release-assets.githubusercontent.com"));
    }

    [TestMethod]
    public async Task StalledResponseBodyHonorsOverallDeadlineAndUserCancellation()
    {
        foreach (bool userCancels in new[] { false, true })
        {
            byte[] archive = Package();
            string root = Path.Combine(Path.GetTempPath(), "KeeStashDock-stalled-update-" + Guid.NewGuid().ToString("N"));
            var stalled = new StallingStream();
            using var http = Client(request => request.RequestUri!.AbsoluteUri.EndsWith(".sha256")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Convert.ToHexStringLower(SHA256.HashData(archive)) + "  " + ArchiveName),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stalled) });
            using var service = new ReleaseUpdateService(http,
                downloadTimeout: userCancels ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(180));
            using var cancellation = new CancellationTokenSource();
            try
            {
                Task<StagedUpdate> download = service.DownloadAsync(Info(archive), root, cancellationToken: cancellation.Token);
                await stalled.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if (userCancels)
                {
                    cancellation.Cancel();
                    await Assert.ThrowsAsync<OperationCanceledException>(() => download);
                }
                else
                {
                    UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => download);
                    Assert.AreEqual(UpdateFailure.Network, error.Failure,
                        "A body timeout is a failed network request, not a user cancellation or a successful empty download.");
                }
                Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
            }
            finally
            {
                cancellation.Cancel();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void PreservedComponentAssetsAreDistinguishedFromShippedSetupScripts()
    {
        Assert.IsTrue(UpdatePathPolicy.IsPreservedAsset("OcrModels/eng.traineddata"));
        Assert.IsTrue(UpdatePathPolicy.IsPreservedAsset("OfflineTranslation/runtime/installed.json"));
        Assert.IsFalse(UpdatePathPolicy.IsPreservedAsset("OcrModels/Setup.ps1"));
        Assert.IsFalse(UpdatePathPolicy.IsPreservedAsset("OfflineTranslation/service.py"));
        Assert.Throws<InvalidDataException>(() => UpdatePathPolicy.ResolveUnderRoot(Path.GetTempPath(), "../outside"));
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> send)
        => new(new Handler(send));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = send(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static string Metadata(bool draft = false, bool prerelease = false, bool includeChecksum = true,
        string archiveUrl = ArchiveUrl)
    {
        var assets = new List<object>
        {
            new { name = ArchiveName, browser_download_url = archiveUrl, size = 200 },
        };
        if (includeChecksum) assets.Add(new { name = ArchiveName + ".sha256", browser_download_url = ArchiveUrl + ".sha256", size = 98 });
        return JsonSerializer.Serialize(new { tag_name = "v" + Version, html_url = ReleaseUrl, draft, prerelease, body = "Fixture notes", assets });
    }

    private static ReleaseUpdateInfo Info(byte[] archive) => new(Version, "Fixture notes", new Uri(ReleaseUrl),
        new Uri(ArchiveUrl), new Uri(ArchiveUrl + ".sha256"), archive.Length);

    private static byte[] Package(string? extraPath = null, bool symlink = false, bool wrongFileHash = false,
        string manifestVersion = Version)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["KeeStashDock.exe"] = Encoding.UTF8.GetBytes("Fixture application"),
            ["Licenses/test.txt"] = Encoding.UTF8.GetBytes("Fixture license"),
        };
        var manifest = new UpdatePackageManifest(1, manifestVersion, files.Select(pair =>
            new UpdatePackageFile(pair.Key, pair.Value.Length,
                wrongFileHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(pair.Value)))).ToArray());
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] contents) in files) Write(zip, path, contents);
            Write(zip, UpdatePackageManifest.FileName, JsonSerializer.SerializeToUtf8Bytes(manifest, UpdatePackageManifest.JsonOptions));
            if (extraPath is not null)
            {
                ZipArchiveEntry extra = Write(zip, extraPath, Encoding.UTF8.GetBytes("Fixture extra file"));
                if (symlink) extra.ExternalAttributes = unchecked((int)0xA1FF0000);
            }
        }
        return bytes.ToArray();
    }

    private static ZipArchiveEntry Write(ZipArchive zip, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path);
        using Stream output = entry.Open();
        output.Write(bytes);
        return entry;
    }

    private static int FindSignature(byte[] value, byte[] signature)
    {
        for (int i = 0; i <= value.Length - signature.Length; i++)
            if (value.AsSpan(i, signature.Length).SequenceEqual(signature)) return i;
        return -1;
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    { public void Report(double value) => report(value); }

    private sealed class StallingStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StagingFixture : IDisposable
    {
        private readonly HttpClient _http;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KeeStashDock-update-tests-" + Guid.NewGuid().ToString("N"));
        public List<Uri> Requests { get; } = [];
        public ReleaseUpdateService Service { get; }
        public StagingFixture(byte[] archive, string? digest = null, string? archiveRedirect = null)
        {
            Directory.CreateDirectory(Root);
            string checksum = (digest ?? Convert.ToHexStringLower(SHA256.HashData(archive))) + "  " + ArchiveName + "\n";
            _http = Client(request =>
            {
                Uri uri = request.RequestUri!;
                Requests.Add(uri);
                if (uri.AbsoluteUri == ArchiveUrl + ".sha256") return new(HttpStatusCode.OK) { Content = new StringContent(checksum) };
                if (uri.AbsoluteUri == ArchiveUrl && archiveRedirect is not null)
                {
                    var redirected = new HttpResponseMessage(HttpStatusCode.Found);
                    redirected.Headers.Location = new Uri(archiveRedirect);
                    return redirected;
                }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            });
            Service = new ReleaseUpdateService(_http);
        }
        public void Dispose()
        {
            Service.Dispose();
            _http.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
