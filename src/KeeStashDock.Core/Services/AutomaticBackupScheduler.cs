using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Core.Services;

/// <summary>Independent minute-based schedule. Missed intervals collapse into
/// one attempt, and failed attempts receive the same full retry interval.</summary>
public sealed class AutomaticBackupScheduler : IDisposable
{
    private readonly BackupService _backup;
    private readonly SettingsStore _settings;
    private readonly DatabaseMaintenanceCoordinator _maintenance;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _scheduleGate = new();
    private ITimer? _timer;
    private DateTimeOffset? _dueUtc;
    private DateTimeOffset? _futureClockBaseline;
    private DateTimeOffset? _deferredUntil;
    private int _intervalMinutes;
    private bool _enabled;
    private bool _started;
    private bool _disposed;
    private string? _backupDirectory;

    public AutomaticBackupScheduler(BackupService backup, SettingsStore settings,
        DatabaseMaintenanceCoordinator maintenance, TimeProvider? clock = null)
    {
        _backup = backup;
        _settings = settings;
        _maintenance = maintenance;
        _clock = clock ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_scheduleGate)
        {
            if (_started || _disposed) return;
            _started = true;
            _settings.Changed += OnSettingsChanged;
            _backup.StatusChanged += OnBackupStatusChanged;
            Recalculate();
            // Short polling also handles wall-clock jumps while ITimer's
            // monotonic clock continues. There is no database work when idle.
            _timer = _clock.CreateTimer(_ => _ = CheckAsync(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }
    }

    public void NotifyResumed()
    {
        if (_disposed || !_started) return;
        Recalculate();
        _ = CheckAsync();
    }

    /// <summary>Deterministic check used by the timer and controllable-clock tests.</summary>
    public async Task CheckAsync()
    {
        if (_disposed || !_started || !await _tickGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            Recalculate();
            DateTimeOffset? due;
            lock (_scheduleGate) due = _dueUtc;
            if (_disposed || due is null || _clock.GetUtcNow() < due) return;
            if (!_maintenance.CanStartOperation)
            {
                _backup.RecordDeferredCheck();
                Defer();
                return;
            }
            BackupAttemptResult result = await _backup.CreateAutomaticSnapshotAsync(
                _settings.Current.BackupKeepCount, _shutdown.Token).ConfigureAwait(false);
            if (result.Outcome is BackupAttemptOutcome.Busy or BackupAttemptOutcome.Deferred) Defer();
            else { lock (_scheduleGate) _deferredUntil = null; Recalculate(); }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally { _tickGate.Release(); }
    }

    private void Defer()
    {
        lock (_scheduleGate) _deferredUntil = _clock.GetUtcNow().AddMinutes(1);
        Recalculate();
    }

    private void OnSettingsChanged()
    {
        if (_intervalMinutes == _settings.Current.AutoBackupIntervalMinutes &&
            _enabled == _settings.Current.AutoBackupEnabled) return;
        lock (_scheduleGate) _deferredUntil = null;
        Recalculate();
        if (_started && !_disposed) _ = CheckAsync();
    }

    private void OnBackupStatusChanged() => Recalculate();

    private void Recalculate()
    {
        lock (_scheduleGate)
        {
            if (_disposed) return;
            string directory = _backup.BackupsDirectory;
            if (!string.Equals(_backupDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                _backupDirectory = directory;
                _dueUtc = null;
                _deferredUntil = null;
                _futureClockBaseline = null;
            }
            _intervalMinutes = _settings.Current.AutoBackupIntervalMinutes;
            _enabled = _settings.Current.AutoBackupEnabled;
            DateTimeOffset now = _clock.GetUtcNow();
            if (!_enabled) _dueUtc = null;
            else
            {
                BackupStatus status = _backup.Status;
                DateTimeOffset? baseline = status.LastAttemptUtc;
                if (status.LastSuccessUtc > baseline || baseline is null) baseline = status.LastSuccessUtc;
                // A backward wall-clock adjustment must not delay the next
                // backup until the old clock catches up, or reset on every tick.
                if (baseline > now)
                {
                    _futureClockBaseline ??= now;
                    baseline = _futureClockBaseline;
                }
                else _futureClockBaseline = null;
                int minutes = _intervalMinutes is >= 15 and <= 43_200 ? _intervalMinutes : 1440;
                _dueUtc = baseline is null
                    ? _dueUtc ?? now.AddSeconds(5)
                    : AddInterval(baseline.Value, minutes);
                if (_deferredUntil > _dueUtc) _dueUtc = _deferredUntil;
            }
            _backup.SetNextCheck(_dueUtc);
        }
    }

    private static DateTimeOffset AddInterval(DateTimeOffset baseline, int minutes)
        => baseline > DateTimeOffset.MaxValue.AddMinutes(-minutes)
            ? DateTimeOffset.MaxValue : baseline.AddMinutes(minutes);

    public void Dispose()
    {
        lock (_scheduleGate)
        {
            if (_disposed) return;
            _disposed = true;
            _settings.Changed -= OnSettingsChanged;
            _backup.StatusChanged -= OnBackupStatusChanged;
            _timer?.Dispose();
            _shutdown.Cancel();
            _backup.SetNextCheck(null);
        }
    }

    public async Task StopAsync()
    {
        Dispose();
        await _tickGate.WaitAsync().ConfigureAwait(false);
        _tickGate.Release();
        await _backup.WaitForIdleAsync().ConfigureAwait(false);
    }
}
