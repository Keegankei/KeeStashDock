using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Background janitor. Runs once at startup and then hourly on a timer:
///   1. moves expired snippets to the recycle bin,
///   2. purges recycle-bin rows past the retention window (default 30 days).
/// Automatic backups have their own minute-based scheduler.
/// All work happens on a ThreadPool timer thread; repositories open their own
/// connections so no UI-thread state is touched.
/// </summary>
public sealed class HousekeepingService : IDisposable
{
    private readonly SnippetRepository _snippets;
    private readonly SettingsStore _settings;
    private readonly FileLogger _log;
    private readonly DatabaseMaintenanceCoordinator _maintenance;
    private Timer? _timer;

    /// <summary>Raised (on a worker thread) when housekeeping changed data.</summary>
    public event Action? Swept;

    public HousekeepingService(SnippetRepository snippets, SettingsStore settings, FileLogger log,
                               DatabaseMaintenanceCoordinator maintenance)
    {
        _snippets = snippets;
        _settings = settings;
        _log = log;
        _maintenance = maintenance;
    }

    public void Start()
    {
        _timer = new Timer(_ => Sweep(), null,
            dueTime: TimeSpan.FromSeconds(5),      // shortly after startup
            period: TimeSpan.FromHours(1));
    }

    private void Sweep()
    {
        if (!_maintenance.CanStartOperation)
        {
            return;
        }

        try
        {
            AppSettings cfg = _settings.Current;
            int expired = _snippets.MoveExpiredToTrash();
            int purged = _snippets.PurgeTrash(cfg.TrashRetentionDays);

            if (expired > 0 || purged > 0)
            {
                _log.Info($"Housekeeping: {expired} expired, {purged} purged");
                Swept?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _log.Error("Housekeeping sweep failed", ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
