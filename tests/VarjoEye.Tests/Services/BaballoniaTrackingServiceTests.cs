using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Tests.Fakes;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaTrackingServiceTests
{
    [Fact]
    public async Task StopAsync_CompletesWithoutFrames()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        service.Start();
        await service.StopAsync();
        Assert.False(service.Current.HasResult);
    }

    [Fact]
    public async Task UnmatchedFrames_DoNotProduceResult()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        service.Start();
        frames.PublishCamera(Gray(true, 1));
        frames.PublishCamera(Gray(false, 2));
        await Task.Delay(50);
        Assert.False(service.Current.HasResult);
        await service.StopAsync();
    }

    [Fact]
    public void SelectDevice_ReloadsAndPersistsIndex()
    {
        var settings = new InMemoryAppSettingsService();
        var frames = new FakeTrackingDataProvider();
        using var service = new BaballoniaTrackingService(
            frames,
            settings,
            new InMemoryLogService(),
            [InferenceDevice.Cpu, new InferenceDevice(0, "GPU 0 · Test")],
            TimeSpan.FromSeconds(1), null, TestModelPaths.Create());
        var gpu = service.AvailableDevices[1];
        service.SelectDevice(gpu);
        Assert.Equal(gpu, service.SelectedDevice);
        Assert.Equal(1, settings.Current.InferenceDeviceIndex);
    }

    [Fact]
    public async Task Shutdown_AfterPairedFrames_DoesNotThrow()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        service.Start();
        frames.PublishCamera(Gray(true, 3));
        frames.PublishCamera(Gray(false, 3));
        await service.StopAsync();
        Assert.False(service.Current.HasResult);
    }

    [Fact]
    public void Inference_OnFourPairedFrames_ProducesFiniteEyes()
    {
        var path = TestModelPaths.Create().StockPath;
        Assert.True(File.Exists(path));

        using var inference = new BaballoniaEyeInference(path, InferenceDevice.Cpu);
        var window = new List<CameraEyeFramePair>();
        for (var i = 0; i < 4; i++)
        {
            window.Add(new CameraEyeFramePair(
                i + 1,
                DateTimeOffset.UtcNow,
                Gray(true, i + 1, 1_000_000L * i),
                Gray(false, i + 1, 1_000_000L * i)));
        }

        var result = inference.Run(window);
        Assert.NotNull(result);
        Assert.True(double.IsFinite(result!.Left.Openness));
        Assert.True(double.IsFinite(result.Right.GazeX));
    }

    [Fact]
    public async Task Reload_ResolvesReplacedUserModelAndRecoversFromMissingModel()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var model = new BaballoniaEyeModel(Path.Combine(folder, "stock.onnx"), Path.Combine(folder, "user.onnx"));
            var logs = new InMemoryLogService();
            using var service = new BaballoniaTrackingService(new FakeTrackingDataProvider(),
                new InMemoryAppSettingsService(), logs, [InferenceDevice.Cpu], TimeSpan.FromSeconds(2), null, model);
            service.Start();
            service.ReloadModel();
            await Until(() => service.Current.Status == BaballoniaRuntimeStatus.ModelUnavailable);
            File.Copy(TestModelPaths.Create().StockPath, model.StockPath);
            service.ReloadModel();
            await Until(() => logs.Entries.Any(e => e.Message.Contains("model loaded") && e.Message.Contains("stock")));
            File.Copy(model.StockPath, model.UserPath);
            service.ReloadModel();
            await Until(() => service.ActiveDeviceText.Contains("trained last layer"));
            var loaded = logs.Entries.Count(e => e.Message.Contains("model loaded"));
            File.Copy(model.StockPath, model.UserPath, true);
            service.ReloadModel();
            await Until(() => logs.Entries.Count(e => e.Message.Contains("model loaded")) > loaded);
            Assert.Contains("trained last layer", service.ActiveDeviceText);
            await service.StopAsync();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task BackendPreview_PublishesOwnedGrayFramesWithTimestamp()
    {
        var frames = new FakeTrackingDataProvider();
        using var service = Create(frames);
        service.SetPreviewEnabled(true);
        bool? workerUsesThreadPool = null;
        service.Updated += (_, sample) => { if (sample.HasResult) workerUsesThreadPool = Thread.CurrentThread.IsThreadPoolThread; };
        service.Start();
        for (var i = 1; i <= 4; i++)
        {
            frames.PublishCamera(Gray(true, i, i * 5_000_000L));
            frames.PublishCamera(Gray(false, i, i * 5_000_000L));
        }
        await Until(() => service.Current.HasResult);
        var sample = service.Current;
        await Until(() => workerUsesThreadPool is not null);
        Assert.False(workerUsesThreadPool);
        Assert.Equal(EyeSide.Left, sample.LeftPreview.Eye);
        Assert.Equal(EyeSide.Right, sample.RightPreview.Eye);
        Assert.Equal(CameraAvailability.Streaming, sample.LeftPreview.Availability);
        Assert.Equal(CameraPixelFormat.Gray8, sample.LeftPreview.PixelFormat);
        Assert.Equal(20_000_000, sample.LeftPreview.TimestampNanoseconds);
        Assert.Equal(128 * 128, sample.LeftPreview.Pixels!.Length);
        var pixels = sample.LeftPreview.Pixels;
        var saved = pixels.ToArray();
        await service.StopAsync();
        Assert.Equal(CameraAvailability.Unavailable, service.Current.LeftPreview.Availability);
        Assert.Equal(saved, pixels);
        Assert.Same(pixels, CameraState.From(sample.LeftPreview).Pixels);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static BaballoniaTrackingService Create(FakeTrackingDataProvider frames) =>
        new(
            frames,
            new InMemoryAppSettingsService(),
            new InMemoryLogService(),
            [InferenceDevice.Cpu],
            TimeSpan.FromSeconds(1), null, TestModelPaths.Create());

    private static EyeCameraFrame Gray(bool left, long number, long capture = 0)
    {
        var pixels = new byte[8 * 8];
        return new EyeCameraFrame(left, 8, 8, pixels, number, capture);
    }
}
