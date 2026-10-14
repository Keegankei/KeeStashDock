using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class SelectionSessionTests
{
    [TestMethod]
    public async Task CopyThenTranslateReadsOriginalSelectionOnlyOnce()
    {
        int sourceReads = 0;
        using var session = new SelectionSession();
        session.Begin(_ => Task.FromResult<string?>(++sourceReads == 1 ? "original selection" : "changed clipboard"));
        string? copied = await session.ReadAsync();
        Assert.IsTrue(session.TryMarkCaptured(session.Revision));
        string? translated = await session.ReadAsync();
        Assert.AreEqual("original selection", copied);
        Assert.AreEqual(copied, translated);
        Assert.AreEqual(1, sourceReads, "Translation must reuse the same in-memory source after Copy.");
        Assert.IsFalse(session.TryMarkCaptured(session.Revision), "Repeated clicks must not add another entry.");
    }

    [TestMethod]
    public async Task OpeningBubbleDoesNotReadClipboardOrMarkCapture()
    {
        int sourceReads = 0;
        using var session = new SelectionSession();
        session.Begin(_ => { sourceReads++; return Task.FromResult<string?>("translation only"); });
        Assert.AreEqual(0, sourceReads, "Showing the bubble is not consent to read or translate text.");
        Assert.AreEqual("translation only", await session.ReadAsync());
        Assert.IsFalse(session.HasCaptured, "Reading text for translation is not a capture action.");
    }

    [TestMethod]
    public async Task ConcurrentActionsShareOnePendingRead()
    {
        int sourceReads = 0;
        var completion = new TaskCompletionSource<string?>();
        using var session = new SelectionSession();
        session.Begin(_ => { sourceReads++; return completion.Task; });
        Task<string?> first = session.ReadAsync();
        Task<string?> second = session.ReadAsync();
        completion.SetResult("one source");
        Assert.AreEqual("one source", await first);
        Assert.AreEqual("one source", await second);
        Assert.AreEqual(1, sourceReads);
    }

    [TestMethod]
    public async Task NewSelectionCancelsLateReadAndRejectsOldCapture()
    {
        var oldSource = new TaskCompletionSource<string?>();
        using var session = new SelectionSession();
        session.Begin(_ => oldSource.Task); // Simulate a source that ignores cancellation.
        long oldRevision = session.Revision;
        Task<string?> oldRead = session.ReadAsync();
        session.Begin(_ => Task.FromResult<string?>("new selection"));
        oldSource.SetResult("old private selection");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => oldRead);
        Assert.IsFalse(session.TryMarkCaptured(oldRevision));
        Assert.AreEqual("new selection", await session.ReadAsync());
    }

    [TestMethod]
    public async Task DismissalDropsSourceAndAllowsFreshSameTextCapture()
    {
        using var session = new SelectionSession();
        session.Begin(_ => Task.FromResult<string?>("selected text"));
        await session.ReadAsync();
        Assert.IsTrue(session.TryMarkCaptured(session.Revision));
        session.Clear();
        Assert.IsFalse(session.IsOpen);
        Assert.IsNull(await session.ReadAsync());
        Assert.IsFalse(session.TryMarkCaptured(session.Revision));
        session.Begin(_ => Task.FromResult<string?>("selected text"));
        Assert.IsFalse(session.HasCaptured);
        Assert.IsTrue(session.TryMarkCaptured(session.Revision), "A new user selection is an independent action.");
    }

    [TestMethod]
    public async Task CancelledConsumerCannotUseCachedText()
    {
        using var session = new SelectionSession();
        session.Begin(_ => Task.FromResult<string?>("cached source"));
        await session.ReadAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await session.ReadAsync(cancellation.Token));
    }
}
