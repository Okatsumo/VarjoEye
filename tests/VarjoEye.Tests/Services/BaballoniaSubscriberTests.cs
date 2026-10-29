using System.Diagnostics;
using System.Text.Json;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Core.Services.Varjo;
using VarjoEye.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaSubscriberTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BlockedUpdatedHandler_DoesNotStopInference_AndCoalescesPendingSnapshots()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var downstream = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long blockedFrame = 0;
        var delivered = new List<long>();
        var deliveredGate = new object();
        service.Updated += (_, sample) =>
        {
            if (!sample.HasResult || Interlocked.CompareExchange(ref blockedFrame, sample.Result!.FrameNumber, 0) != 0) return;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };
        service.Updated += (_, sample) =>
        {
            if (!sample.HasResult) return;
            lock (deliveredGate) delivered.Add(sample.Result!.FrameNumber);
            downstream.TrySetResult();
        };
        service.Start();
        try
        {
            for (var i = 1; i <= 4; i++) Publish(frames, i, i * 5_000_000L);
            await Until(() => entered.IsSet);
            var dropsBefore = service.DroppedUpdateCount;
            // Wait for every inference while the first callback remains held.
            for (var i = 5; i <= 12; i++)
            {
                Publish(frames, i, i * 5_000_000L);
                await Until(() => service.Current.Result?.FrameNumber == i);
            }
            Assert.Equal(12, service.Current.Result!.FrameNumber);
            Assert.Equal(7, service.DroppedUpdateCount - dropsBefore);
            Assert.False(downstream.Task.IsCompleted);
            release.Set();
            await downstream.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => { lock (deliveredGate) return delivered.Count == 2; });
            lock (deliveredGate) Assert.Equal(new long[] { blockedFrame, 12 }, delivered);
            output.WriteLine($"Callback held at {blockedFrame}; inference reached 12; seven pending snapshots superseded; delivery order [{blockedFrame}, 12].");
        }
        finally { release.Set(); await service.StopAsync(); }
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotDisableModelOrSkipOtherSubscribers()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        long delivered = 0;
        service.Updated += (_, sample) => { if (sample.HasResult) throw new InvalidOperationException("Consumer failure"); };
        service.Updated += (_, sample) => { if (sample.HasResult) Interlocked.Exchange(ref delivered, sample.Result!.FrameNumber); };
        service.Start();
        for (var i = 1; i <= 4; i++) Publish(frames, i, i * 5_000_000L);
        await Until(() => Interlocked.Read(ref delivered) == 4);
        Publish(frames, 5, 25_000_000);
        await Until(() => Interlocked.Read(ref delivered) == 5);
        Assert.Equal(BaballoniaRuntimeStatus.Tracking, service.Current.Status);
        await service.StopAsync();
    }

    [Fact]
    public async Task StopAndRestart_WithBlockedSubscriber_DeliversLatestSnapshot()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var latest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Updated += (_, sample) =>
        {
            if (sample.Result?.FrameNumber == 4) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
            if (sample.Result?.FrameNumber == 8) latest.TrySetResult();
        };
        service.Start();
        try
        {
            for (var i = 1; i <= 4; i++) Publish(frames, i, i * 5_000_000L);
            await Until(() => entered.IsSet);
            await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(service.Current.HasResult);
            service.Start();
            for (var i = 5; i <= 8; i++) Publish(frames, i, i * 5_000_000L);
            await Until(() => service.Current.Result?.FrameNumber == 8);
            release.Set();
            await latest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); await service.StopAsync(); }
    }

    [Fact]
    public async Task UpdatedHandlerCost_ProbeThroughputAndDeliveryAge()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        using var stopFeed = new CancellationTokenSource();
        var firstResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        var observations = new List<Observation>();
        var phase = 0;
        var nextPhase = 0;
        var delayMs = 0;
        var fed = 0;
        service.Updated += (_, sample) =>
        {
            if (!sample.HasResult || sample.Result is not { } result) return;
            firstResult.TrySetResult();
            int currentPhase, delay;
            lock (gate) { currentPhase = phase; delay = delayMs; }
            var started = Stopwatch.GetTimestamp();
            var before = (DateTimeOffset.UtcNow - result.ReceivedAt).TotalMilliseconds;
            if (delay > 0)
            {
                // Thread.Sleep(2) can take ~15 ms; measure a high-resolution delay instead.
                using var delayTimer = new HighResolutionPollTimer(TimeSpan.FromMilliseconds(delay), CancellationToken.None);
                delayTimer.WaitForNextTick();
            }
            var cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            lock (gate)
            {
                if (phase == currentPhase && phase != 0)
                    observations.Add(new(result.FrameNumber, before, before + cost, cost));
            }
        };
        service.Start();
        var producer = Task.Factory.StartNew(() =>
        {
            using var timer = new HighResolutionPollTimer(TimeSpan.FromMilliseconds(5), stopFeed.Token);
            while (timer.WaitForNextTick())
            {
                var number = Interlocked.Increment(ref fed);
                Publish(frames, number, (long)(Stopwatch.GetTimestamp() * (1_000_000_000d / Stopwatch.Frequency)));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await firstResult.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(300);
            foreach (var delay in new[] { 0, 2, 10, 0 })
            {
                long started;
                int inputBefore;
                lock (gate)
                {
                    observations.Clear();
                    delayMs = delay;
                    phase = ++nextPhase;
                    inputBefore = Volatile.Read(ref fed);
                    started = Stopwatch.GetTimestamp();
                }
                await Task.Delay(TimeSpan.FromSeconds(2));
                Observation[] samples;
                double seconds;
                int inputCount;
                lock (gate)
                {
                    seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
                    inputCount = Volatile.Read(ref fed) - inputBefore;
                    samples = observations.ToArray();
                    phase = 0;
                }
                Assert.NotEmpty(samples);
                var cost = samples.Select(s => s.HandlerMs).Order().ToArray();
                var skipped = samples[^1].Frame - samples[0].Frame + 1 - samples.Length;
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    RequestedHandlerMs = delay, CameraFps = inputCount / seconds,
                    ResultsFps = samples.Length / seconds, SkippedFrameNumbers = skipped,
                    BeforeHandlerMeanMs = samples.Average(s => s.BeforeMs),
                    AfterHandlerMeanMs = samples.Average(s => s.AfterMs),
                    HandlerMeanMs = cost.Average(), HandlerP95Ms = cost[(int)((cost.Length - 1) * 0.95)]
                }));
                if (delay == 10) Assert.True(cost.Average() >= 8);
            }
        }
        finally
        {
            stopFeed.Cancel();
            await producer;
            await service.StopAsync();
        }
    }

    private static BaballoniaTrackingService Create(FakeTrackingDataProvider frames) => new(
        frames, new InMemoryAppSettingsService(), new InMemoryLogService(),
        [InferenceDevice.Cpu], TimeSpan.FromSeconds(2), null, TestModelPaths.Create());

    private static readonly byte[] Pixels = new byte[64];
    private static void Publish(FakeTrackingDataProvider frames, int number, long timestamp)
    {
        frames.PublishCamera(new(true, 8, 8, Pixels, number, timestamp));
        frames.PublishCamera(new(false, 8, 8, Pixels, number, timestamp));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private readonly record struct Observation(long Frame, double BeforeMs, double AfterMs, double HandlerMs);
}
