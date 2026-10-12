using KeeStashDock.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class OfflineStartupTests
{
    [TestMethod]
    public async Task WorkingServerTranslatesOnceWithoutCheckingOrStartingTheBundledEngine()
    {
        int requests = 0;
        var expected = new TranslationResult("Offline", "translated text");

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () => { requests++; return Task.FromResult(expected); },
            _ => throw new AssertFailedException("A working server must not be started again."),
            () => throw new AssertFailedException("Installed files do not matter when the server already works."),
            allowStartup: true, CancellationToken.None);

        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task ConnectionFailureStartsAnInstalledEngineOnceThenRetriesTranslation()
    {
        using var cancellation = new CancellationTokenSource();
        var sequence = new List<string>();
        int requests = 0, starts = 0;

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () =>
            {
                sequence.Add("translate");
                return Task.FromResult(++requests == 1 ? Unavailable() : new TranslationResult("Offline", "retry result"));
            },
            token =>
            {
                Assert.AreEqual(cancellation.Token, token);
                sequence.Add("start"); starts++;
                return Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.None));
            },
            () => true, allowStartup: true, cancellation.Token);

        Assert.IsTrue(actual.IsSuccess);
        Assert.AreEqual("retry result", actual.Text);
        Assert.AreEqual(2, requests);
        Assert.AreEqual(1, starts);
        CollectionAssert.AreEqual(new[] { "translate", "start", "translate" }, sequence);
    }

    [TestMethod]
    [DataRow(false, true, null)]
    [DataRow(true, false, null)]
    [DataRow(true, true, 503)]
    public async Task MissingInstallationCustomEndpointOrHttpErrorNeverStartsEngine(
        bool installed, bool allowStartup, int? httpStatus)
    {
        int requests = 0, starts = 0;
        TranslationResult original = Unavailable(httpStatus);

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () => { requests++; return Task.FromResult(original); },
            _ => { starts++; return Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.None)); },
            () => installed, allowStartup, CancellationToken.None);

        Assert.AreSame(original, actual);
        Assert.AreEqual(1, requests);
        Assert.AreEqual(0, starts);
    }

    [TestMethod]
    [DataRow(TranslationFailure.Cancelled)]
    [DataRow(TranslationFailure.Timeout)]
    [DataRow(TranslationFailure.InvalidConfiguration)]
    [DataRow(TranslationFailure.LanguageModelUnavailable)]
    [DataRow(TranslationFailure.InvalidResponse)]
    public async Task ExistingServiceAndRequestFailuresAreReturnedWithoutAttemptingStartup(TranslationFailure failure)
    {
        int requests = 0;
        var original = new TranslationResult("Offline", null, failure);

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () => { requests++; return Task.FromResult(original); },
            _ => throw new AssertFailedException("This failure cannot be fixed by starting a bundled engine."),
            () => true, allowStartup: true, CancellationToken.None);

        Assert.AreSame(original, actual);
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    [DataRow(ComponentInstallFailure.Cancelled, TranslationFailure.Cancelled)]
    [DataRow(ComponentInstallFailure.Failed, TranslationFailure.LocalServiceUnavailable)]
    [DataRow(ComponentInstallFailure.Timeout, TranslationFailure.LocalServiceUnavailable)]
    [DataRow(ComponentInstallFailure.MissingFiles, TranslationFailure.LocalServiceUnavailable)]
    [DataRow(ComponentInstallFailure.AccessDenied, TranslationFailure.LocalServiceUnavailable)]
    [DataRow(ComponentInstallFailure.RestartRequired, TranslationFailure.LocalServiceUnavailable)]
    public async Task StartupFailuresMapToTranslationFailuresWithoutRetrying(
        ComponentInstallFailure startupFailure, TranslationFailure expected)
    {
        int requests = 0, starts = 0;

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () => { requests++; return Task.FromResult(Unavailable()); },
            _ => { starts++; return Task.FromResult(new ComponentInstallResult(startupFailure, "internal fixture detail")); },
            () => true, allowStartup: true, CancellationToken.None);

        Assert.AreEqual(expected, actual.Failure);
        Assert.AreEqual("Offline", actual.Provider);
        Assert.IsNull(actual.Text);
        Assert.AreEqual(1, requests);
        Assert.AreEqual(1, starts);
    }

    [TestMethod]
    public async Task CancellationDuringTheInitialRequestPreventsStartingTheEngine()
    {
        using var cancellation = new CancellationTokenSource();
        int starts = 0;

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () => { cancellation.Cancel(); return Task.FromResult(Unavailable()); },
            _ => { starts++; return Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.None)); },
            () => true, allowStartup: true, cancellation.Token);

        Assert.AreEqual(TranslationFailure.Cancelled, actual.Failure);
        Assert.AreEqual(0, starts);
    }

    [TestMethod]
    public async Task FailedRetryIsReturnedWithoutAnEndlessRestartLoop()
    {
        int requests = 0, starts = 0;
        TranslationResult original = Unavailable();

        TranslationResult actual = await TranslationService.TranslateWithOfflineStartupAsync(
            () => { requests++; return Task.FromResult(original); },
            _ => { starts++; return Task.FromResult(new ComponentInstallResult(ComponentInstallFailure.None)); },
            () => true, allowStartup: true, CancellationToken.None);

        Assert.AreSame(original, actual);
        Assert.AreEqual(2, requests);
        Assert.AreEqual(1, starts);
    }

    private static TranslationResult Unavailable(int? httpStatus = null)
        => new("Offline", null, TranslationFailure.LocalServiceUnavailable, httpStatus);
}
