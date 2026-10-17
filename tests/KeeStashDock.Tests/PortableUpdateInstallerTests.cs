using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeeStashDock.Infrastructure.Updates;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class PortableUpdateInstallerTests
{
    private string _root = null!, _install = null!, _package = null!, _transaction = null!;
    private readonly PortableUpdateInstaller _installer = new();
    private static readonly JsonSerializerOptions JournalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "PortableUpdate", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "installed");
        _package = Path.Combine(_root, "package");
        _transaction = Path.Combine(_root, "transaction");
        WritePackage(_install, "1.2.2", new()
        {
            ["KeeStashDock.exe"] = "old application", ["library.dll"] = "old library", ["obsolete.dll"] = "obsolete",
            ["OcrModels/eng.traineddata"] = "bundled old model", ["OcrModels/Setup.ps1"] = "old setup"
        });
        WritePackage(_package, "1.2.3", new()
        {
            ["KeeStashDock.exe"] = "new application", ["library.dll"] = "new library", ["added.dll"] = "new dependency",
            ["OcrModels/eng.traineddata"] = "new bundled model", ["OcrModels/chi_sim.traineddata"] = "new optional model",
            ["OcrModels/Setup.ps1"] = "new setup"
        });
        Write(_install, "Data/settings.json", "user settings");
        Write(_install, "Data/snippets.db", "synthetic database");
        Write(_install, "my-notes.txt", "custom notes");
        Write(_install, "OfflineTranslation/runtime/python/pythonw.exe", "installed runtime");
        Write(_install, "OcrModels/eng.traineddata", "user installed model");
    }

    [TestCleanup]
    public void Cleanup()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "PortableUpdate")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(_root);
        Assert.IsTrue(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    [TestMethod]
    public void UpgradeReplacesOwnedFilesAndPreservesUserDataAndInstalledAssets()
    {
        var prepared = _installer.Prepare(_install, _package, _transaction);
        Assert.AreEqual(PortableUpdateState.Prepared, prepared.State);
        Assert.AreEqual("old application", Read(_install, "KeeStashDock.exe"));
        var applied = _installer.Apply(_transaction);
        Assert.AreEqual(PortableUpdateState.AwaitingHealth, applied.State);
        Assert.AreEqual("new application", Read(_install, "KeeStashDock.exe"));
        Assert.AreEqual("new setup", Read(_install, "OcrModels/Setup.ps1"));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "obsolete.dll")));
        Assert.AreEqual("new dependency", Read(_install, "added.dll"));
        Assert.AreEqual("user installed model", Read(_install, "OcrModels/eng.traineddata"));
        Assert.AreEqual("new optional model", Read(_install, "OcrModels/chi_sim.traineddata"));
        AssertPreserved();
        Assert.AreEqual("1.2.3", UpdatePackageManifest.Load(_install).Version);
        Assert.AreEqual(PortableUpdateState.Committed, _installer.Commit(_transaction).State);
        Assert.AreEqual("old application", Read(_transaction, "backup/KeeStashDock.exe"));
        Assert.ThrowsExactly<InvalidOperationException>(() => _installer.Rollback(_transaction));
    }

    [TestMethod]
    public void OptionalOcrUpgradePreservesLegacyEngineWhileUpdatingSetup()
    {
        WritePackage(_install, "1.2.2", new()
        {
            ["KeeStashDock.exe"] = "old application", ["library.dll"] = "old library", ["obsolete.dll"] = "obsolete",
            ["OcrModels/Setup.ps1"] = "old setup", ["OcrModels/eng.traineddata"] = "user installed model",
            ["x64/tesseract.exe"] = "legacy engine", ["x64/tesseract55.dll"] = "legacy library",
            ["x64/leptonica-1.85.0.dll"] = "legacy image library"
        });
        _installer.Prepare(_install, _package, _transaction);
        _installer.Apply(_transaction);
        Assert.AreEqual("legacy engine", Read(_install, "x64/tesseract.exe"));
        Assert.AreEqual("legacy library", Read(_install, "x64/tesseract55.dll"));
        Assert.AreEqual("legacy image library", Read(_install, "x64/leptonica-1.85.0.dll"));
        Assert.AreEqual("new setup", Read(_install, "OcrModels/Setup.ps1"));
        AssertPreserved();
    }

    [TestMethod]
    public void FlatPackageCanMoveToBundledLayoutAndRollBackWithoutMovingData()
    {
        // Only package ownership changes; the entry point and portable data root
        // stay in place, including when a new executable fails its health check.
        string organized = Path.Combine(_root, "organized-package");
        WritePackage(organized, "1.2.3", new()
        {
            ["KeeStashDock.exe"] = "bundled application",
            ["StartHere.md"] = "package guide",
            ["Docs/Updating.md"] = "upgrade guide",
            ["Licenses/LICENSE"] = "project license",
            ["x64/tesseract.exe"] = "independent OCR engine",
            ["OcrModels/Setup.ps1"] = "new setup"
        });
        _installer.Prepare(_install, organized, _transaction);
        _installer.Apply(_transaction);
        Assert.AreEqual("bundled application", Read(_install, "KeeStashDock.exe"));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "library.dll")));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "obsolete.dll")));
        Assert.AreEqual("upgrade guide", Read(_install, "Docs/Updating.md"));
        Assert.AreEqual("project license", Read(_install, "Licenses/LICENSE"));
        Assert.AreEqual("independent OCR engine", Read(_install, "x64/tesseract.exe"));
        Assert.AreEqual("user installed model", Read(_install, "OcrModels/eng.traineddata"));
        AssertPreserved();

        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Rollback(_transaction).State);
        AssertOldApplication();
        Assert.AreEqual("old library", Read(_install, "library.dll"));
        Assert.AreEqual("obsolete", Read(_install, "obsolete.dll"));
        Assert.AreEqual("old setup", Read(_install, "OcrModels/Setup.ps1"));
        Assert.AreEqual("1.2.2", UpdatePackageManifest.Load(_install).Version);
        Assert.IsFalse(File.Exists(Path.Combine(_install, "Docs", "Updating.md")));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "Licenses", "LICENSE")));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "x64", "tesseract.exe")));
        Assert.AreEqual("user installed model", Read(_install, "OcrModels/eng.traineddata"));
        AssertPreserved();
    }

    [TestMethod]
    public void FirstUpgradeWithoutOwnershipManifestIsRefused()
    {
        File.Delete(Path.Combine(_install, UpdatePackageManifest.FileName));
        Assert.ThrowsExactly<InvalidDataException>(() => _installer.Prepare(_install, _package, _transaction));
        AssertOldApplication();
        Assert.IsFalse(Directory.Exists(_transaction));
    }

    [TestMethod]
    public void ModifiedManagedFileIsRefusedBeforeAnyInstallationMutation()
    {
        Write(_install, "library.dll", "locally changed library");
        Assert.ThrowsExactly<IOException>(() => _installer.Prepare(_install, _package, _transaction));
        AssertOldApplication();
        Assert.AreEqual("locally changed library", Read(_install, "library.dll"));
        Assert.IsFalse(Directory.Exists(_transaction));
    }

    [TestMethod]
    public void NewPackageCannotClobberUnownedCustomFile()
    {
        Write(_install, "added.dll", "custom file with same name");
        Assert.ThrowsExactly<IOException>(() => _installer.Prepare(_install, _package, _transaction));
        AssertOldApplication();
        Assert.AreEqual("custom file with same name", Read(_install, "added.dll"));
        Assert.IsFalse(Directory.Exists(_transaction));
    }

    [TestMethod]
    public void ChangesAfterPreparationAreDetectedBeforeApplying()
    {
        _installer.Prepare(_install, _package, _transaction);
        Write(_install, "library.dll", "changed after preparation");
        Assert.ThrowsExactly<IOException>(() => _installer.Apply(_transaction));
        AssertOldApplication();
        Assert.AreEqual(PortableUpdateState.Prepared, _installer.Load(_transaction).State);
    }

    [TestMethod]
    public void AlteredStagedPayloadCannotReachInstallation()
    {
        _installer.Prepare(_install, _package, _transaction);
        Write(_package, "KeeStashDock.exe", "modified package");
        Assert.ThrowsExactly<InvalidDataException>(() => _installer.Apply(_transaction));
        AssertOldApplication();
        Assert.AreEqual(PortableUpdateState.Prepared, _installer.Load(_transaction).State);
    }

    [TestMethod]
    public void FailedHealthCheckRollsBackWithoutRemovingUserFiles()
    {
        _installer.Prepare(_install, _package, _transaction);
        _installer.Apply(_transaction);
        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Rollback(_transaction).State);
        AssertOldApplication();
        Assert.AreEqual("old library", Read(_install, "library.dll"));
        Assert.AreEqual("obsolete", Read(_install, "obsolete.dll"));
        Assert.AreEqual("old setup", Read(_install, "OcrModels/Setup.ps1"));
        Assert.AreEqual("1.2.2", UpdatePackageManifest.Load(_install).Version);
        Assert.IsFalse(File.Exists(Path.Combine(_install, "added.dll")));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "OcrModels", "chi_sim.traineddata")));
        Assert.AreEqual("user installed model", Read(_install, "OcrModels/eng.traineddata"));
        AssertPreserved();
        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Rollback(_transaction).State);
    }

    [TestMethod]
    public void InterruptedApplyRestoresMissingAndPartiallyReplacedFiles()
    {
        var state = _installer.Prepare(_install, _package, _transaction);
        SaveJournal(state with { State = PortableUpdateState.Applying });
        File.Copy(Path.Combine(_package, "KeeStashDock.exe"), Path.Combine(_install, "KeeStashDock.exe"), overwrite: true);
        File.Delete(Path.Combine(_install, "obsolete.dll"));
        File.Copy(Path.Combine(_package, "added.dll"), Path.Combine(_install, "added.dll"));
        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Rollback(_transaction).State);
        AssertOldApplication();
        Assert.AreEqual("obsolete", Read(_install, "obsolete.dll"));
        Assert.IsFalse(File.Exists(Path.Combine(_install, "added.dll")));
        AssertPreserved();
    }

    [TestMethod]
    public void MidReplacementFailureAutomaticallyRestoresPreviousVersion()
    {
        _installer.Prepare(_install, _package, _transaction);
        Assert.ThrowsExactly<IOException>(() => _installer.Apply(_transaction,
            new InlineProgress(_ => throw new IOException("Injected disk failure after a replacement."))));
        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Load(_transaction).State);
        AssertOldApplication();
        Assert.AreEqual("old library", Read(_install, "library.dll"));
        AssertPreserved();
    }

    [TestMethod]
    public void LockedFileKeepsRecoveryCopyAndRollbackSucceedsAfterUnlock()
    {
        _installer.Prepare(_install, _package, _transaction);
        FileStream? locked = null;
        try
        {
            Assert.ThrowsExactly<AggregateException>(() => _installer.Apply(_transaction,
                new InlineProgress(progress =>
                {
                    if (progress.Completed == 1)
                        locked = new FileStream(Path.Combine(_install, "library.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
                })));
            Assert.AreEqual(PortableUpdateState.RecoveryRequired, _installer.Load(_transaction).State);
            Assert.AreEqual("old application", Read(_transaction, "backup/KeeStashDock.exe"));
            AssertPreserved();
        }
        finally { locked?.Dispose(); }
        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Rollback(_transaction).State);
        AssertOldApplication();
    }

    [TestMethod]
    public void UnexpectedPostUpdateChangeIsPreservedForManualRecovery()
    {
        _installer.Prepare(_install, _package, _transaction);
        _installer.Apply(_transaction);
        Write(_install, "KeeStashDock.exe", "external replacement after update");
        Assert.ThrowsExactly<IOException>(() => _installer.Rollback(_transaction));
        Assert.AreEqual(PortableUpdateState.RecoveryRequired, _installer.Load(_transaction).State);
        Assert.AreEqual("external replacement after update", Read(_install, "KeeStashDock.exe"));
        Assert.AreEqual("old application", Read(_transaction, "backup/KeeStashDock.exe"));
        AssertPreserved();
    }

    [TestMethod]
    public void CorruptBackupCannotReplaceInstalledApplication()
    {
        _installer.Prepare(_install, _package, _transaction);
        Write(_transaction, "backup/KeeStashDock.exe", "corrupt backup");
        Assert.ThrowsExactly<IOException>(() => _installer.Apply(_transaction));
        AssertOldApplication();
        Assert.AreEqual(PortableUpdateState.Prepared, _installer.Load(_transaction).State);
    }

    [TestMethod]
    public void TransactionCannotAuthorizeAnUnownedUserDataPath()
    {
        var state = _installer.Prepare(_install, _package, _transaction);
        var extra = new UpdatePackageFile("Data/settings.json", 0, new string('0', 64));
        SaveJournal(state with { Changes = state.Changes.Append(new PortableUpdateChange(extra.Path, null, extra)).ToArray() });
        Assert.ThrowsExactly<InvalidDataException>(() => _installer.Load(_transaction));
        AssertOldApplication();
        AssertPreserved();
    }

    [TestMethod]
    public void StagingAndTransactionCannotOverlapTheInstallation()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => _installer.Prepare(_install, Path.Combine(_install, "stage"), _transaction));
        Assert.ThrowsExactly<InvalidDataException>(() => _installer.Prepare(_install, _package, Path.Combine(_install, "transaction")));
        Assert.ThrowsExactly<InvalidDataException>(() => _installer.Prepare(_install, _package, Path.Combine(_package, "transaction")));
        AssertOldApplication();
    }

    [TestMethod]
    public void CommitRequiresAppliedStateAndVerifiesNewFileHashes()
    {
        _installer.Prepare(_install, _package, _transaction);
        Assert.ThrowsExactly<InvalidOperationException>(() => _installer.Commit(_transaction));
        _installer.Apply(_transaction);
        File.Delete(Path.Combine(_install, "library.dll"));
        Assert.ThrowsExactly<IOException>(() => _installer.Commit(_transaction));
        Assert.AreEqual(PortableUpdateState.AwaitingHealth, _installer.Load(_transaction).State);
        Assert.AreEqual(PortableUpdateState.RolledBack, _installer.Rollback(_transaction).State);
    }

    [TestMethod]
    public void JournalPollingDoesNotBlockAtomicHealthCommit()
    {
        _installer.Prepare(_install, _package, _transaction);
        _installer.Apply(_transaction);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reading = new CountdownEvent(4);
        int snapshots = 0;
        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            bool first = true;
            while (!stop.IsCancellationRequested)
            {
                var snapshot = _installer.Load(_transaction);
                Assert.IsTrue(snapshot.State is PortableUpdateState.AwaitingHealth or PortableUpdateState.Committed,
                    "Concurrent readers must observe complete old or new journal snapshots.");
                Interlocked.Increment(ref snapshots);
                if (first) { first = false; reading.Signal(); }
                Thread.Yield();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            Assert.IsTrue(reading.Wait(TimeSpan.FromSeconds(5)), "Journal readers did not start.");
            Assert.AreEqual(PortableUpdateState.Committed, _installer.Commit(_transaction).State,
                "On Windows, active health-poll readers must permit the supervisor's atomic journal replacement.");
        }
        finally
        {
            stop.Cancel();
            Task.WaitAll(readers);
        }
        Assert.IsTrue(snapshots >= 4);
        Assert.AreEqual(PortableUpdateState.Committed, _installer.Load(_transaction).State);
    }

    private void AssertOldApplication() => Assert.AreEqual("old application", Read(_install, "KeeStashDock.exe"));
    private void AssertPreserved()
    {
        Assert.AreEqual("user settings", Read(_install, "Data/settings.json"));
        Assert.AreEqual("synthetic database", Read(_install, "Data/snippets.db"));
        Assert.AreEqual("custom notes", Read(_install, "my-notes.txt"));
        Assert.AreEqual("installed runtime", Read(_install, "OfflineTranslation/runtime/python/pythonw.exe"));
    }
    private void SaveJournal(PortableUpdateTransaction state)
        => File.WriteAllBytes(Path.Combine(_transaction, PortableUpdateInstaller.TransactionFileName), JsonSerializer.SerializeToUtf8Bytes(state, JournalJson));
    private static string Read(string root, string relative) => File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
    private static void WritePackage(string root, string version, Dictionary<string, string> files)
    {
        foreach (var file in files) Write(root, file.Key, file.Value);
        var descriptors = files.Keys.Select(path =>
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            return new UpdatePackageFile(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }).ToArray();
        var manifest = new UpdatePackageManifest(1, version, descriptors);
        File.WriteAllBytes(Path.Combine(root, UpdatePackageManifest.FileName), JsonSerializer.SerializeToUtf8Bytes(manifest, UpdatePackageManifest.JsonOptions));
    }
    private sealed class InlineProgress(Action<PortableUpdateProgress> report) : IProgress<PortableUpdateProgress>
    {
        public void Report(PortableUpdateProgress value) => report(value);
    }
}
