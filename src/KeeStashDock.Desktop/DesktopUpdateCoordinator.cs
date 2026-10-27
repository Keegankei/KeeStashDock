using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Updates;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Desktop;

/// <summary>
/// Runs the downloaded application as a short-lived helper outside the live
/// installation. The non-elevated supervisor owns restart and health checks;
/// only the file operation is elevated when the target directory requires it.
/// </summary>
internal static class DesktopUpdateCoordinator
{
    internal const string ApplyArgument = "--apply-update";
    internal const string RecoveryArgument = "--recover-update";
    internal const string HealthArgument = "--update-complete";
    private const string OperationArgument = "--update-operation";
    internal static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.2";
    internal static string UpdatesDirectory => Path.Combine(ComponentInstallService.DefaultLocalDataDirectory, "Updates");
    private sealed record Attempt(string Token, int ParentId, long ParentTicks, string Language, string? DatabaseFile = null);
    internal sealed record Healthy(string Token, int ProcessId, long StartedTicks, string Version);
    internal sealed record UpdateProcessIdentity(int ProcessId, long StartedTicks);
    private static readonly PortableUpdateInstaller Installer = new();
    private static string L(string key) => LocalizationService.Instance[key];

    internal static string PendingFile(string installationDirectory)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory)).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        return Path.Combine(UpdatesDirectory, "pending-" + key + ".json");
    }

    internal static bool Launch(StagedUpdate staged, string installationDirectory, out string recoveryDirectory, string? databaseFile = null)
    {
        recoveryDirectory = Path.Combine(UpdatesDirectory, "transaction-" + Guid.NewGuid().ToString("N"));
        var transaction = Installer.Prepare(installationDirectory, staged.PackageDirectory, recoveryDirectory);
        using var current = Process.GetCurrentProcess();
        var attempt = new Attempt(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), current.Id,
            current.StartTime.ToUniversalTime().Ticks, LocalizationService.Instance.CurrentLanguage, databaseFile);
        WriteJson(Path.Combine(recoveryDirectory, "launch.json"), attempt);
        WriteJson(PendingFile(installationDirectory), recoveryDirectory);
        WriteRecoveryInstructions(transaction);
        using var helper = Process.Start(CreateHelperStartInfo(Path.Combine(staged.PackageDirectory, "KeeStashDock.exe"),
            ApplyArgument, recoveryDirectory)) ?? throw new IOException("The update helper did not start.");
        // Persist identity before the parent is allowed to exit. The helper repeats this
        // write under its lifetime lease, so a second startup cannot mistake it for an abandoned update.
        WriteJson(Path.Combine(recoveryDirectory, "supervisor.json"), Identity(helper));
        return true;
    }

    internal static ProcessStartInfo CreateHelperStartInfo(string executable, string action, string transactionDirectory)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable)! };
        info.ArgumentList.Add(action); info.ArgumentList.Add(transactionDirectory);
        return info;
    }

    /// <summary>Called before single-instance, settings, database or tray initialization.</summary>
    internal static async Task<bool> RunHelperIfRequestedAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is not (ApplyArgument or RecoveryArgument or OperationArgument)) return false;
        try
        {
            if (args[0] == OperationArgument)
            {
                if (args.Length != 3 || args[1] is not ("apply" or "rollback")) throw new InvalidDataException("Invalid update operation.");
                var transaction = Installer.Load(args[2]);
                ValidateHelperLocation(transaction);
                var operationAttempt = ReadJson<Attempt>(Path.Combine(transaction.TransactionDirectory, "launch.json"));
                ValidateAttempt(operationAttempt);
                if (!await WaitForParentAsync(operationAttempt.ParentId, operationAttempt.ParentTicks, TimeSpan.FromSeconds(60)))
                    throw new IOException("The application is still running; file replacement is not allowed.");
                if (args[1] == "rollback") await WaitForOwnedProcessAsync(transaction.TransactionDirectory, "replacement.json");
                using var operation = Process.GetCurrentProcess();
                WriteJson(Path.Combine(transaction.TransactionDirectory, "operation.json"), Identity(operation));
                if (args[1] == "apply") Installer.Apply(args[2]); else Installer.Rollback(args[2]);
                Environment.ExitCode = 0;
                return true;
            }
            if (args.Length != 2) throw new InvalidDataException("Invalid update arguments.");
            string directory = Path.GetFullPath(args[1]);
            var tx = Installer.Load(directory);
            ValidateHelperLocation(tx);
            using var supervisorLease = AcquireSupervisorLease(directory);
            using var supervisor = Process.GetCurrentProcess();
            WriteJson(Path.Combine(directory, "supervisor.json"), Identity(supervisor));
            var attempt = ReadJson<Attempt>(Path.Combine(directory, "launch.json"));
            ValidateAttempt(attempt);
            LocalizationService.Instance.SetLanguage(attempt.Language);
            if (!await WaitForParentAsync(attempt.ParentId, attempt.ParentTicks, TimeSpan.FromSeconds(60)))
                throw new IOException("The application did not exit; installation was not started.");

            if (args[0] == RecoveryArgument)
            {
                using var startupLease = AcquireStartupLease(directory);
                await WaitForOwnedProcessAsync(directory, "replacement.json");
                await WaitForOwnedProcessAsync(directory, "operation.json");
                await RunFileOperationAsync("rollback", tx);
                ClearPending(tx); RestartAfterRollback(tx, attempt);
                return true;
            }
            Process? replacement = null;
            try
            {
                await RunFileOperationAsync("apply", tx);
                replacement = StartInstalled(tx, directory) ?? throw new IOException("The updated application could not start.");
                long started = replacement.StartTime.ToUniversalTime().Ticks;
                WriteJson(Path.Combine(directory, "replacement.json"), new UpdateProcessIdentity(replacement.Id, started));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (replacement.HasExited) throw new IOException("The updated application exited before its startup health check.");
                    string healthFile = Path.Combine(directory, "healthy.json");
                    if (File.Exists(healthFile))
                    {
                        var health = ReadJson<Healthy>(healthFile);
                        if (HealthMatches(health, attempt.Token, new(replacement.Id, started), tx.TargetVersion))
                        {
                            // Give immediate startup failures a chance to surface before discarding rollback eligibility.
                            await Task.Delay(1500, timeout.Token);
                            if (replacement.HasExited) throw new IOException("The updated application failed after startup.");
                            Installer.Commit(directory);
                            // Commit is the durable success marker. Cleanup failure must never
                            // kill a healthy application or attempt a post-commit rollback.
                            try { ClearPending(tx); WriteJson(Path.Combine(directory, "completed.json"), true); }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                            return true;
                        }
                    }
                    await Task.Delay(150, timeout.Token);
                }
            }
            catch
            {
                // This is the exact child we launched, never a name-based process search.
                if (replacement is not null && !replacement.HasExited)
                {
                    replacement.Kill();
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await replacement.WaitForExitAsync(stop.Token);
                }
                using var startupLease = AcquireStartupLease(directory);
                await WaitForOwnedProcessAsync(directory, "replacement.json");
                await WaitForOwnedProcessAsync(directory, "operation.json");
                var failed = Installer.Load(directory);
                if (failed.State == PortableUpdateState.Prepared)
                    Installer.Rollback(directory); // No installation write occurred; do not prompt for elevation again.
                else if (failed.State != PortableUpdateState.RolledBack)
                    await RunFileOperationAsync("rollback", failed);
                ClearPending(tx);
                if (RestartAfterRollback(tx, attempt))
                    MessageBox.Show(L("Updates.Restored"), L("Updates.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { replacement?.Dispose(); }
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            string directory = args.LastOrDefault() ?? UpdatesDirectory;
            // A failed elevated child reports through its exit code; its supervisor provides the dialog.
            if (args[0] != OperationArgument)
                MessageBox.Show(L("Updates.RecoveryFailed") + "\n" + directory + "\n\n" + ex.Message,
                    L("Updates.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return true;
    }

    private static async Task RunFileOperationAsync(string operation, PortableUpdateTransaction transaction)
    {
        if (CanWriteInstallation(transaction))
        {
            if (operation == "apply") await Task.Run(() => Installer.Apply(transaction.TransactionDirectory));
            else await Task.Run(() => Installer.Rollback(transaction.TransactionDirectory));
            return;
        }
        // Keep the supervisor unelevated: restarting the normal app must preserve its user/profile context.
        var start = new ProcessStartInfo(Path.Combine(transaction.PackageDirectory, "KeeStashDock.exe"))
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(OperationArgument); start.ArgumentList.Add(operation); start.ArgumentList.Add(transaction.TransactionDirectory);
        using var elevated = Process.Start(start) ?? throw new IOException("The elevated helper did not start.");
        WriteJson(Path.Combine(transaction.TransactionDirectory, "operation.json"), Identity(elevated));
        await elevated.WaitForExitAsync(); // Never cancel an in-progress replacement.
        if (elevated.ExitCode != 0) throw new IOException("The elevated update operation failed or was denied.");
    }

    internal static bool PreviousVersionSupportsSchema(string version, int schema) =>
        Version.TryParse(version, out Version? parsed) && schema >= 0 &&
        schema <= (parsed >= new Version(1, 3, 0) ? 5 : 4);

    internal static void VerifyRollbackDatabase(string path, string previousVersion)
    {
        if (!File.Exists(path)) return;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        int schema = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
        {
            command.CommandText = "SELECT COUNT(*) FROM schema_version";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("The database version record is invalid.");
            command.CommandText = "SELECT version FROM schema_version";
            // Legacy databases can have an unset PRAGMA. Never trust a lower
            // header version over the application's migration version record.
            schema = Math.Max(schema, Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture));
        }
        if (!PreviousVersionSupportsSchema(previousVersion, schema))
            throw new InvalidDataException("The database requires the newer application.");
    }

    private static bool RestartAfterRollback(PortableUpdateTransaction transaction, Attempt attempt)
    {
        try
        {
            // Older parents did not record their data path. Resolve their portable
            // layout read-only, without creating a new Data folder in the helper.
            VerifyRollbackDatabases(transaction.InstallationDirectory, attempt.DatabaseFile, transaction.PreviousVersion);
            StartInstalled(transaction, null)?.Dispose();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            // File rollback must never silently downgrade or overwrite migrated data.
            MessageBox.Show(L("Updates.SchemaRecovery") + "\n\n" + transaction.TransactionDirectory,
                L("Updates.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    internal static void VerifyRollbackDatabases(string installationDirectory, string? recordedDatabase,
        string previousVersion, string? applicationDataRoot = null)
    {
        if (recordedDatabase is not null)
        {
            VerifyRollbackDatabase(recordedDatabase, previousVersion);
            return;
        }
        // Old parent versions did not persist the resolved data path. A stale
        // portable file can coexist with the active AppData fallback, so fail
        // closed if either existing candidate requires the newer application.
        string portable = Path.Combine(installationDirectory, "Data", "keestashdock.db");
        string fallback = Path.Combine(applicationDataRoot ?? Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "KeeStashDock", "keestashdock.db");
        VerifyRollbackDatabase(portable, previousVersion);
        if (!string.Equals(portable, fallback, StringComparison.OrdinalIgnoreCase))
            VerifyRollbackDatabase(fallback, previousVersion);
    }

    private static bool CanWriteInstallation(PortableUpdateTransaction transaction)
    {
        try
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { transaction.InstallationDirectory };
            foreach (var change in transaction.Changes)
            {
                string target = UpdatePathPolicy.ResolveUnderRoot(transaction.InstallationDirectory, change.Path);
                if (File.Exists(target))
                {
                    // Open without truncation to detect per-file ACL/read-only restrictions.
                    using var access = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                }
                string parent = Path.GetDirectoryName(target)!;
                while (!Directory.Exists(parent)) parent = Path.GetDirectoryName(parent)!;
                directories.Add(parent);
            }
            foreach (string directory in directories)
            {
                string probe = Path.Combine(directory, ".keestash-update-probe-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            }
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static async Task<bool> WaitForParentAsync(int processId, long startedTicks, TimeSpan timeout)
    {
        if (processId <= 0 || processId == Environment.ProcessId || startedTicks <= 0) return false;
        try
        {
            using var parent = Process.GetProcessById(processId);
            if (parent.StartTime.ToUniversalTime().Ticks != startedTicks) return true;
            using var deadline = new CancellationTokenSource(timeout);
            await parent.WaitForExitAsync(deadline.Token); return true;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (OperationCanceledException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    internal static bool CheckRecoveryBeforeStartup(string[] args)
    {
        if (args.Length == 2 && args[0] == HealthArgument)
        {
            using var current = Process.GetCurrentProcess();
            return ClaimHealthIdentity(args[1], AppContext.BaseDirectory, CurrentVersion, Identity(current));
        }
        string pending = PendingFile(AppContext.BaseDirectory);
        if (!File.Exists(pending)) return true;
        try
        {
            string directory = ReadJson<string>(pending);
            var tx = Installer.Load(directory);
            if (!SameDirectory(tx.InstallationDirectory, AppContext.BaseDirectory)) throw new InvalidDataException("Update target mismatch.");
            if (tx.State is PortableUpdateState.Committed or PortableUpdateState.RolledBack)
            { File.Delete(pending); return true; }
            var launch = ReadJson<Attempt>(Path.Combine(directory, "launch.json"));
            ValidateAttempt(launch);
            if (AnyOwnedProcessRunning(directory) || IsExactProcessRunning(new(launch.ParentId, launch.ParentTicks)))
            {
                MessageBox.Show(L("Updates.Running"), L("Updates.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            if (tx.State == PortableUpdateState.Prepared)
            {
                using var startupLease = AcquireStartupLease(directory);
                if (AnyOwnedProcessRunning(directory)) return false;
                Installer.Rollback(directory);
                ClearPending(tx);
                return true;
            }
            if (MessageBox.Show(L("Updates.Recovery") + "\n\n" + directory, L("Updates.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
            using var current = Process.GetCurrentProcess();
            var attempt = new Attempt(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), current.Id,
                current.StartTime.ToUniversalTime().Ticks, LocalizationService.Instance.CurrentLanguage, launch.DatabaseFile);
            WriteJson(Path.Combine(directory, "launch.json"), attempt);
            using var helper = Process.Start(CreateHelperStartInfo(Path.Combine(tx.PackageDirectory, "KeeStashDock.exe"), RecoveryArgument, directory))
                ?? throw new IOException("The recovery helper did not start.");
            WriteJson(Path.Combine(directory, "supervisor.json"), Identity(helper));
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(L("Updates.RecoveryFailed") + "\n" + pending + "\n\n" + ex.Message, L("Updates.Title"));
            return false;
        }
    }

    private static bool ValidateHealthTarget(string directory)
    {
        try
        {
            var tx = Installer.Load(directory);
            return SameDirectory(tx.InstallationDirectory, AppContext.BaseDirectory) &&
                tx.TargetVersion == CurrentVersion && tx.State == PortableUpdateState.AwaitingHealth;
        }
        catch { return false; }
    }

    /// <summary>
    /// Child startup and recovery use the same short lease. If the supervisor died
    /// immediately after Process.Start, recovery either sees this child's durable
    /// identity or completes rollback before the child can initialize its data/UI.
    /// A busy lease aborts startup; it never blocks the UI thread waiting for UAC.
    /// </summary>
    internal static bool ClaimHealthIdentity(string directory, string installationDirectory,
        string version, UpdateProcessIdentity identity)
    {
        try
        {
            using var startupLease = AcquireStartupLease(directory);
            var tx = Installer.Load(directory);
            if (!SameDirectory(tx.InstallationDirectory, installationDirectory) || tx.TargetVersion != version ||
                tx.State != PortableUpdateState.AwaitingHealth || !IsExactProcessRunning(identity)) return false;
            string path = Path.Combine(directory, "replacement.json");
            if (File.Exists(path))
            {
                var previous = ReadJson<UpdateProcessIdentity>(path);
                if (previous != identity && IsExactProcessRunning(previous)) return false;
            }
            WriteJson(path, identity);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    internal static void AcknowledgeStartup(string[] args)
    {
        if (args.Length != 2 || args[0] != HealthArgument || !ValidateHealthTarget(args[1])) return;
        var attempt = ReadJson<Attempt>(Path.Combine(args[1], "launch.json"));
        ValidateAttempt(attempt);
        using var current = Process.GetCurrentProcess();
        WriteJson(Path.Combine(args[1], "healthy.json"), new Healthy(attempt.Token, current.Id,
            current.StartTime.ToUniversalTime().Ticks, CurrentVersion));
    }

    private static Process? StartInstalled(PortableUpdateTransaction tx, string? transactionDirectory)
    {
        var start = new ProcessStartInfo(Path.Combine(tx.InstallationDirectory, "KeeStashDock.exe"))
        { UseShellExecute = false, WorkingDirectory = tx.InstallationDirectory };
        if (transactionDirectory is not null) { start.ArgumentList.Add(HealthArgument); start.ArgumentList.Add(transactionDirectory); }
        return Process.Start(start);
    }

    internal static async Task<bool> WaitForCompletionAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 2 || args[0] != HealthArgument) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(75));
        try
        {
            while (!deadline.IsCancellationRequested)
            {
                var state = Installer.Load(args[1]);
                if (!SameDirectory(state.InstallationDirectory, AppContext.BaseDirectory) || state.TargetVersion != CurrentVersion) return false;
                if (state.State == PortableUpdateState.Committed) return true;
                if (state.State != PortableUpdateState.AwaitingHealth) return false;
                await Task.Delay(200, deadline.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return false;
    }

    private static UpdateProcessIdentity Identity(Process process)
        => new(process.Id, process.StartTime.ToUniversalTime().Ticks);

    internal static bool IsExactProcessRunning(UpdateProcessIdentity identity)
    {
        if (identity.ProcessId <= 0 || identity.StartedTicks <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == identity.StartedTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // Unknown access must not authorize replacement.
    }

    internal static bool HealthMatches(Healthy health, string token, UpdateProcessIdentity expected, string version)
        => health.Token == token && health.ProcessId == expected.ProcessId && health.StartedTicks == expected.StartedTicks &&
           health.Version == version && token.Length == 64 && token.All(Uri.IsHexDigit);

    private static bool AnyOwnedProcessRunning(string directory)
        => new[] { "supervisor.json", "replacement.json", "operation.json" }.Any(name =>
        {
            string path = Path.Combine(directory, name);
            return File.Exists(path) && IsExactProcessRunning(ReadJson<UpdateProcessIdentity>(path));
        });

    internal static async Task WaitForOwnedProcessAsync(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) return;
        var identity = ReadJson<UpdateProcessIdentity>(path);
        if (identity.ProcessId <= 0 || identity.StartedTicks <= 0)
            throw new InvalidDataException("The recorded update process identity is invalid.");
        if (!IsExactProcessRunning(identity)) return;
        if (!await WaitForParentAsync(identity.ProcessId, identity.StartedTicks, TimeSpan.FromSeconds(30)))
            throw new IOException("A process belonging to this update is still running. Close it before recovery.");
    }

    private static FileStream AcquireSupervisorLease(string directory)
        => new(UpdatePathPolicy.ResolveUnderRoot(directory, "supervisor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal static FileStream AcquireStartupLease(string directory)
        => new(UpdatePathPolicy.ResolveUnderRoot(directory, "startup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void ValidateAttempt(Attempt attempt)
    {
        if (attempt.Token is null || attempt.Token.Length != 64 || !attempt.Token.All(Uri.IsHexDigit) ||
            attempt.ParentId <= 0 || attempt.ParentTicks <= 0 || string.IsNullOrWhiteSpace(attempt.Language))
            throw new InvalidDataException("The update launch identity is invalid.");
    }

    private static void ValidateHelperLocation(PortableUpdateTransaction tx)
    {
        if (!SameDirectory(AppContext.BaseDirectory, tx.PackageDirectory))
            throw new InvalidDataException("The update helper must run from its verified staged package.");
        // Validate the package again before executing any transaction, including elevated operations.
        tx.TargetManifest.VerifyFiles(tx.PackageDirectory);
    }
    private static bool SameDirectory(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static void ClearPending(PortableUpdateTransaction tx)
    {
        string pending = PendingFile(tx.InstallationDirectory);
        if (File.Exists(pending) && ReadJson<string>(pending) == tx.TransactionDirectory) File.Delete(pending);
    }
    internal static T ReadJson<T>(string path)
        => UpdateStateFile.ReadJson<T>(path, 65536, new JsonSerializerOptions { MaxDepth = 16 });
    private static void WriteJson<T>(string path, T value)
        => UpdateStateFile.WriteJson(path, value, 65536, new JsonSerializerOptions { MaxDepth = 16 });
    private static void WriteRecoveryInstructions(PortableUpdateTransaction tx)
    {
        File.WriteAllText(Path.Combine(tx.TransactionDirectory, "RECOVERY.txt"),
            "KeeStashDock update recovery\n\nInstallation: " + tx.InstallationDirectory +
            "\nPrevious version: " + tx.PreviousVersion + "\nTarget version: " + tx.TargetVersion +
            "\n\nKeep this folder and the staged package until recovery succeeds. User Data and AppData are not replaced.\n" +
            "Start the installed application to use its recovery prompt. If its executable cannot start, run:\n\"" +
            Path.Combine(tx.PackageDirectory, "KeeStashDock.exe") + "\" " + RecoveryArgument + " \"" + tx.TransactionDirectory + "\"\n" +
            "Close all application instances first. Never copy the backup over your Data folder.\n" +
            "Version 1.3.0 uses database schema v5. Application-file rollback does not downgrade a database. " +
            "If the database has migrated, keep it and reinstall 1.3.0 or later. Only restore a pre-migration snapshot explicitly, after preserving newer data.\n");
    }
}
