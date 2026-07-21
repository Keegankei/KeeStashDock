using KeeStashDock.Core.Services;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class DirectPasteTests
{
    private const uint CurrentProcessId = 42;
    private static readonly nint TargetWindow = (nint)100;
    private static readonly DateTimeOffset ProcessStart =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ValidExternalTarget_SendsExactlyOneCtrlVChord()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.Success, result);
        CollectionAssert.AreEqual(
            new[] { "Down:Control", "Down:V", "Up:V", "Up:Control" },
            test.Input.Events);
        Assert.AreEqual(1, test.Windows.SetForegroundCalls);
        Assert.AreEqual(0, test.Input.Pressed.Count);
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("same-process")]
    [DataRow("pid-unavailable")]
    public async Task InvalidRememberObservation_ClearsPreviousTarget(string scenario)
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Windows.ForegroundWindow = (nint)101;
        switch (scenario)
        {
            case "zero":
                test.Windows.ForegroundWindow = nint.Zero;
                break;
            case "same-process":
                test.Windows.ProcessId = CurrentProcessId;
                break;
            case "pid-unavailable":
                test.Windows.CanReadProcessId = false;
                break;
            default:
                Assert.Fail($"Unknown scenario: {scenario}");
                break;
        }

        test.Coordinator.RememberForeground();
        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.NoTarget, result);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task ClosedTarget_FailsWithoutInputAndClearsTarget()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Windows.WindowExists = false;

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.InvalidTarget, result);
        Assert.AreEqual(DirectPasteResult.NoTarget, await test.Coordinator.PasteAsync());
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task ReusedWindowHandleWithDifferentPid_FailsWithoutInput()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Windows.ProcessId++;

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.TargetProcessChanged, result);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task ReusedProcessIdWithDifferentStartTime_FailsWithoutInput()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Windows.ProcessStartedAtUtc = ProcessStart.AddMinutes(1);

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.TargetProcessChanged, result);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task SetForegroundDenied_FailsWithoutInput()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Windows.SetForegroundResult = false;

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.FocusDenied, result);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task UserSwitchesWindowDuringDelay_FailsWithoutInput()
    {
        TestContext test = CreateContext(gatedDelay: true);
        test.Coordinator.RememberForeground();

        Task<DirectPasteResult> paste = test.Coordinator.PasteAsync();
        await test.Clock.DelayStarted.Task;
        test.Windows.ForegroundWindow = (nint)999;
        test.Clock.ReleaseDelay();

        Assert.AreEqual(DirectPasteResult.FocusLost, await paste);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task WindowPidChangesDuringDelay_FailsWithoutInput()
    {
        TestContext test = CreateContext(gatedDelay: true);
        test.Coordinator.RememberForeground();

        Task<DirectPasteResult> paste = test.Coordinator.PasteAsync();
        await test.Clock.DelayStarted.Task;
        test.Windows.ProcessId++;
        test.Clock.ReleaseDelay();

        Assert.AreEqual(DirectPasteResult.TargetProcessChanged, await paste);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task RapidPaste_OnlyOneOperationCanSendInput()
    {
        TestContext test = CreateContext(gatedDelay: true);
        test.Coordinator.RememberForeground();

        Task<DirectPasteResult> first = test.Coordinator.PasteAsync();
        await test.Clock.DelayStarted.Task;
        DirectPasteResult second = await test.Coordinator.PasteAsync();
        test.Clock.ReleaseDelay();

        Assert.AreEqual(DirectPasteResult.Busy, second);
        Assert.AreEqual(DirectPasteResult.Success, await first);
        Assert.AreEqual(1, test.Input.Events.Count(value => value == "Down:V"));
    }

    [TestMethod]
    public async Task CancellationDuringFocusDelay_SendsNoInputAndClearsTarget()
    {
        TestContext test = CreateContext(gatedDelay: true);
        test.Coordinator.RememberForeground();
        using var cancellation = new CancellationTokenSource();

        Task<DirectPasteResult> paste = test.Coordinator.PasteAsync(cancellation.Token);
        await test.Clock.DelayStarted.Task;
        cancellation.Cancel();

        Assert.AreEqual(DirectPasteResult.Cancelled, await paste);
        Assert.AreEqual(DirectPasteResult.NoTarget, await test.Coordinator.PasteAsync());
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    [TestMethod]
    public async Task InputFailure_ReleasesAllKeysThatMayBeDown()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Input.Pressed.Add(DirectPasteKey.Alt);
        test.Input.ThrowAfterKeyDown = DirectPasteKey.V;

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.InputFailed, result);
        Assert.AreEqual(0, test.Input.Pressed.Count);
        CollectionAssert.Contains(test.Input.Events, "Up:Alt");
        CollectionAssert.Contains(test.Input.Events, "Up:V");
        CollectionAssert.Contains(test.Input.Events, "Up:Control");
    }

    [TestMethod]
    public async Task ExpiredTarget_FailsWithoutInput()
    {
        TestContext test = CreateContext();
        test.Coordinator.RememberForeground();
        test.Clock.UtcNow += DirectPasteCoordinator.DefaultTargetLifetime +
                             TimeSpan.FromSeconds(1);

        DirectPasteResult result = await test.Coordinator.PasteAsync();

        Assert.AreEqual(DirectPasteResult.TargetExpired, result);
        Assert.AreEqual(0, test.Input.Events.Count);
    }

    private static TestContext CreateContext(bool gatedDelay = false)
    {
        var windows = new FakeForegroundWindowService
        {
            ForegroundWindow = TargetWindow,
            ProcessId = 1000,
            ProcessStartedAtUtc = ProcessStart,
        };
        var input = new FakeInputSender();
        var clock = new FakeClock { GateDelay = gatedDelay };
        var coordinator = new DirectPasteCoordinator(
            windows,
            input,
            clock,
            CurrentProcessId);
        return new TestContext(coordinator, windows, input, clock);
    }

    private sealed record TestContext(
        DirectPasteCoordinator Coordinator,
        FakeForegroundWindowService Windows,
        FakeInputSender Input,
        FakeClock Clock);

    private sealed class FakeForegroundWindowService : IForegroundWindowService
    {
        public nint ForegroundWindow { get; set; }
        public bool WindowExists { get; set; } = true;
        public bool CanReadProcessId { get; set; } = true;
        public uint ProcessId { get; set; }
        public DateTimeOffset ProcessStartedAtUtc { get; set; }
        public bool CanReadProcessStart { get; set; } = true;
        public bool SetForegroundResult { get; set; } = true;
        public int SetForegroundCalls { get; private set; }

        public nint GetForegroundWindow() => ForegroundWindow;

        public bool IsWindow(nint windowHandle)
            => WindowExists && windowHandle != nint.Zero;

        public bool TryGetWindowProcessId(nint windowHandle, out uint processId)
        {
            processId = CanReadProcessId ? ProcessId : 0;
            return CanReadProcessId;
        }

        public bool TryGetProcessStartTimeUtc(
            uint processId,
            out DateTimeOffset startedAtUtc)
        {
            startedAtUtc = ProcessStartedAtUtc;
            return CanReadProcessStart;
        }

        public bool SetForegroundWindow(nint windowHandle)
        {
            SetForegroundCalls++;
            if (SetForegroundResult)
                ForegroundWindow = windowHandle;
            return SetForegroundResult;
        }
    }

    private sealed class FakeInputSender : IInputSender
    {
        public HashSet<DirectPasteKey> Pressed { get; } = new();
        public List<string> Events { get; } = new();
        public DirectPasteKey? ThrowAfterKeyDown { get; set; }

        public bool IsKeyDown(DirectPasteKey key) => Pressed.Contains(key);

        public void KeyDown(DirectPasteKey key)
        {
            Pressed.Add(key);
            Events.Add($"Down:{key}");
            if (ThrowAfterKeyDown == key)
            {
                ThrowAfterKeyDown = null;
                throw new InvalidOperationException("Injected input failure.");
            }
        }

        public void KeyUp(DirectPasteKey key)
        {
            Pressed.Remove(key);
            Events.Add($"Up:{key}");
        }
    }

    private sealed class FakeClock : IDirectPasteClock
    {
        private readonly TaskCompletionSource<bool> _releaseDelay =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
        public bool GateDelay { get; set; }
        public TaskCompletionSource<bool> DelayStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayStarted.TrySetResult(true);
            return GateDelay
                ? _releaseDelay.Task.WaitAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public void ReleaseDelay() => _releaseDelay.TrySetResult(true);
    }
}
