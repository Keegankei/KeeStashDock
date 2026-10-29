using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Core.Services;

public sealed partial class SnippetService
{
    public BatchSelection? CaptureUse(Snippet expected)
    {
        try { return _repo.CaptureUse(expected); }
        catch (Exception error)
        { _log.Warn($"Could not prepare item output: {error.GetType().Name}"); return null; }
    }

    /// <summary>Best-effort accounting after output. Never retry the clipboard or surface a late database failure.</summary>
    public bool TryRegisterUse(Snippet item, BatchSelection snapshot)
    {
        if (snapshot.Items.Count != 1 || snapshot.Items[0].Id != item.Id) return false;
        try
        {
            RegisterUseResult? result = _repo.TryRegisterUse(snapshot);
            if (result is null)
            { _log.Warn("Skipped output accounting because its library or item changed."); return false; }
            item.UseCount = result.UseCount;
            item.LastUsedAt = result.LastUsedAt;
            item.DeletedAt = result.DeletedAt;
            if (result.DeletedAt is not null) Changed?.Invoke();
            return true;
        }
        catch (Exception error)
        { _log.Warn($"Output succeeded but usage accounting failed: {error.GetType().Name}"); return false; }
    }
}
