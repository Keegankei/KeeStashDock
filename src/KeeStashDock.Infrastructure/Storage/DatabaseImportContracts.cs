namespace KeeStashDock.Infrastructure.Storage;

public interface IDatabaseFileOperations
{
    bool Exists(string path);
    long GetLength(string path);
    void CreateDirectory(string path);
    void CopyToNew(string sourcePath, string destinationPath);
    void Replace(string sourcePath, string destinationPath, string backupPath);
    void MoveNew(string sourcePath, string destinationPath);
    void DeleteIfExists(string path);
}

public sealed class PhysicalDatabaseFileOperations : IDatabaseFileOperations
{
    private readonly bool _forceMoveFallback;

    public PhysicalDatabaseFileOperations(bool forceMoveFallback = false)
        => _forceMoveFallback = forceMoveFallback;

    public bool Exists(string path) => File.Exists(path);

    public long GetLength(string path) => new FileInfo(path).Length;

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyToNew(string sourcePath, string destinationPath)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            FileOptions.WriteThrough);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    public void Replace(string sourcePath, string destinationPath, string backupPath)
    {
        if (!_forceMoveFallback)
        {
            try
            {
                File.Replace(
                    sourcePath,
                    destinationPath,
                    backupPath,
                    ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Same-volume overwrite move maps to an atomic rename on
                // supported filesystems. It never degrades to File.Copy.
            }
        }

        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    public void MoveNew(string sourcePath, string destinationPath)
        => File.Move(sourcePath, destinationPath);

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public interface IApplicationRestarter
{
    /// <summary>
    /// Schedules a replacement process. The UI closes the current process
    /// only after the import task has returned and cleaned temporary files.
    /// </summary>
    bool TryRestart();
}

public interface IDatabaseImportObserver
{
    void OnStage(DatabaseImportStage stage);
}

public enum DatabaseImportStage
{
    AfterStaging,
    AfterSafetyBackup,
    BeforeReplacement,
    AfterReplacement,
    BeforeRestart,
}

public sealed record DatabaseImportResult(
    string SafetyBackupFile,
    int ImportedSchemaVersion);

public class DatabaseImportException : Exception
{
    public DatabaseImportException(string message) : base(message)
    {
    }

    public DatabaseImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DatabaseRestartRequiredException : DatabaseImportException
{
    public DatabaseRestartRequiredException(string message) : base(message)
    {
    }

    public DatabaseRestartRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
