using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using KeeStashDock.Infrastructure.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class DesktopUpdateCoordinatorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "UpdateCoordinator", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "UpdateCoordinator")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(_root);
        Assert.IsTrue(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    [TestMethod]
    public void HelperArgumentsKeepPathsLiteralWithoutShellEvaluation()
    {
        string executable = Path.Combine(_root, "folder with spaces", "KeeStashDock.exe");
        string transaction = Path.Combine(_root, "transaction with spaces & symbols");
        var start = DesktopUpdateCoordinator.CreateHelperStartInfo(executable,
            DesktopUpdateCoordinator.ApplyArgument, transaction);
        Assert.IsFalse(start.UseShellExecute);
        Assert.IsTrue(start.CreateNoWindow);
        Assert.AreEqual(ProcessWindowStyle.Hidden, start.WindowStyle);
        CollectionAssert.AreEqual(new[] { "--apply-update", transaction }, start.ArgumentList.ToArray());
        Assert.AreEqual(Path.GetDirectoryName(executable), start.WorkingDirectory);
    }

    [TestMethod]
    public void ProcessIdentityRequiresMatchingStartTimeNotJustPid()
    {
        using var current = Process.GetCurrentProcess();
        long started = current.StartTime.ToUniversalTime().Ticks;
        Assert.IsTrue(DesktopUpdateCoordinator.IsExactProcessRunning(new(current.Id, started)));
        Assert.IsFalse(DesktopUpdateCoordinator.IsExactProcessRunning(new(current.Id, started - 1)));
        Assert.IsFalse(DesktopUpdateCoordinator.IsExactProcessRunning(new(0, started)));
    }

    [TestMethod]
    public async Task ParentWaitRejectsSelfAndInvalidIdentity()
    {
        using var current = Process.GetCurrentProcess();
        Assert.IsFalse(await DesktopUpdateCoordinator.WaitForParentAsync(current.Id,
            current.StartTime.ToUniversalTime().Ticks, TimeSpan.FromMilliseconds(10)));
        Assert.IsFalse(await DesktopUpdateCoordinator.WaitForParentAsync(0, 0, TimeSpan.FromMilliseconds(10)));
    }

    [TestMethod]
    public void HealthAcknowledgementRequiresNonceVersionAndExactChildIdentity()
    {
        string token = new('a', 64);
        var identity = new DesktopUpdateCoordinator.UpdateProcessIdentity(1234, 987654321);
        var health = new DesktopUpdateCoordinator.Healthy(token, identity.ProcessId, identity.StartedTicks, "1.2.3");
        Assert.IsTrue(DesktopUpdateCoordinator.HealthMatches(health, token, identity, "1.2.3"));
        Assert.IsFalse(DesktopUpdateCoordinator.HealthMatches(health with { Token = new string('b', 64) }, token, identity, "1.2.3"));
        Assert.IsFalse(DesktopUpdateCoordinator.HealthMatches(health with { ProcessId = 1235 }, token, identity, "1.2.3"));
        Assert.IsFalse(DesktopUpdateCoordinator.HealthMatches(health with { StartedTicks = 987654322 }, token, identity, "1.2.3"));
        Assert.IsFalse(DesktopUpdateCoordinator.HealthMatches(health with { Version = "1.2.2" }, token, identity, "1.2.3"));
        Assert.IsFalse(DesktopUpdateCoordinator.HealthMatches(health with { Token = "weak" }, "weak", identity, "1.2.3"));
    }

    [TestMethod]
    public void PendingPointerIsStableForSamePathAndDifferentForAnotherInstallation()
    {
        string first = DesktopUpdateCoordinator.PendingFile(_root);
        Assert.AreEqual(first, DesktopUpdateCoordinator.PendingFile(_root + Path.DirectorySeparatorChar));
        Assert.AreEqual(first, DesktopUpdateCoordinator.PendingFile(_root.ToUpperInvariant()));
        Assert.AreNotEqual(first, DesktopUpdateCoordinator.PendingFile(Path.Combine(_root, "other")));
    }

    [TestMethod]
    public void MetadataReadsRejectOversizedPayloadAndDeeplyNestedJson()
    {
        string path = Path.Combine(_root, "state.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new string('x', 65536)));
        Assert.ThrowsExactly<InvalidDataException>(() => DesktopUpdateCoordinator.ReadJson<string>(path));
        File.WriteAllText(path, new string('[', 30) + "0" + new string(']', 30));
        Assert.ThrowsExactly<JsonException>(() => DesktopUpdateCoordinator.ReadJson<JsonElement>(path));
    }

    [TestMethod]
    public async Task NormalStartupArgumentsCannotClaimAnUpdateCompletion()
    {
        Assert.IsFalse(await DesktopUpdateCoordinator.WaitForCompletionAsync([], CancellationToken.None));
        Assert.IsFalse(await DesktopUpdateCoordinator.WaitForCompletionAsync(["--unknown", _root], CancellationToken.None));
    }

    [TestMethod]
    public void RecoveryLeasePreventsLateChildStartupAcrossRollback()
    {
        var (installer, install, transaction) = AppliedFixture();
        using var current = Process.GetCurrentProcess();
        var identity = new DesktopUpdateCoordinator.UpdateProcessIdentity(current.Id, current.StartTime.ToUniversalTime().Ticks);
        using (DesktopUpdateCoordinator.AcquireStartupLease(transaction))
        {
            Assert.IsFalse(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, install, "1.2.3", identity),
                "A child must not initialize while recovery holds the startup lease.");
            installer.Rollback(transaction);
        }
        Assert.IsFalse(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, install, "1.2.3", identity),
            "A delayed child must reject the rolled-back state after the recovery lease is released.");
        Assert.IsFalse(File.Exists(Path.Combine(transaction, "replacement.json")));
    }

    [TestMethod]
    public async Task ChildClaimsExactIdentityBeforeUiAndRecoveryRefusesLiveOwnedProcess()
    {
        var (_, install, transaction) = AppliedFixture();
        using var current = Process.GetCurrentProcess();
        var identity = new DesktopUpdateCoordinator.UpdateProcessIdentity(current.Id, current.StartTime.ToUniversalTime().Ticks);
        Assert.IsTrue(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, install, "1.2.3", identity));
        Assert.AreEqual(identity, DesktopUpdateCoordinator.ReadJson<DesktopUpdateCoordinator.UpdateProcessIdentity>(
            Path.Combine(transaction, "replacement.json")));
        await Assert.ThrowsExactlyAsync<IOException>(() => DesktopUpdateCoordinator.WaitForOwnedProcessAsync(transaction, "replacement.json"));
        Assert.IsFalse(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, install, "1.2.2", identity));
        Assert.IsFalse(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, Path.Combine(_root, "other"), "1.2.3", identity));
    }

    [TestMethod]
    public void IdentityPollingAllowsChildToRefreshItsDurableStartupClaim()
    {
        var (_, install, transaction) = AppliedFixture();
        using var current = Process.GetCurrentProcess();
        var identity = new DesktopUpdateCoordinator.UpdateProcessIdentity(current.Id, current.StartTime.ToUniversalTime().Ticks);
        Assert.IsTrue(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, install, "1.2.3", identity));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reading = new CountdownEvent(4);
        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            bool first = true;
            while (!stop.IsCancellationRequested)
            {
                Assert.AreEqual(identity, DesktopUpdateCoordinator.ReadJson<DesktopUpdateCoordinator.UpdateProcessIdentity>(
                    Path.Combine(transaction, "replacement.json")));
                if (first) { first = false; reading.Signal(); }
                Thread.Yield();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            Assert.IsTrue(reading.Wait(TimeSpan.FromSeconds(5)), "Identity readers did not start.");
            for (int attempt = 0; attempt < 12; attempt++)
                Assert.IsTrue(DesktopUpdateCoordinator.ClaimHealthIdentity(transaction, install, "1.2.3", identity),
                    "Concurrent recovery checks must not prevent a child's atomic identity refresh.");
        }
        finally
        {
            stop.Cancel();
            Task.WaitAll(readers);
        }
    }

    [TestMethod]
    public void SnapshotCopiesUserSettingsAndDraftsWithoutRecopyingDatabaseLogsOrBackups()
    {
        string data = Path.Combine(_root, "Data");
        string snapshot = Path.Combine(data, "Backups", "before-update");
        Write(data, "settings.json", "synthetic settings");
        Write(data, "draft.txt", "synthetic draft");
        Write(data, "nested/asset.bin", "synthetic asset");
        Write(data, "keestashdock.db", "live database is not copied");
        Write(data, "keestashdock.db-wal", "write-ahead log");
        Write(data, "keestashdock.db-shm", "shared memory");
        Write(data, "Logs/app.log", "log excluded");
        Write(data, "Backups/old/keestashdock.db", "older backup excluded");
        Write(snapshot, "keestashdock.db", "consistent SQLite snapshot created by BackupService");
        UpdateDataSnapshot.CopyNonDatabaseFiles(data, snapshot, "keestashdock.db");
        Assert.AreEqual("synthetic settings", File.ReadAllText(Path.Combine(snapshot, "settings.json")));
        Assert.AreEqual("synthetic draft", File.ReadAllText(Path.Combine(snapshot, "draft.txt")));
        Assert.AreEqual("synthetic asset", File.ReadAllText(Path.Combine(snapshot, "nested", "asset.bin")));
        Assert.AreEqual("consistent SQLite snapshot created by BackupService", File.ReadAllText(Path.Combine(snapshot, "keestashdock.db")));
        Assert.IsFalse(File.Exists(Path.Combine(snapshot, "keestashdock.db-wal")));
        Assert.IsFalse(Directory.Exists(Path.Combine(snapshot, "Logs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(snapshot, "Backups")));
    }

    [TestMethod]
    public void SnapshotRejectsLinkedUserFilesInsteadOfFollowingThem()
    {
        string data = Path.Combine(_root, "Data"), snapshot = Path.Combine(_root, "snapshot");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(snapshot);
        string outside = Path.Combine(_root, "external.txt"), link = Path.Combine(data, "linked.txt");
        File.WriteAllText(outside, "must not be copied through a link");
        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (UnauthorizedAccessException) { Assert.Inconclusive("Creating symbolic links is unavailable on this Windows account."); }
            catch (IOException) { Assert.Inconclusive("This filesystem does not permit creating the symbolic-link fixture."); }
            Assert.ThrowsExactly<IOException>(() => UpdateDataSnapshot.CopyNonDatabaseFiles(data, snapshot, "keestashdock.db"));
            Assert.IsFalse(File.Exists(Path.Combine(snapshot, "linked.txt")));
        }
        finally { if (File.Exists(link)) File.Delete(link); }
    }

    private static void Write(string root, string relative, string contents)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private (PortableUpdateInstaller Installer, string Install, string Transaction) AppliedFixture()
    {
        string install = Path.Combine(_root, "install"), package = Path.Combine(_root, "package"),
            transaction = Path.Combine(_root, "transaction");
        void Package(string directory, string version, string content)
        {
            Write(directory, "KeeStashDock.exe", content);
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "KeeStashDock.exe"));
            var manifest = new UpdatePackageManifest(1, version,
                [new("KeeStashDock.exe", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())]);
            File.WriteAllBytes(Path.Combine(directory, UpdatePackageManifest.FileName),
                JsonSerializer.SerializeToUtf8Bytes(manifest, UpdatePackageManifest.JsonOptions));
        }
        Package(install, "1.2.2", "synthetic old application bytes; never executed");
        Package(package, "1.2.3", "synthetic new application bytes; never executed");
        var installer = new PortableUpdateInstaller();
        installer.Prepare(install, package, transaction);
        installer.Apply(transaction);
        return (installer, install, transaction);
    }
}
