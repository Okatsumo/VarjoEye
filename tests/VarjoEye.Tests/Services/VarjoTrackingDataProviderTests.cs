using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Tests.Fakes;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class VarjoTrackingDataProviderTests
{
    [Fact]
    public async Task FailedConnect_RetriesUntilRuntimeIsAvailable()
    {
        var runtime = new FakeVarjoRuntime();
        runtime.EnqueueConnectResult(OperationResult.Failure("Varjo runtime is unavailable."));
        runtime.EnqueueConnectResult(OperationResult.Failure("Varjo runtime is unavailable."));
        using var provider = Create(runtime);

        provider.Start();
        await WaitUntil(() => runtime.ConnectCount >= 3 && runtime.InitializeCount >= 1);

        Assert.True(runtime.IsSessionOpen);
        Assert.True(provider.Current.HasTrackingData);
        await provider.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WaitsForPollThenDisconnectsIdleSession()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.InitializeCount >= 1);

        await provider.StopAsync();

        Assert.Equal(1, runtime.DisconnectCount);
        Assert.False(runtime.DisconnectedWhileBusy);
        Assert.False(runtime.IsSessionOpen);
        Assert.False(provider.Current.HasTrackingData);
        Assert.Equal(ConnectionStatus.Disconnected, provider.Current.Headset.Connection);
    }

    [Fact]
    public async Task HeadsetReconnect_ReinitializesGaze()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.InitializeCount >= 1);

        runtime.HeadsetConnected = false;
        await WaitUntil(() => !runtime.IsEyeTrackingInitialized);

        runtime.HeadsetConnected = true;
        await WaitUntil(() => runtime.InitializeCount >= 2 && runtime.IsEyeTrackingInitialized && provider.Current.HasTrackingData);

        Assert.True(provider.Current.HasTrackingData);
        await provider.StopAsync();
    }

    [Fact]
    public async Task HeadsetConnected_StartsCameraStreamAtTrackingFrequency()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.StartCameraCount >= 1 && runtime.IsEyeCameraStreaming);

        Assert.Equal(EyeTrackingFrequency.Hz200, runtime.LastCameraFrequency);
        await provider.StopAsync();
        Assert.True(runtime.StopCameraCount >= 1);
        Assert.False(runtime.IsEyeCameraStreaming);
    }

    [Fact]
    public async Task HeadsetDisconnect_StopsCameraStream()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.IsEyeCameraStreaming);

        runtime.HeadsetConnected = false;
        await WaitUntil(() => !runtime.IsEyeCameraStreaming);
        await provider.StopAsync();
    }

    [Fact]
    public async Task StatusReads_AreThrottledWhileGazeKeepsPolling()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        provider.Start();
        await WaitUntil(() => runtime.StatusReadCount >= 4);
        await provider.StopAsync();
        Assert.True(runtime.GazeReadCount > runtime.StatusReadCount * 2,
            $"gaze={runtime.GazeReadCount}, status={runtime.StatusReadCount}");
    }

    [Fact]
    public async Task SlowGazeSubscriber_DoesNotBlockNativePoll()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        provider.GazeReceived += (_, _) => { entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); };
        provider.Start();
        try
        {
            await WaitUntil(() => entered.IsSet);
            var before = runtime.GazeReadCount;
            await WaitUntil(() => runtime.GazeReadCount >= before + 8);
        }
        finally
        {
            release.Set();
            await provider.StopAsync();
        }
        Assert.False(runtime.DisconnectedWhileBusy);
    }

    [Fact]
    public async Task BatchGaze_DeliversEveryFrameInOrderAndPublishesLatestSnapshot()
    {
        var runtime = new FakeVarjoRuntime();
        var eye = new EyeTrackingEye(1, 4, 12, 0.3, 0.5, Vector3Value.Zero, new(0.2, 0.1, 1));
        var calls = 0;
        runtime.BatchFactory = () => Interlocked.Increment(ref calls) == 1
            ? new VarjoGazeBatchResult([new(10, eye, eye, 1), new(11, eye, eye, 2), new(12, eye, eye, 3)])
            : new VarjoGazeBatchResult([]);
        using var provider = Create(runtime);
        var delivered = new System.Collections.Concurrent.ConcurrentQueue<long>();
        provider.GazeReceived += (_, sample) => delivered.Enqueue(sample.FrameNumber);
        provider.Start();
        await WaitUntil(() => delivered.Count == 3 && provider.Current.HasTrackingData);
        Assert.Equal(new long[] { 10, 11, 12 }, delivered.ToArray());
        Assert.True(provider.Current.HasTrackingData);
        Assert.Equal(0.2, provider.Current.LeftEye.Varjo.GazeDirection.X);
        await provider.StopAsync();
        Assert.False(runtime.DisconnectedWhileBusy);
    }

    [Fact]
    public async Task GazeDelivery_ResumesOnSameDedicatedThreadAfterEmptyQueue()
    {
        var runtime = new FakeVarjoRuntime();
        var eye = new EyeTrackingEye(1, 4, 12, 0.3, 0.5, Vector3Value.Zero, new(0.2, 0.1, 1));
        var next = 0;
        var batchReads = 0;
        runtime.BatchFactory = () =>
        {
            Interlocked.Increment(ref batchReads);
            var number = Interlocked.Exchange(ref next, 0);
            return number != 0
                ? new VarjoGazeBatchResult([new(number, eye, eye, number)])
                : new VarjoGazeBatchResult([]);
        };
        using var provider = Create(runtime);
        var delivered = new System.Collections.Concurrent.ConcurrentQueue<(long Frame, int Thread, bool Pool)>();
        provider.GazeReceived += (_, sample) => delivered.Enqueue(
            (sample.FrameNumber, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread));
        provider.Start();
        Interlocked.Exchange(ref next, 10);
        await WaitUntil(() => delivered.Count == 1);
        var reads = Volatile.Read(ref batchReads);
        await WaitUntil(() => Volatile.Read(ref batchReads) >= reads + 8);
        Interlocked.Exchange(ref next, 11);
        await WaitUntil(() => delivered.Count == 2);
        await provider.StopAsync();
        var samples = delivered.ToArray();
        Assert.Equal(new long[] { 10, 11 }, samples.Select(s => s.Frame));
        Assert.All(samples, s => Assert.False(s.Pool));
        Assert.Equal(samples[0].Thread, samples[1].Thread);
        Assert.False(runtime.DisconnectedWhileBusy);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingGazeDeliveryAndJoinsActiveCallback()
    {
        var runtime = new FakeVarjoRuntime();
        var eye = new EyeTrackingEye(1, 4, 12, 0.3, 0.5, Vector3Value.Zero, new(0.2, 0.1, 1));
        var calls = 0;
        runtime.BatchFactory = () => Interlocked.Increment(ref calls) == 1
            ? new VarjoGazeBatchResult([new(10, eye, eye, 1), new(11, eye, eye, 2), new(12, eye, eye, 3)])
            : new VarjoGazeBatchResult([]);
        using var provider = Create(runtime);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var delivered = 0;
        provider.GazeReceived += (_, _) =>
        {
            Interlocked.Increment(ref delivered);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(3));
        };
        provider.Start();
        try
        {
            await WaitUntil(() => entered.IsSet);
            var stopping = provider.StopAsync();
            Assert.False(stopping.IsCompleted);
            release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, Volatile.Read(ref delivered));
            Assert.False(runtime.IsSessionOpen);
            Assert.False(runtime.DisconnectedWhileBusy);
        }
        finally { release.Set(); await provider.StopAsync(); }
    }

    [Fact]
    public async Task BlockedStateSubscriber_DoesNotBlockPollOrStop_AndReceivesLatestState()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = 0;
        var active = 0;
        var concurrent = false;
        bool? pool = null;
        provider.Updated += (_, state) =>
        {
            if (Interlocked.Increment(ref active) != 1) concurrent = true;
            try
            {
                pool = Thread.CurrentThread.IsThreadPoolThread;
                if (state.HasTrackingData && Interlocked.Exchange(ref blocked, 1) == 0)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                }
                if (entered.IsSet && !state.HasTrackingData && state.Headset.Connection == ConnectionStatus.Disconnected)
                    stopped.TrySetResult();
            }
            finally { Interlocked.Decrement(ref active); }
        };
        provider.Start();
        try
        {
            await WaitUntil(() => entered.IsSet);
            var before = runtime.GazeReadCount;
            await WaitUntil(() => runtime.GazeReadCount >= before + 10);
            await provider.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(provider.Current.HasTrackingData);
            release.Set();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(pool);
            Assert.False(concurrent);
            Assert.False(runtime.DisconnectedWhileBusy);
        }
        finally { release.Set(); await provider.StopAsync(); }
    }

    [Fact]
    public async Task ThrowingStateSubscriber_DoesNotStopPollingOrSkipNextSubscriber()
    {
        var runtime = new FakeVarjoRuntime();
        using var provider = Create(runtime);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Updated += (_, _) => throw new InvalidOperationException("State consumer failed");
        provider.Updated += (_, state) => { if (state.HasTrackingData) delivered.TrySetResult(); };
        provider.Start();
        try
        {
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before = runtime.GazeReadCount;
            await WaitUntil(() => runtime.GazeReadCount >= before + 10);
        }
        finally { await provider.StopAsync(); }
    }

    private static VarjoTrackingDataProvider Create(FakeVarjoRuntime runtime) =>
        new(
            runtime,
            new InMemoryAppSettingsService(),
            new InMemoryLogService(),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromSeconds(2));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        throw new TimeoutException("Condition was not met in time.");
    }
}
