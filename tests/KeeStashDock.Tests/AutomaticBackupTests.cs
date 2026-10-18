using KeeStashDock.Core.Services;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AutomaticBackupTests
{
    private string _root = null!;
    private FileLogger _log = null!;
    private Db _db = null!;
    private DatabaseMaintenanceCoordinator _maintenance = null!;
    private ControlledClock _clock = null!;
    private BackupService _backup = null!;
    private SettingsStore _settings = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "AutomaticBackup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new FileLogger(_root);
        _maintenance = new DatabaseMaintenanceCoordinator();
        _db = new Db(Path.Combine(_root, "library.db"), _maintenance);
        new MigrationRunner(_db, _log).Run();
        _clock = new ControlledClock(new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));
        _backup = new BackupService(_db, _log, _clock, _maintenance);
        _settings = new SettingsStore(Path.Combine(_root, "settings.json"));
        _settings.Load();
        _settings.Current.AutoBackupIntervalMinutes = 15;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _log.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    [TestMethod]
    [DataRow(15)]
    [DataRow(30)]
    [DataRow(60)]
    public async Task Scheduler_UsesMinuteIntervalsWithoutHourlyHousekeeping(int minutes)
    {
        _settings.Current.AutoBackupIntervalMinutes = minutes;
        using var scheduler = CreateScheduler();
        scheduler.Start();
        Assert.AreEqual(TimeSpan.FromSeconds(30), _clock.TimerPeriod);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await scheduler.CheckAsync();
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
        DateTimeOffset first = _backup.Status.LastSuccessUtc!.Value;
        _clock.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1));
        await scheduler.CheckAsync();
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await scheduler.CheckAsync();
        Assert.AreEqual(2, _backup.ListSnapshots().Count);
        Assert.AreEqual(first.AddMinutes(minutes), _backup.Status.LastSuccessUtc);
        Assert.AreEqual(_clock.GetUtcNow().AddMinutes(minutes), _backup.Status.NextCheckUtc);
    }

    [TestMethod]
    public async Task Resume_CollapsesMissedIntervalsAndRepeatedChecksIntoOneBackup()
    {
        await _backup.BackUpNowAsync();
        using var scheduler = CreateScheduler();
        scheduler.Start();
        _clock.Advance(TimeSpan.FromDays(2));
        await scheduler.CheckAsync();
        await scheduler.CheckAsync();
        await scheduler.CheckAsync();
        Assert.AreEqual(2, _backup.ListSnapshots().Count);
        Assert.AreEqual(_clock.GetUtcNow().AddMinutes(15), _backup.Status.NextCheckUtc);
    }

    [TestMethod]
    public async Task ClockTimerActuallyTriggersTheFifteenMinuteBackup()
    {
        await _backup.BackUpNowAsync();
        using var scheduler = CreateScheduler();
        scheduler.Start();
        _clock.Advance(TimeSpan.FromMinutes(14));
        _clock.FireTimer();
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _backup.StatusChanged += () =>
        {
            if (_backup.Status.State == BackupRunState.Succeeded && _backup.Status.LastSuccessUtc == _clock.GetUtcNow())
                completed.TrySetResult();
        };
        _clock.Advance(TimeSpan.FromMinutes(1));
        _clock.FireTimer();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _backup.WaitForIdleAsync();
        Assert.AreEqual(2, _backup.ListSnapshots().Count);
        await scheduler.StopAsync();
    }

    [TestMethod]
    public async Task ClockMovingBackward_DoesNotWaitForOldWallClockOrResetDeadlineEachCheck()
    {
        await _backup.BackUpNowAsync();
        using var scheduler = CreateScheduler();
        scheduler.Start();
        _clock.Advance(TimeSpan.FromHours(-2));
        await scheduler.CheckAsync();
        DateTimeOffset expected = _clock.GetUtcNow().AddMinutes(15);
        Assert.AreEqual(expected, _backup.Status.NextCheckUtc);
        _clock.Advance(TimeSpan.FromMinutes(14));
        await scheduler.CheckAsync();
        Assert.AreEqual(expected, _backup.Status.NextCheckUtc);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.CheckAsync();
        Assert.AreEqual(2, _backup.ListSnapshots().Count);
    }

    [TestMethod]
    public async Task SettingsChanges_RecalculateAndDisablingStopsAutomaticWork()
    {
        await _backup.BackUpNowAsync();
        using var scheduler = CreateScheduler();
        scheduler.Start();
        _settings.Current.AutoBackupIntervalMinutes = 30;
        _settings.Save();
        Assert.AreEqual(_clock.GetUtcNow().AddMinutes(30), _backup.Status.NextCheckUtc);
        _settings.Current.AutoBackupEnabled = false;
        _settings.Save();
        Assert.IsNull(_backup.Status.NextCheckUtc);
        _clock.Advance(TimeSpan.FromHours(3));
        await scheduler.CheckAsync();
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
    }

    [TestMethod]
    public async Task ShutdownStopsChecksAndClearsNextCheck()
    {
        using var scheduler = CreateScheduler();
        scheduler.Start();
        await scheduler.StopAsync();
        _clock.Advance(TimeSpan.FromDays(3));
        await scheduler.CheckAsync();
        Assert.AreEqual(0, _backup.ListSnapshots().Count);
        Assert.IsNull(_backup.Status.NextCheckUtc);
        Assert.IsTrue(_clock.TimerDisposed);
    }

    [TestMethod]
    public async Task StatusPersistsUtcSuccessAndAttemptAcrossRestart()
    {
        await _backup.BackUpNowAsync();
        var reloaded = new BackupService(_db, _log, _clock, _maintenance);
        Assert.AreEqual(_clock.GetUtcNow(), reloaded.Status.LastSuccessUtc);
        Assert.AreEqual(_clock.GetUtcNow(), reloaded.Status.LastAttemptUtc);
        Assert.AreEqual(TimeSpan.Zero, reloaded.Status.LastSuccessUtc!.Value.Offset);
        using var scheduler = new AutomaticBackupScheduler(reloaded, _settings, _maintenance, _clock);
        scheduler.Start();
        await scheduler.CheckAsync();
        Assert.AreEqual(1, reloaded.ListSnapshots().Count);
    }

    [TestMethod]
    public async Task Maintenance_DefersWithoutCreatingSnapshotOrStartingAttempt()
    {
        using var scheduler = CreateScheduler();
        scheduler.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        using (var lease = _maintenance.EnterMaintenance())
        {
            await scheduler.CheckAsync();
            Assert.AreEqual(BackupRunState.Deferred, _backup.Status.State);
            Assert.IsNull(_backup.Status.LastAttemptUtc);
            Assert.AreEqual(0, _backup.ListSnapshots().Count);
            Assert.AreEqual(BackupAttemptOutcome.Deferred, (await _backup.BackUpNowAsync()).Outcome);
        }
        _clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.CheckAsync();
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
    }

    [TestMethod]
    public async Task SimultaneousManualAndAutomaticRequests_DoNotQueueDuplicateSnapshots()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _backup.StatusChanged += () => { if (_backup.Status.State == BackupRunState.Running) started.TrySetResult(); };
        Task<BackupAttemptResult> automatic;
        using (SqliteConnection held = _db.Open())
        {
            automatic = _backup.CreateAutomaticSnapshotAsync(7);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(BackupAttemptOutcome.Busy, (await _backup.BackUpNowAsync()).Outcome);
        }
        Assert.AreEqual(BackupAttemptOutcome.Succeeded, (await automatic.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
    }

    [TestMethod]
    public async Task FailurePreservesPreviousSuccessAndDoesNotRetryEveryTick()
    {
        await _backup.BackUpNowAsync();
        DateTimeOffset success = _backup.Status.LastSuccessUtc!.Value;
        using var scheduler = CreateScheduler();
        scheduler.Start();
        CorruptSource();
        _clock.Advance(TimeSpan.FromMinutes(15));
        await scheduler.CheckAsync();
        Assert.AreEqual(BackupRunState.Failed, _backup.Status.State);
        DateTimeOffset attempt = _backup.Status.LastAttemptUtc!.Value;
        Assert.AreEqual(success, _backup.Status.LastSuccessUtc);
        Assert.IsNotNull(_backup.Status.ErrorCode);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.CheckAsync();
        Assert.AreEqual(attempt, _backup.Status.LastAttemptUtc);
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
        Assert.AreEqual(attempt.AddMinutes(15), _backup.Status.NextCheckUtc);
    }

    [TestMethod]
    public async Task RetentionRemovesOnlyOwnedAutomaticSnapshotsAfterNewSuccess()
    {
        Directory.CreateDirectory(_backup.BackupsDirectory);
        string manualExport = Path.Combine(_backup.BackupsDirectory, "keestashdock_personal.db");
        string recovery = Path.Combine(_backup.BackupsDirectory, "pre_import_safety.db");
        File.WriteAllText(manualExport, "keep me");
        File.WriteAllText(recovery, "keep recovery");
        await _backup.BackUpNowAsync();
        for (int index = 0; index < 4; index++)
        {
            _clock.Advance(TimeSpan.FromMinutes(15));
            Assert.AreEqual(BackupAttemptOutcome.Succeeded, (await _backup.CreateAutomaticSnapshotAsync(2)).Outcome);
        }
        Assert.AreEqual(2, _backup.ListSnapshots().Count(snapshot => snapshot.IsAutomatic));
        Assert.IsTrue(_backup.ListSnapshots().Any(snapshot => Path.GetFileName(snapshot.Path).Contains("manual_")));
        Assert.AreEqual("keep me", File.ReadAllText(manualExport));
        Assert.AreEqual("keep recovery", File.ReadAllText(recovery));
        CorruptSource();
        Assert.AreEqual(BackupAttemptOutcome.Failed, (await _backup.CreateAutomaticSnapshotAsync(1)).Outcome);
        Assert.AreEqual(2, _backup.ListSnapshots().Count(snapshot => snapshot.IsAutomatic));
        Assert.AreEqual(0, Directory.GetFiles(_backup.BackupsDirectory, ".snapshot-*").Length);
    }

    [TestMethod]
    public void FailedExportPreservesExistingDestinationAndRemovesPartialSnapshot()
    {
        string target = Path.Combine(_root, "export.ksdpkg");
        File.WriteAllText(target, "original export");
        CorruptSource();
        Assert.ThrowsExactly<SqliteException>(() => _backup.ExportPackage(target));
        Assert.AreEqual("original export", File.ReadAllText(target));
        Assert.AreEqual(0, Directory.GetFiles(_root, ".snapshot-*").Length);
    }

    [TestMethod]
    public async Task WalOnlyCommittedChangesAndImagesAppearInSelfContainedSnapshot()
    {
        // A separate raw connection intentionally keeps WAL open while the
        // coordinated connection factory snapshots committed text and blobs.
        using var writer = new SqliteConnection($"Data Source={_db.DatabaseFile};Pooling=False");
        writer.Open();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA wal_autocheckpoint=0; CREATE TABLE BackupProbe (TextValue TEXT, ImageValue BLOB); INSERT INTO BackupProbe VALUES ('old', X'010203');";
        command.ExecuteNonQuery();
        await _backup.BackUpNowAsync();
        command.CommandText = "UPDATE BackupProbe SET TextValue='changed', ImageValue=X'04050607';";
        command.ExecuteNonQuery();
        BackupAttemptResult result = await _backup.BackUpNowAsync();
        Assert.AreEqual(BackupAttemptOutcome.Succeeded, result.Outcome);
        using var snapshot = new SqliteConnection($"Data Source={result.SnapshotPath};Mode=ReadOnly;Pooling=False");
        snapshot.Open();
        using var read = snapshot.CreateCommand();
        read.CommandText = "SELECT TextValue || ':' || hex(ImageValue) FROM BackupProbe;";
        Assert.AreEqual("changed:04050607", read.ExecuteScalar());
        Assert.IsFalse(File.Exists(result.SnapshotPath + "-wal"));
        Assert.IsFalse(File.Exists(result.SnapshotPath + "-shm"));
    }

    [TestMethod]
    public async Task IdenticalClockTimestampsStillProduceUniqueSnapshots()
    {
        BackupAttemptResult first = await _backup.BackUpNowAsync();
        BackupAttemptResult second = await _backup.BackUpNowAsync();
        Assert.AreNotEqual(first.SnapshotPath, second.SnapshotPath);
        Assert.AreEqual(2, _backup.ListSnapshots().Count);
    }

    [TestMethod]
    public async Task ConcurrentScheduleStatusUpdatesNeverEraseBackupProgressOrSuccess()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _backup.StatusChanged += () => { if (_backup.Status.State == BackupRunState.Running) started.TrySetResult(); };
        Task<BackupAttemptResult> backup;
        using (SqliteConnection held = _db.Open())
        {
            backup = _backup.BackUpNowAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
            {
                for (int iteration = 0; iteration < 100; iteration++)
                    _backup.SetNextCheck(_clock.GetUtcNow().AddMinutes(worker + iteration + 15));
            })));
            Assert.AreEqual(BackupRunState.Running, _backup.Status.State);
            Assert.AreEqual(_clock.GetUtcNow(), _backup.Status.LastAttemptUtc);
        }
        Assert.AreEqual(BackupAttemptOutcome.Succeeded, (await backup).Outcome);
        Assert.AreEqual(_clock.GetUtcNow(), _backup.Status.LastSuccessUtc);
        Assert.AreEqual(BackupRunState.Succeeded, _backup.Status.State);
    }

    [TestMethod]
    public void CorruptStatusValuesAndFutureTimestampAreNormalizedWithoutCrashingSchedule()
    {
        Directory.CreateDirectory(_backup.BackupsDirectory);
        File.WriteAllText(Path.Combine(_backup.BackupsDirectory, "backup-status.json"),
            System.Text.Json.JsonSerializer.Serialize(new BackupStatus
            {
                State = (BackupRunState)999, ErrorCode = "UnrecognizedLocaleKey",
                LastAttemptUtc = DateTimeOffset.MaxValue,
            }));
        var reloaded = new BackupService(_db, _log, _clock, _maintenance);
        Assert.AreEqual(BackupRunState.Never, reloaded.Status.State);
        Assert.IsNull(reloaded.Status.ErrorCode);
        using var scheduler = new AutomaticBackupScheduler(reloaded, _settings, _maintenance, _clock);
        scheduler.Start();
        Assert.AreEqual(_clock.GetUtcNow().AddMinutes(15), reloaded.Status.NextCheckUtc);
    }

    [TestMethod]
    public async Task CancelledWritePreservesLastSuccessfulSnapshotAndCleansStaging()
    {
        await _backup.BackUpNowAsync();
        DateTimeOffset lastSuccess = _backup.Status.LastSuccessUtc!.Value;
        _clock.Advance(TimeSpan.FromMinutes(15));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _backup.StatusChanged += () => { if (_backup.Status.State == BackupRunState.Running) started.TrySetResult(); };
        Task<BackupAttemptResult> pending;
        using (SqliteConnection held = _db.Open())
        {
            pending = _backup.CreateAutomaticSnapshotAsync(1, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        Assert.AreEqual(BackupAttemptOutcome.Cancelled, (await pending).Outcome);
        Assert.AreEqual(lastSuccess, _backup.Status.LastSuccessUtc);
        Assert.AreEqual(1, _backup.ListSnapshots().Count);
        Assert.AreEqual(0, Directory.GetFiles(_backup.BackupsDirectory, ".snapshot-*").Length);
    }

    [TestMethod]
    public async Task InaccessibleBackupLocationReportsFailureWithoutRepeatedRetries()
    {
        File.WriteAllText(_backup.BackupsDirectory, "A file blocks the backup directory");
        using var scheduler = CreateScheduler();
        scheduler.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        await scheduler.CheckAsync();
        Assert.AreEqual(BackupRunState.Failed, _backup.Status.State);
        Assert.IsNotNull(_backup.Status.ErrorCode);
        Assert.IsNull(_backup.Status.LastSuccessUtc);
        DateTimeOffset? attempted = _backup.Status.LastAttemptUtc;
        _clock.Advance(TimeSpan.FromSeconds(30));
        await scheduler.CheckAsync();
        Assert.AreEqual(attempted, _backup.Status.LastAttemptUtc);
        Assert.AreEqual("A file blocks the backup directory", File.ReadAllText(_backup.BackupsDirectory));
    }

    [TestMethod]
    public void InterruptedProcessStatusIsVisibleAndRetainsPreviousSuccess()
    {
        Directory.CreateDirectory(_backup.BackupsDirectory);
        DateTimeOffset success = _clock.GetUtcNow().AddMinutes(-15);
        File.WriteAllText(Path.Combine(_backup.BackupsDirectory, "backup-status.json"),
            System.Text.Json.JsonSerializer.Serialize(new BackupStatus
            {
                State = BackupRunState.Running, LastAttemptUtc = _clock.GetUtcNow(), LastSuccessUtc = success,
            }));
        var reloaded = new BackupService(_db, _log, _clock, _maintenance);
        Assert.AreEqual(BackupRunState.Failed, reloaded.Status.State);
        Assert.AreEqual("Interrupted", reloaded.Status.ErrorCode);
        Assert.AreEqual(success, reloaded.Status.LastSuccessUtc);
        using var scheduler = new AutomaticBackupScheduler(reloaded, _settings, _maintenance, _clock);
        scheduler.Start();
        Assert.AreEqual(_clock.GetUtcNow().AddMinutes(15), reloaded.Status.NextCheckUtc);
    }

    private AutomaticBackupScheduler CreateScheduler() => new(_backup, _settings, _maintenance, _clock);

    private void CorruptSource()
    {
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "-wal", "-shm" }) File.Delete(_db.DatabaseFile + suffix);
        File.WriteAllText(_db.DatabaseFile, "not a database");
    }

    private sealed class ControlledClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private TimerCallback? _callback;
        private object? _state;
        public TimeSpan TimerPeriod { get; private set; }
        public bool TimerDisposed { get; private set; }
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
        public void FireTimer() { if (!TimerDisposed) _callback?.Invoke(_state); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerPeriod = period;
            _callback = callback;
            _state = state;
            return new ControlledTimer(this);
        }
        private sealed class ControlledTimer(ControlledClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) { owner.TimerPeriod = period; return true; }
            public void Dispose() => owner.TimerDisposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
