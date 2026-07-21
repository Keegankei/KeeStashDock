using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class WorkbenchEditorSessionTests
{
    [TestMethod]
    public void EditorFields_CompareAgainstNormalizedOriginalSnapshot()
    {
        WorkbenchEditorSession session = CreateLoadedSession();

        Assert.IsFalse(session.IsDirty);
        session.EditTitle = " changed ";
        Assert.IsTrue(session.IsDirty);
        session.EditTitle = "Original";
        session.EditTags = "beta，alpha, alpha";
        Assert.IsFalse(session.IsDirty);
        session.EditContent = "changed content";
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public void ExternalRefreshWhileDirty_PreservesEveryEditorBuffer()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditTitle = "draft title";
        session.EditContent = "draft content";
        session.EditTags = "draft-tag";
        session.EditExpirePolicy = ExpirePolicy.SevenDays;

        session.MergeExternal(CreateSnapshot());

        Assert.AreEqual("draft title", session.EditTitle);
        Assert.AreEqual("draft content", session.EditContent);
        Assert.AreEqual("draft-tag", session.EditTags);
        Assert.AreEqual(ExpirePolicy.SevenDays, session.EditExpirePolicy);
        Assert.IsTrue(session.IsDirty);
        Assert.AreEqual(WorkbenchExternalConflict.None, session.ExternalConflict);
    }

    [TestMethod]
    public async Task SaveDecision_WaitsForSaveThenCommitsNewSelection()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "saved content";
        WorkbenchEditSnapshot? persisted = null;
        WorkbenchEditSnapshot next = CreateSnapshot() with
        {
            ItemId = 20,
            Title = "Next",
        };

        WorkbenchLeaveResult result = await session.RequestSelectionChangeAsync(
            next,
            _ => Task.FromResult(WorkbenchUnsavedChoice.Save),
            (snapshot, _) =>
            {
                persisted = snapshot;
                return Task.CompletedTask;
            });

        Assert.AreEqual(WorkbenchLeaveResult.Proceed, result);
        Assert.AreEqual("saved content", persisted?.Content);
        Assert.AreEqual(20L, session.ItemId);
        Assert.AreEqual("Next", session.EditTitle);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public async Task DiscardDecision_CommitsNewSelectionWithoutSaving()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditTitle = "discard me";
        WorkbenchEditSnapshot next = CreateSnapshot() with
        {
            ItemId = 20,
            Title = "Next",
        };
        int saves = 0;

        WorkbenchLeaveResult result = await session.RequestSelectionChangeAsync(
            next,
            _ => Task.FromResult(WorkbenchUnsavedChoice.Discard),
            (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            });

        Assert.AreEqual(WorkbenchLeaveResult.Proceed, result);
        Assert.AreEqual(20L, session.ItemId);
        Assert.AreEqual("Next", session.EditTitle);
        Assert.AreEqual(0, saves);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public async Task CancelDecision_PreservesSelectionIdentityAndBuffer()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "keep this draft";

        WorkbenchEditSnapshot next = CreateSnapshot() with { ItemId = 20 };
        WorkbenchLeaveResult result = await session.RequestSelectionChangeAsync(
            next,
            _ => Task.FromResult(WorkbenchUnsavedChoice.Cancel),
            (_, _) => Task.CompletedTask);

        Assert.AreEqual(WorkbenchLeaveResult.Cancelled, result);
        Assert.AreEqual(10L, session.ItemId);
        Assert.AreEqual("keep this draft", session.EditContent);
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public async Task SaveFailure_KeepsDirtyBufferAndOriginalSnapshot()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "unsaved after busy database";

        WorkbenchLeaveResult result = await session.ResolveUnsavedChangesAsync(
            _ => Task.FromResult(WorkbenchUnsavedChoice.Save),
            (_, _) => throw new InvalidOperationException("database busy"));

        Assert.AreEqual(WorkbenchLeaveResult.SaveFailed, result);
        Assert.AreEqual("unsaved after busy database", session.EditContent);
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public async Task CleanLeave_DoesNotPromptOrSave()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        int prompts = 0;
        int saves = 0;

        WorkbenchLeaveResult result = await session.ResolveUnsavedChangesAsync(
            _ =>
            {
                prompts++;
                return Task.FromResult(WorkbenchUnsavedChoice.Cancel);
            },
            (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            });

        Assert.AreEqual(WorkbenchLeaveResult.Proceed, result);
        Assert.AreEqual(0, prompts);
        Assert.AreEqual(0, saves);
    }

    [TestMethod]
    public void CurrentItemDeletedExternally_ReportsConflictWithoutLosingDraft()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "draft survives deletion";

        session.MarkExternalDeleted();

        Assert.AreEqual(WorkbenchExternalConflict.Deleted, session.ExternalConflict);
        Assert.AreEqual("draft survives deletion", session.EditContent);
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public void CurrentItemChangedExternally_ReportsConflictWithoutReplacingDraft()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "local draft";
        WorkbenchEditSnapshot external = CreateSnapshot() with
        {
            Content = "external content",
        };

        session.MergeExternal(external);

        Assert.AreEqual(WorkbenchExternalConflict.Changed, session.ExternalConflict);
        Assert.AreEqual("local draft", session.EditContent);
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public void CleanExternalRefresh_LoadsAuthoritativeSnapshotWithoutConflict()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        WorkbenchEditSnapshot external = CreateSnapshot() with
        {
            Title = "Externally updated",
            Content = "new authoritative content",
        };

        session.MergeExternal(external);

        Assert.AreEqual("Externally updated", session.EditTitle);
        Assert.AreEqual("new authoritative content", session.EditContent);
        Assert.IsFalse(session.IsDirty);
        Assert.AreEqual(WorkbenchExternalConflict.None, session.ExternalConflict);
    }

    [TestMethod]
    public async Task DiscardAfterExternalConflict_LoadsLatestExternalSnapshot()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "local draft";
        WorkbenchEditSnapshot external = CreateSnapshot() with
        {
            Content = "external authoritative content",
        };
        session.MergeExternal(external);

        WorkbenchLeaveResult result = await session.ResolveUnsavedChangesAsync(
            _ => Task.FromResult(WorkbenchUnsavedChoice.Discard),
            (_, _) => Task.CompletedTask);

        Assert.AreEqual(WorkbenchLeaveResult.Proceed, result);
        Assert.AreEqual("external authoritative content", session.EditContent);
        Assert.AreEqual(WorkbenchExternalConflict.None, session.ExternalConflict);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public async Task CancelledSave_PreservesDirtyState()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "cancelled save draft";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        WorkbenchLeaveResult result = await session.SaveAsync(
            (_, token) => Task.FromCanceled(token),
            cancellation.Token);

        Assert.AreEqual(WorkbenchLeaveResult.Cancelled, result);
        Assert.AreEqual("cancelled save draft", session.EditContent);
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public async Task RequestingCurrentSelection_DoesNotPromptOrSave()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "dirty but same item";
        int prompts = 0;
        int saves = 0;

        WorkbenchLeaveResult result = await session.RequestSelectionChangeAsync(
            CreateSnapshot(),
            _ =>
            {
                prompts++;
                return Task.FromResult(WorkbenchUnsavedChoice.Save);
            },
            (_, _) =>
            {
                saves++;
                return Task.CompletedTask;
            });

        Assert.AreEqual(WorkbenchLeaveResult.Proceed, result);
        Assert.AreEqual(0, prompts);
        Assert.AreEqual(0, saves);
        Assert.AreEqual("dirty but same item", session.EditContent);
    }

    [TestMethod]
    public void Clear_RemovesSelectionBufferAndConflict()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditContent = "draft";
        session.MarkExternalDeleted();

        session.Clear();

        Assert.IsNull(session.ItemId);
        Assert.AreEqual(string.Empty, session.EditTitle);
        Assert.AreEqual(string.Empty, session.EditContent);
        Assert.AreEqual(string.Empty, session.EditTags);
        Assert.AreEqual(WorkbenchExternalConflict.None, session.ExternalConflict);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public async Task RepeatedCancelForHideAndClose_NeverChangesDraft()
    {
        WorkbenchEditorSession session = CreateLoadedSession();
        session.EditTitle = "window lifetime draft";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            WorkbenchLeaveResult result = await session.ResolveUnsavedChangesAsync(
                _ => Task.FromResult(WorkbenchUnsavedChoice.Cancel),
                (_, _) => Task.CompletedTask);
            Assert.AreEqual(WorkbenchLeaveResult.Cancelled, result);
        }

        Assert.AreEqual("window lifetime draft", session.EditTitle);
        Assert.IsTrue(session.IsDirty);
    }

    private static WorkbenchEditorSession CreateLoadedSession()
    {
        var session = new WorkbenchEditorSession();
        session.Load(CreateSnapshot());
        return session;
    }

    private static WorkbenchEditSnapshot CreateSnapshot() => new(
        10,
        "Original",
        "original content",
        ExpirePolicy.Never,
        ["alpha", "beta"]);
}
