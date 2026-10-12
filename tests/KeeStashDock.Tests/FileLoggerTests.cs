using KeeStashDock.Infrastructure.Logging;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class FileLoggerTests
{
    [TestMethod]
    public async Task ShutdownAllowsLateBackgroundMessagesWithoutThrowing()
    {
        string parent = Path.Combine(Path.GetTempPath(), "KeeStashDock.Tests", "Logger");
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var logger = new FileLogger(root);
            logger.Info("Before shutdown");
            logger.Dispose();
            // Housekeeping and cancelled operations can finish after application shutdown.
            await Task.Run(() =>
            {
                logger.Info("Late background completion");
                logger.Error("Late background failure", new IOException("Synthetic failure"));
            });
            StringAssert.Contains(File.ReadAllText(Path.Combine(root, "keestashdock.log")), "Before shutdown");
        }
        finally
        {
            Assert.AreEqual(Path.GetFullPath(parent), Path.GetDirectoryName(Path.GetFullPath(root)));
            Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(root), "N", out _));
            Directory.Delete(root, recursive: true);
        }
    }
}
