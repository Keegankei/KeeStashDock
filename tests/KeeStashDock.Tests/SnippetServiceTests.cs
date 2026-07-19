using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class SnippetServiceTests
{
    private static readonly DateTime ReferenceTime =
        new(2026, 7, 19, 12, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    public void ComputeExpireAt_ReturnsNullForNever()
    {
        Assert.IsNull(SnippetService.ComputeExpireAt(ExpirePolicy.Never, ReferenceTime));
    }

    [TestMethod]
    public void ComputeExpireAt_ReturnsNullForBurnAfterRead()
    {
        Assert.IsNull(SnippetService.ComputeExpireAt(ExpirePolicy.BurnAfterRead, ReferenceTime));
    }

    [TestMethod]
    public void ComputeExpireAt_AddsOneDay()
    {
        Assert.AreEqual(
            ReferenceTime.AddDays(1),
            SnippetService.ComputeExpireAt(ExpirePolicy.OneDay, ReferenceTime));
    }

    [TestMethod]
    public void ComputeExpireAt_AddsSevenDays()
    {
        Assert.AreEqual(
            ReferenceTime.AddDays(7),
            SnippetService.ComputeExpireAt(ExpirePolicy.SevenDays, ReferenceTime));
    }
}
