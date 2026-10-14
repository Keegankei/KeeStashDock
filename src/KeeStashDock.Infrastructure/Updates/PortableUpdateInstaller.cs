using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeeStashDock.Infrastructure.Updates;

public enum PortableUpdateState { Prepared, Applying, AwaitingHealth, RollingBack, RolledBack, Committed, RecoveryRequired }
public sealed record PortableUpdateChange(string Path, UpdatePackageFile? Previous, UpdatePackageFile? Next);
public sealed record PortableUpdateProgress(string Path, int Completed, int Total);
public sealed record PortableUpdateTransaction(int Format, string Id, string InstallationDirectory,
    string PackageDirectory, string TransactionDirectory, string PreviousVersion, string TargetVersion,
    PortableUpdateState State, UpdatePackageManifest PreviousManifest, UpdatePackageManifest TargetManifest,
    IReadOnlyList<PortableUpdateChange> Changes);

/// <summary>
/// Replaces only manifest-owned application files. The caller must stop the exact
/// application instance, back up active data, and acknowledge a healthy new process
/// before Commit. Backups and the durable journal are retained for recovery.
/// </summary>
public sealed class PortableUpdateInstaller
{
    public const string TransactionFileName = "transaction.json";
    private const int MaximumJournalBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16
    };

    public PortableUpdateTransaction Prepare(string installationDirectory, string packageDirectory, string transactionDirectory)
    {
        string install = CanonicalDirectory(installationDirectory), package = CanonicalDirectory(packageDirectory),
            transaction = CanonicalDirectory(transactionDirectory);
        EnsureSeparate(install, package);
        EnsureSeparate(install, transaction);
        EnsureSeparate(package, transaction);
        using var lease = AcquireInstallLock(install);
        if (!Directory.Exists(install) || !Directory.Exists(package))
            throw new DirectoryNotFoundException("The installation and verified package must exist.");
        if (Directory.Exists(transaction) && Directory.EnumerateFileSystemEntries(transaction).Any())
            throw new IOException("The transaction directory must be empty.");
        var previous = UpdatePackageManifest.Load(install);
        var next = UpdatePackageManifest.Load(package);
        if (!Version.TryParse(previous.Version, out var previousVersion) ||
            !Version.TryParse(next.Version, out var nextVersion) || nextVersion <= previousVersion)
            throw new InvalidDataException("The update must have a newer supported version.");
        next.VerifyFiles(package);
        var oldFiles = previous.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var nextFiles = next.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var changes = new List<PortableUpdateChange>();
        foreach (string path in oldFiles.Keys.Union(nextFiles.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            oldFiles.TryGetValue(path, out var oldFile);
            nextFiles.TryGetValue(path, out var nextFile);
            string destination = UpdatePathPolicy.ResolveUnderRoot(install, path);
            if (UpdatePathPolicy.IsPreservedAsset(path))
            {
                if (Directory.Exists(destination)) throw new IOException("A component file path is occupied by a directory.");
                if (File.Exists(destination) || nextFile is null) continue;
                oldFile = null; // An absent optional model is installed without claiming any existing component.
            }
            EnsureDestination(destination);
            if (oldFile is not null) VerifyFile(destination, oldFile);
            else if (File.Exists(destination)) throw new IOException($"An unowned file occupies the update path: {path}");
            changes.Add(new(path, oldFile, nextFile));
        }
        string manifestName = UpdatePackageManifest.FileName;
        changes.Add(new(manifestName, Describe(Path.Combine(install, manifestName), manifestName),
            Describe(Path.Combine(package, manifestName), manifestName)));
        ValidatePathConflicts(changes);
        EnsureFreeSpace(transaction, changes.Sum(change => change.Previous?.Length ?? 0) + 8 * 1024 * 1024L);
        EnsureFreeSpace(install, changes.Sum(change => change.Next?.Length ?? 0) + 8 * 1024 * 1024L);
        var state = new PortableUpdateTransaction(1, Guid.NewGuid().ToString("N"), install, package, transaction,
            previous.Version, next.Version, PortableUpdateState.Prepared, previous, next, changes);
        Directory.CreateDirectory(transaction);
        using var journalLock = LockJournal(transaction);
        foreach (var change in changes.Where(change => change.Previous is not null))
        {
            string original = UpdatePathPolicy.ResolveUnderRoot(install, change.Path);
            string backup = BackupPath(state, change.Path);
            CopyNewVerified(original, backup, change.Previous!);
        }
        // No installation write has occurred. A failed prepare leaves only recovery/staging files.
        Save(state);
        return state;
    }

    public PortableUpdateTransaction Load(string transactionDirectory)
    {
        string directory = CanonicalDirectory(transactionDirectory);
        string journal = UpdatePathPolicy.ResolveUnderRoot(directory, TransactionFileName);
        var state = UpdateStateFile.ReadJson<PortableUpdateTransaction>(journal, MaximumJournalBytes, Json);
        ValidateState(state, directory);
        return state;
    }

    public PortableUpdateTransaction Apply(string transactionDirectory, IProgress<PortableUpdateProgress>? progress = null)
    {
        var initial = Load(transactionDirectory);
        using var lease = AcquireInstallLock(initial.InstallationDirectory);
        using var journalLock = LockJournal(initial.TransactionDirectory);
        var state = Load(transactionDirectory);
        if (state.State != PortableUpdateState.Prepared)
            throw new InvalidOperationException("Only a prepared transaction may be applied. Recover interrupted transactions first.");
        VerifyBackups(state);
        state.TargetManifest.VerifyFiles(state.PackageDirectory);
        VerifyOriginals(state); // Detect modifications/collisions after Prepare before writing any live file.
        EnsureFreeSpace(state.InstallationDirectory, state.Changes.Sum(change => change.Next?.Length ?? 0) + 8 * 1024 * 1024L);
        state = Save(state with { State = PortableUpdateState.Applying });
        try
        {
            int completed = 0;
            foreach (var change in state.Changes)
            {
                string destination = UpdatePathPolicy.ResolveUnderRoot(state.InstallationDirectory, change.Path);
                if (change.Previous is not null) VerifyFile(destination, change.Previous);
                else if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("An update destination changed while applying the package.");
                if (change.Next is null) File.Delete(destination);
                else ReplaceVerified(UpdatePathPolicy.ResolveUnderRoot(state.PackageDirectory, change.Path),
                    destination, change.Next, state.Id);
                progress?.Report(new(change.Path, ++completed, state.Changes.Count));
            }
            VerifyApplied(state);
            return Save(state with { State = PortableUpdateState.AwaitingHealth });
        }
        catch (Exception applyError)
        {
            try { RollbackCore(state); }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Update failed and requires recovery. Installation data and the backup were retained.",
                    applyError, rollbackError);
            }
            throw new IOException("Update failed. The previous application files were restored.", applyError);
        }
    }

    public PortableUpdateTransaction Rollback(string transactionDirectory)
    {
        var initial = Load(transactionDirectory);
        using var lease = AcquireInstallLock(initial.InstallationDirectory);
        using var journalLock = LockJournal(initial.TransactionDirectory);
        return RollbackCore(Load(transactionDirectory));
    }

    private static PortableUpdateTransaction RollbackCore(PortableUpdateTransaction state)
    {
        if (state.State == PortableUpdateState.Committed)
            throw new InvalidOperationException("A committed healthy update cannot be rolled back automatically.");
        if (state.State == PortableUpdateState.RolledBack) return state;
        try
        {
            VerifyBackups(state);
            // Refuse to overwrite a post-crash custom change, even during recovery.
            foreach (var change in state.Changes)
            {
                string destination = UpdatePathPolicy.ResolveUnderRoot(state.InstallationDirectory, change.Path);
                EnsureDestination(destination);
                if (File.Exists(destination) && !Matches(destination, change.Previous) && !Matches(destination, change.Next))
                    throw new IOException($"A file changed outside the update; preserve it before recovering: {change.Path}");
            }
            state = Save(state with { State = PortableUpdateState.RollingBack });
            foreach (var change in state.Changes.Reverse())
            {
                string destination = UpdatePathPolicy.ResolveUnderRoot(state.InstallationDirectory, change.Path);
                if (change.Previous is null)
                {
                    if (File.Exists(destination))
                    {
                        VerifyFile(destination, change.Next!);
                        File.Delete(destination);
                    }
                }
                else if (!Matches(destination, change.Previous))
                    ReplaceVerified(BackupPath(state, change.Path), destination, change.Previous, state.Id + "-rollback");
            }
            VerifyOriginals(state);
            return Save(state with { State = PortableUpdateState.RolledBack });
        }
        catch
        {
            Save(state with { State = PortableUpdateState.RecoveryRequired });
            throw;
        }
    }

    /// <summary>Call only after the exact newly started application reports a healthy startup.</summary>
    public PortableUpdateTransaction Commit(string transactionDirectory)
    {
        var initial = Load(transactionDirectory);
        using var lease = AcquireInstallLock(initial.InstallationDirectory);
        using var journalLock = LockJournal(initial.TransactionDirectory);
        var state = Load(transactionDirectory);
        if (state.State == PortableUpdateState.Committed) return state;
        if (state.State != PortableUpdateState.AwaitingHealth)
            throw new InvalidOperationException("Only an applied update awaiting a health acknowledgement can be committed.");
        VerifyApplied(state);
        return Save(state with { State = PortableUpdateState.Committed });
    }

    private static void VerifyOriginals(PortableUpdateTransaction state)
    {
        foreach (var change in state.Changes)
        {
            string destination = UpdatePathPolicy.ResolveUnderRoot(state.InstallationDirectory, change.Path);
            EnsureDestination(destination);
            if (change.Previous is not null) VerifyFile(destination, change.Previous);
            else if (File.Exists(destination)) throw new IOException($"An unowned file occupies the update path: {change.Path}");
        }
    }

    private static void VerifyApplied(PortableUpdateTransaction state)
    {
        foreach (var change in state.Changes)
        {
            string destination = UpdatePathPolicy.ResolveUnderRoot(state.InstallationDirectory, change.Path);
            EnsureDestination(destination);
            if (change.Next is not null) VerifyFile(destination, change.Next);
            else if (File.Exists(destination)) throw new IOException($"An obsolete managed file is still present: {change.Path}");
        }
    }

    private static void VerifyBackups(PortableUpdateTransaction state)
    {
        foreach (var change in state.Changes.Where(change => change.Previous is not null))
            VerifyFile(BackupPath(state, change.Path), change.Previous!);
    }

    private static void ValidateState(PortableUpdateTransaction state, string directory)
    {
        if (state.Format != 1 || !Guid.TryParseExact(state.Id, "N", out _) || !Enum.IsDefined(state.State) ||
            !string.Equals(CanonicalDirectory(state.TransactionDirectory), directory, StringComparison.OrdinalIgnoreCase) ||
            state.Changes is null || state.Changes.Count is < 1 or > 20001 ||
            state.PreviousManifest is null || state.TargetManifest is null)
            throw new InvalidDataException("Invalid update transaction metadata.");
        CanonicalDirectory(state.InstallationDirectory);
        CanonicalDirectory(state.PackageDirectory);
        EnsureSeparate(state.InstallationDirectory, state.PackageDirectory);
        EnsureSeparate(state.InstallationDirectory, directory);
        EnsureSeparate(state.PackageDirectory, directory);
        state.PreviousManifest.Validate();
        state.TargetManifest.Validate();
        if (state.PreviousVersion != state.PreviousManifest.Version || state.TargetVersion != state.TargetManifest.Version ||
            !Version.TryParse(state.PreviousVersion, out var previousVersion) ||
            !Version.TryParse(state.TargetVersion, out var targetVersion) || targetVersion <= previousVersion)
            throw new InvalidDataException("Invalid transaction version metadata.");
        var oldFiles = state.PreviousManifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var nextFiles = state.TargetManifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in state.Changes)
        {
            if (change is null || UpdatePathPolicy.ValidateRelativePath(change.Path) != change.Path || !paths.Add(change.Path))
                throw new InvalidDataException("The transaction contains an invalid or duplicate managed path.");
            if (change.Previous is null && change.Next is null) throw new InvalidDataException("Empty transaction change.");
            foreach (var file in new[] { change.Previous, change.Next }.OfType<UpdatePackageFile>())
                if (file.Path != change.Path || file.Length < 0 || file.Length > 1024 * 1024 * 1024L ||
                    file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid transaction file metadata.");
            if (change.Path.Equals(UpdatePackageManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                if (change.Previous is null || change.Next is null)
                    throw new InvalidDataException("The ownership manifest must be backed up and replaced.");
                continue;
            }
            oldFiles.TryGetValue(change.Path, out var oldFile);
            nextFiles.TryGetValue(change.Path, out var nextFile);
            if (UpdatePathPolicy.IsPreservedAsset(change.Path))
            {
                if (change.Previous is not null || !Same(change.Next, nextFile))
                    throw new InvalidDataException("An update cannot replace an installed component asset.");
            }
            else if (!Same(change.Previous, oldFile) || !Same(change.Next, nextFile))
                throw new InvalidDataException("A transaction change is not authorized by its ownership manifests.");
        }
        if (!paths.Contains(UpdatePackageManifest.FileName) || oldFiles.Keys.Union(nextFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(path => !UpdatePathPolicy.IsPreservedAsset(path)).Any(path => !paths.Contains(path)))
            throw new InvalidDataException("The transaction omits a managed file.");
        ValidatePathConflicts(state.Changes);
    }

    private static bool Same(UpdatePackageFile? left, UpdatePackageFile? right)
        => left is null || right is null ? left is null && right is null
            : left.Path.Equals(right.Path, StringComparison.OrdinalIgnoreCase) && left.Length == right.Length &&
              left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase);

    private static void ValidatePathConflicts(IEnumerable<PortableUpdateChange> changes)
    {
        var files = changes.Select(change => change.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
            for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                if (files.Contains(path[..slash])) throw new InvalidDataException("Managed files have conflicting directory paths.");
    }

    private static string CanonicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Update locations must be absolute paths.");
        string result = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (result.Length <= (Path.GetPathRoot(result)?.TrimEnd(Path.DirectorySeparatorChar).Length ?? 0))
            throw new InvalidDataException("A drive root is not an update location.");
        UpdatePathPolicy.EnsureNoReparsePoints(result);
        return result;
    }

    private static void EnsureSeparate(string first, string second)
    {
        string a = CanonicalDirectory(first), b = CanonicalDirectory(second);
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installation, package, and transaction directories must not overlap.");
    }

    private static void EnsureDestination(string path)
    {
        UpdatePathPolicy.EnsureNoReparsePoints(path);
        if (Directory.Exists(path)) throw new IOException("A managed file path is occupied by a directory.");
        for (string? parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            if (File.Exists(parent)) throw new IOException("A managed directory path is occupied by a file.");
    }

    private static void EnsureFreeSpace(string directory, long bytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(directory)!);
        if (drive.AvailableFreeSpace < bytes) throw new IOException("There is insufficient disk space for a safe update and rollback.");
    }

    private static UpdatePackageFile Describe(string path, string relative)
    {
        UpdatePathPolicy.EnsureNoReparsePoints(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new(relative, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static bool Matches(string path, UpdatePackageFile? expected)
        => expected is not null && File.Exists(path) && Same(Describe(path, expected.Path), expected);

    private static void VerifyFile(string path, UpdatePackageFile expected)
    {
        if (!Matches(path, expected)) throw new IOException($"A managed file is missing or has been modified: {expected.Path}");
    }

    private static string BackupPath(PortableUpdateTransaction state, string relative)
        => UpdatePathPolicy.ResolveUnderRoot(state.TransactionDirectory, "backup/" + relative);

    private static void CopyNewVerified(string source, string destination, UpdatePackageFile expected)
    {
        EnsureDestination(destination);
        UpdatePathPolicy.EnsureNoReparsePoints(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        bool created = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       128 * 1024, FileOptions.WriteThrough))
            {
                created = true;
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            VerifyFile(destination, expected);
        }
        catch
        {
            if (created && File.Exists(destination))
            {
                UpdatePathPolicy.EnsureNoReparsePoints(destination);
                File.Delete(destination);
            }
            throw;
        }
    }

    private static void ReplaceVerified(string source, string destination, UpdatePackageFile expected, string transactionId)
    {
        EnsureDestination(destination);
        string temporary = destination + ".keestashdock-update-" + transactionId + ".tmp";
        UpdatePathPolicy.EnsureNoReparsePoints(temporary);
        // A leftover temp file is transaction-owned, but do not overwrite an unrelated collision.
        if (File.Exists(temporary))
        {
            VerifyFile(temporary, expected);
            File.Delete(temporary);
        }
        bool created = false;
        try
        {
            CopyNewVerified(source, temporary, expected);
            created = true;
            UpdatePathPolicy.EnsureNoReparsePoints(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (created && File.Exists(temporary))
            {
                UpdatePathPolicy.EnsureNoReparsePoints(temporary);
                File.Delete(temporary);
            }
        }
    }

    private static PortableUpdateTransaction Save(PortableUpdateTransaction state)
    {
        ValidateState(state, state.TransactionDirectory);
        string journal = UpdatePathPolicy.ResolveUnderRoot(state.TransactionDirectory, TransactionFileName);
        UpdateStateFile.WriteJson(journal, state, MaximumJournalBytes, Json);
        return state;
    }

    private static FileStream LockJournal(string directory)
        => new(UpdatePathPolicy.ResolveUnderRoot(directory, "transaction.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static IDisposable AcquireInstallLock(string directory)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())));
        var mutex = new Mutex(false, "KeeStashDock.Update." + key);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new IOException("Another update is using this installation."); }
        return new MutexLease(mutex);
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
