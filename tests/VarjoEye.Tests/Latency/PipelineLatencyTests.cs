using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Core.Services.Varjo;
using VarjoEye.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace VarjoEye.Tests.Latency;

// Timing probes must not compete with other test collections in the same process.
[CollectionDefinition("Latency", DisableParallelization = true)]
public sealed class LatencyCollection;

[Collection("Latency")]
[Trait("Category", "Latency")]
public sealed class PipelineLatencyTests(ITestOutputHelper output)
{
    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    [Fact]
    public async Task Varjo_SdkAvailability_PollAndDelivery_AreMeasuredSeparately()
    {
        const int count = 180;
        var runtime = new FakeVarjoRuntime();
        var stamps = new ConcurrentDictionary<long, (long Available, long Read)>();
        var poll = new ConcurrentQueue<double>();
        var delivery = new ConcurrentQueue<double>();
        var total = new ConcurrentQueue<double>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eye = new EyeTrackingEye(1, 4, 12, 0.3, 0.5, Vector3Value.Zero, new(0, 0, 1));
        var period = Stopwatch.Frequency / 200;
        long next = 0;
        var frame = 0;
        runtime.BatchFactory = () =>
        {
            var now = Stopwatch.GetTimestamp();
            if (next == 0) next = now + period;
            var samples = new List<GazeSample>();
            while (frame < count && now >= next)
            {
                var number = ++frame;
                stamps[number] = (next, now);
                samples.Add(new(number, eye, eye));
                next += period;
            }
            return new(samples);
        };
        using var provider = new VarjoTrackingDataProvider(runtime, new InMemoryAppSettingsService(),
            new InMemoryLogService(), TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(2));
        provider.GazeReceived += (_, sample) =>
        {
            var now = Stopwatch.GetTimestamp();
            var stamp = stamps[sample.FrameNumber];
            if (sample.FrameNumber > 30)
            {
                poll.Enqueue(Milliseconds(stamp.Read - stamp.Available));
                delivery.Enqueue(Milliseconds(now - stamp.Read));
                total.Enqueue(Milliseconds(now - stamp.Available));
            }
            if (sample.FrameNumber == count) completed.TrySetResult();
        };
        provider.Start();
        try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await provider.StopAsync(); }
        Assert.Equal(count - 30, total.Count);
        Report("Varjo.AvailableToSdkRead", poll);
        Report("Varjo.SdkReadToSubscriber", delivery);
        Report("Varjo.AvailableToSubscriber", total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Baballonia_CameraToSubscriber_WithConsumerLoad(int consumerDelayMs)
    {
        const int count = 240;
        var frames = new FakeTrackingDataProvider();
        using var service = new BaballoniaTrackingService(frames, new InMemoryAppSettingsService(),
            new InMemoryLogService(), [InferenceDevice.Cpu], TimeSpan.FromSeconds(2), null, TestModelPaths.Create());
        var stamps = new ConcurrentDictionary<long, long>();
        var age = new ConcurrentQueue<double>();
        var handler = new ConcurrentQueue<double>();
        var numbers = new ConcurrentQueue<long>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        service.Updated += (_, sample) =>
        {
            if (sample.Result is not { } result) return;
            var entered = Stopwatch.GetTimestamp();
            if (consumerDelayMs > 0)
            {
                using var timer = new HighResolutionPollTimer(TimeSpan.FromMilliseconds(consumerDelayMs), cancel.Token);
                timer.WaitForNextTick();
            }
            if (result.FrameNumber > 40)
            {
                age.Enqueue(Milliseconds(entered - stamps[result.FrameNumber]));
                handler.Enqueue(Milliseconds(Stopwatch.GetTimestamp() - entered));
                numbers.Enqueue(result.FrameNumber);
            }
            if (result.FrameNumber == count) completed.TrySetResult();
        };
        service.Start();
        var feed = Task.Factory.StartNew(() =>
        {
            var pixels = new byte[64];
            using var timer = new HighResolutionPollTimer(TimeSpan.FromMilliseconds(5), cancel.Token);
            for (var i = 1; i <= count && timer.WaitForNextTick(); i++)
            {
                var stamp = Stopwatch.GetTimestamp();
                stamps[i] = stamp;
                var ns = (long)(stamp * (1_000_000_000d / Stopwatch.Frequency));
                frames.PublishCamera(new(true, 8, 8, pixels, i, ns));
                frames.PublishCamera(new(false, 8, 8, pixels, i, ns));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var sequence = numbers.ToArray();
            Assert.NotEmpty(sequence);
            Assert.True(sequence.Zip(sequence.Skip(1), (a, b) => b > a).All(x => x));
            Report($"Baballonia.CameraToSubscriber.Consumer{consumerDelayMs}ms", age);
            Report($"Baballonia.ConsumerDuration.Requested{consumerDelayMs}ms", handler);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                ConsumerDelayMs = consumerDelayMs, Delivered = sequence.Length,
                UndeliveredFrameNumbers = sequence[^1] - sequence[0] + 1 - sequence.Length,
                SupersededNotifications = service.DroppedUpdateCount
            }));
        }
        finally
        {
            cancel.Cancel();
            await feed;
            await service.StopAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Baballonia_RunDuration_WithAndWithoutPreview(bool preview)
    {
        using var inference = new BaballoniaEyeInference(TestModelPaths.Create().StockPath, InferenceDevice.Cpu);
        var window = new List<CameraEyeFramePair>();
        var durations = new List<double>();
        var pixels = new byte[64];
        for (var i = 1; i <= 90; i++)
        {
            window.Add(new(i, DateTimeOffset.UtcNow,
                new(true, 8, 8, pixels, i, i * 5_000_000L), new(false, 8, 8, pixels, i, i * 5_000_000L)));
            if (window.Count > 4) window.RemoveAt(0);
            var start = Stopwatch.GetTimestamp();
            var result = inference.Run(window, smoothingEnabled: false, generatePreview: preview, captureFeatures: false);
            if (i <= 30) continue;
            Assert.NotNull(result);
            durations.Add(Milliseconds(Stopwatch.GetTimestamp() - start));
        }
        Assert.Equal(60, durations.Count);
        Report($"Baballonia.Run.Preview{preview}", durations);
    }

    [Fact]
    public void PollTimer_OneMillisecond_IntervalDistribution()
    {
        using var timer = new HighResolutionPollTimer(TimeSpan.FromMilliseconds(1), CancellationToken.None);
        var intervals = new List<double>();
        var previous = Stopwatch.GetTimestamp();
        for (var i = 0; i < 230; i++)
        {
            Assert.True(timer.WaitForNextTick());
            var now = Stopwatch.GetTimestamp();
            if (i >= 30) intervals.Add(Milliseconds(now - previous));
            previous = now;
        }
        Assert.Equal(200, intervals.Count);
        Report("PollTimer.1ms.ActualInterval", intervals);
    }

    private void Report(string stage, IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        Assert.NotEmpty(sorted);
        Assert.All(sorted, value => Assert.True(double.IsFinite(value) && value >= 0));
        double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Stage = stage, Samples = sorted.Length, MeanMs = sorted.Average(),
            P50Ms = Percentile(.5), P95Ms = Percentile(.95), P99Ms = Percentile(.99), MaxMs = sorted[^1]
        }));
    }
}
