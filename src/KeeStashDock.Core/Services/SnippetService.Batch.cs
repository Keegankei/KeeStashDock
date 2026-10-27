using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Core.Services;

public sealed partial class SnippetService
{
    public string DatabaseIdentity => _repo.DatabaseIdentity;

    public BatchSelection CaptureBatchSelection(IReadOnlyCollection<long> ids, string? expectedDatabaseIdentity = null)
        => _repo.CaptureBatchSelection(ids, expectedDatabaseIdentity);

    public BatchResult ApplyBatch(BatchSelection selection, BatchAction action, long? targetCategoryId = null,
        IReadOnlyList<string>? tags = null, bool resetExpiredOnRestore = false)
    {
        BatchResult result = _repo.ApplyBatch(selection, action, targetCategoryId, tags, resetExpiredOnRestore);
        Changed?.Invoke();
        return result;
    }

    public BatchResult UndoTrash(BatchUndoToken undo)
    {
        BatchResult result = _repo.UndoTrash(undo);
        Changed?.Invoke();
        return result;
    }
}
