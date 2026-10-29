using VarjoEye.Core.Services;
using Xunit;
using VarjoEye.Tests.Fakes;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests.Services;

public sealed class TrackingDiagnosticsTests
{
    [Fact]
    public async Task Service_ReportsEveryFiveSecondsAndStopsOnDispose()
    {
        var tracking = new FakeTrackingDataProvider();
        var babble = new FakeBaballoniaTrackingService();
        var logs = new InMemoryLogService();
        using var releaseLog = new ManualResetEventSlim();
        var logEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logs.Changed += (_, _) =>
        {
            if (logs.Entries.Count == 1)
            {
                logEntered.TrySetResult();
                releaseLog.Wait(TimeSpan.FromSeconds(5));
            }
            if (logs.Entries.Any(entry => entry.Message.StartsWith("[FPS]"))) ready.TrySetResult();
        };
        using var monitor = new TrackingDiagnosticsService(tracking, babble, logs, new InMemoryAppSettingsService());
        monitor.Start();
        monitor.Start();
        tracking.PublishCamera(new EyeCameraFrame(true, 1, 1, [0], 1, 900_000_000));
        tracking.PublishCamera(new EyeCameraFrame(false, 1, 1, [0], 1, 900_000_000));
        Assert.Empty(logs.Entries);
        try
        {
            await logEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Logging is blocked, but acquisition must still update counters independently.
            await Task.Run(() => tracking.PublishCamera(new EyeCameraFrame(true, 1, 1, [0], 2)))
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally { releaseLog.Set(); }
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, logs.Entries.Count);
        Assert.Contains(logs.Entries, e => e.Message == "[FPS] Varjo API: 0.0 | Baballonia: 0.0 | Stream Camera: 0.2");
        Assert.Contains(logs.Entries, e => e.Message.StartsWith("[Latency] Varjo API: N/A | Baballonia: N/A | Stream Camera: "));
        monitor.Dispose();
        tracking.PublishCamera(new EyeCameraFrame(true, 1, 1, [0], 2));
        Assert.Equal(2, logs.Entries.Count);
    }

    [Fact]
    public async Task Settings_ToggleMeasurementsLiveAndResetReportingWindow()
    {
        var settings = new InMemoryAppSettingsService();
        settings.Current.TrackingDiagnosticsEnabled = false;
        var tracking = new FakeTrackingDataProvider();
        var logs = new InMemoryLogService();
        using var monitor = new TrackingDiagnosticsService(tracking, new FakeBaballoniaTrackingService(), logs, settings);
        monitor.Start();
        tracking.PublishCamera(new EyeCameraFrame(true, 1, 1, [0], 1));
        settings.Current.TrackingDiagnosticsEnabled = true;
        settings.NotifyChanged();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (logs.Entries.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Contains(logs.Entries, e => e.Message == "[FPS] Varjo API: 0.0 | Baballonia: 0.0 | Stream Camera: 0.0");
        settings.Current.TrackingDiagnosticsEnabled = false;
        settings.NotifyChanged();
        var count = logs.Entries.Count;
        tracking.PublishCamera(new EyeCameraFrame(true, 1, 1, [0], 2));
        await Task.Delay(TimeSpan.FromMilliseconds(5200));
        Assert.Equal(count, logs.Entries.Count);
        settings.Current.TrackingDiagnosticsEnabled = true;
        settings.NotifyChanged();
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (logs.Entries.Count < count + 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(count + 2, logs.Entries.Count);
        Assert.Equal("[FPS] Varjo API: 0.0 | Baballonia: 0.0 | Stream Camera: 0.0", logs.Entries[0].Message);
    }

    [Fact]
    public void Counter_MeasuresAverageAgeAndUniqueFramesOverActualWindow()
    {
        var counter = new TrackingDiagnosticsService.Counter();
        counter.Add(1, 1_000_000, 11_000_000);
        counter.Add(1, 1_000_000, 21_000_000);
        counter.Add(2, 2_000_000, 32_000_000);
        var sample = counter.Take(5);
        Assert.Equal("20.0 ms", sample.Latency);
        Assert.Equal("0.4", sample.Fps);
        Assert.Equal(("N/A", "0.0"), counter.Take(5));
        counter.Add(2, 2_000_000, 32_000_000);
        Assert.Equal(("N/A", "0.0"), counter.Take(5));
    }

    [Fact]
    public void Counter_IgnoresInvalidLatencyButCountsFramesAndHandlesRestart()
    {
        var counter = new TrackingDiagnosticsService.Counter();
        counter.Add(10, 0, 0);
        counter.Add(11, 100, 99);
        counter.Add(1, 100, 0);
        Assert.Equal(("N/A", "1.5"), counter.Take(2));
    }
}
