using Xunit;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.ViewModels;
using VarjoEye.Tests.Fakes;

namespace VarjoEye.Tests.ViewModels;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void Initializes_WithoutHeadset()
    {
        var vm = Create();
        Assert.Equal(ConnectionStatus.Disconnected, vm.Headset.Connection);
        Assert.Equal("No headset", vm.Headset.Name);
        Assert.Equal("unknown", vm.Headset.ModelId);
        Assert.False(vm.HasTrackingData);
        Assert.Equal("—", vm.LeftMetrics.PupilDiameter);
        Assert.False(vm.RecalibrateCommand.CanExecute(null));
    }

    [Fact]
    public void LiveUpdate_MapsHeadsetAssetAndMetrics()
    {
        var provider = new FakeTrackingDataProvider();
        var vm = Create(provider: provider);
        vm.OnNavigatedTo();
        provider.Publish(Live(HeadsetCatalog.Xr4, "Varjo XR-4"));

        Assert.Equal(ConnectionStatus.Connected, vm.Headset.Connection);
        Assert.Equal("Varjo XR-4", vm.Headset.Name);
        Assert.Equal("xr-4", vm.Headset.ModelId);
        Assert.True(vm.HasTrackingData);
        Assert.Equal("4.10 mm", vm.LeftMetrics.PupilDiameter);
        Assert.True(vm.RecalibrateCommand.CanExecute(null));
    }

    [Fact]
    public void HiddenDashboard_DoesNotApplyUpdates()
    {
        var provider = new FakeTrackingDataProvider();
        var vm = Create(provider: provider);
        vm.OnNavigatedTo();
        vm.OnNavigatedFrom();
        provider.Publish(Live(HeadsetCatalog.Aero, "Varjo Aero"));
        Assert.False(vm.HasTrackingData);
        Assert.Equal("No headset", vm.Headset.Name);
    }

    [Fact]
    public void VisibleDashboard_AppliesVarjoCameraFrames()
    {
        var provider = new FakeTrackingDataProvider();
        var vm = Create(provider: provider);
        vm.OnNavigatedTo();
        var pixels = new byte[4];
        provider.PublishCamera(new EyeCameraFrame(true, 2, 2, pixels, 1));

        Assert.True(vm.VarjoLeftCamera.IsAvailable);
        Assert.Equal(2, vm.VarjoLeftCamera.Width);
        Assert.Same(pixels, vm.VarjoLeftCamera.Pixels);
        Assert.False(vm.VarjoRightCamera.IsAvailable);
    }

    [Fact]
    public void HiddenDashboard_DoesNotApplyCameraFrames()
    {
        var provider = new FakeTrackingDataProvider();
        var vm = Create(provider: provider);
        vm.OnNavigatedTo();
        vm.OnNavigatedFrom();
        provider.PublishCamera(new EyeCameraFrame(true, 2, 2, new byte[4], 1));

        Assert.False(vm.VarjoLeftCamera.IsAvailable);
    }

    [Fact]
    public void LayerToggles_AreIndependent()
    {
        var provider = new FakeTrackingDataProvider { Current = Live(HeadsetCatalog.Aero, "Varjo Aero") };
        var vm = Create(provider: provider);
        Assert.True(vm.ShowVarjoLayer);
        vm.ShowVarjoLayer = false;
        vm.ShowBaballoniaLayer = false;
        Assert.False(vm.ShowVarjoLayer);
        Assert.False(vm.ShowBaballoniaLayer);
        Assert.True(vm.ShowCombinedLayer);
        Assert.NotEqual("—", vm.LeftMetrics.PupilDiameter);
    }

    [Fact]
    public async Task RecalibrateCommand_ReportsProviderResult()
    {
        var logs = new InMemoryLogService();
        var provider = new FakeTrackingDataProvider
        {
            Current = Live(HeadsetCatalog.Aero, "Varjo Aero"),
            CalibrationResult = OperationResult.Failure("No Varjo headset is connected.")
        };
        var vm = Create(logs, provider);
        Assert.True(vm.RecalibrateCommand.CanExecute(null));
        await vm.RecalibrateCommand.ExecuteAsync(null);
        Assert.True(vm.IsStatusOpen);
        Assert.Contains("No Varjo headset", vm.StatusMessage);
        Assert.Contains(logs.Entries, e => e.Message.Contains("No Varjo headset"));
    }

    [Fact]
    public async Task RecalibrateCommand_StartsBabbleCollection()
    {
        var calibrator = new FakeBaballoniaCalibrator();
        var provider = new FakeTrackingDataProvider
        {
            Current = Live(HeadsetCatalog.Aero, "Varjo Aero")
        };
        var vm = Create(provider: provider, calibrator: calibrator);
        Assert.True(vm.RecalibrateCommand.CanExecute(null));
        await vm.RecalibrateCommand.ExecuteAsync(null);
        Assert.Equal(1, calibrator.BeginCount);
        Assert.Contains("Waiting for Varjo calibration", vm.StatusMessage);
    }

    [Fact]
    public void RecalibrateCommand_DisabledWhileCalibrating()
    {
        var provider = new FakeTrackingDataProvider();
        var vm = Create(provider: provider);
        vm.OnNavigatedTo();
        provider.Publish(Live(HeadsetCatalog.Aero, "Varjo Aero"));
        Assert.True(vm.RecalibrateCommand.CanExecute(null));
        Assert.Equal("Recalibrate", vm.RecalibrateButtonText);

        provider.Publish(Live(HeadsetCatalog.Aero, "Varjo Aero", calibrating: true));
        Assert.False(vm.RecalibrateCommand.CanExecute(null));
        Assert.Equal("Recalibrating", vm.RecalibrateButtonText);
    }

    [Fact]
    public void VisibleDashboard_OverlaysBaballoniaSample()
    {
        var provider = new FakeTrackingDataProvider();
        var babble = new FakeBaballoniaTrackingService();
        var vm = Create(provider: provider, baballonia: babble);
        vm.OnNavigatedTo();
        provider.Publish(Live(HeadsetCatalog.Aero, "Varjo Aero"));
        babble.Publish(new BaballoniaTrackingSample(
            BaballoniaRuntimeStatus.Tracking,
            "Running on CPU",
            true,
            false,
            new BaballoniaEyeResult(
                1,
                DateTimeOffset.UtcNow,
                new BaballoniaEye(0.2, -0.1, 0.8),
                new BaballoniaEye(-0.3, 0.1, 0.4)),
            new CameraFrameState(EyeSide.Left, CameraAvailability.Streaming, 128, 128, new byte[128 * 128]),
            new CameraFrameState(EyeSide.Right, CameraAvailability.Streaming, 128, 128, new byte[128 * 128])));

        Assert.True(vm.LeftCamera.IsAvailable);
        Assert.Equal(128, vm.LeftCamera.Width);
        Assert.True(vm.LeftEye.Babble.HasSample);
        Assert.Equal(0.8, vm.LeftEye.Babble.Openness);
        Assert.Equal("0.80", vm.LeftMetrics.BabbleOpenness);
        Assert.Equal("4.10 mm", vm.LeftMetrics.PupilDiameter);
    }

    [Fact]
    public void TrainModelCommand_StartsTrainer()
    {
        var trainer = new FakeBaballoniaTrainer();
        var vm = Create(trainer: trainer);
        vm.TrainModelCommand.Execute(null);
        Assert.Equal(1, trainer.BeginCount);
        Assert.True(vm.IsTrainingBusy);
        Assert.Contains("look at the center", vm.StatusMessage);
    }

    [Fact]
    public void TrainModelCommand_ReportsWhenTrainerCannotStart()
    {
        var trainer = new FakeBaballoniaTrainer { BeginResult = false };
        var vm = Create(trainer: trainer);
        vm.TrainModelCommand.Execute(null);
        Assert.Contains("Finish Varjo calibration", vm.StatusMessage);
        Assert.False(vm.IsTrainingBusy);
    }

    [Fact]
    public void Navigation_EnablesPreviewOnlyWhileVisible()
    {
        var babble = new FakeBaballoniaTrackingService();
        using var vm = Create(baballonia: babble);
        Assert.False(babble.PreviewEnabled);
        vm.OnNavigatedTo();
        Assert.True(babble.PreviewEnabled);
        vm.OnNavigatedFrom();
        Assert.False(babble.PreviewEnabled);
    }

    private static DashboardViewModel Create(
        ILogService? logs = null,
        FakeTrackingDataProvider? provider = null,
        FakeBaballoniaTrackingService? baballonia = null,
        FakeBaballoniaTrainer? trainer = null,
        FakeBaballoniaCalibrator? calibrator = null)
    {
        return new DashboardViewModel(
            provider ?? new FakeTrackingDataProvider(),
            baballonia ?? new FakeBaballoniaTrackingService(),
            trainer ?? new FakeBaballoniaTrainer(),
            calibrator ?? new FakeBaballoniaCalibrator(),
            new FakeCalibrationOverlay(),
            logs ?? new InMemoryLogService(),
            new ImmediateDispatcher());
    }

    private static EyeTrackingState Live(HeadsetModel model, string name, bool calibrating = false)
    {
        var visual = new EyeSourceVisual(0.9, 0.1, -0.05, 0.55);
        var varjo = new VarjoEyeMetrics(
            0.9, 4.1, 11.6, 0.35, 0.55, "High",
            new Vector3Value(-0.03, 0.02, -0.04),
            new Vector3Value(0.1, -0.05, 0.99));
        var eye = new EyeSourceState(visual, varjo, BabbleEyeMetrics.Empty, CombinedEyeMetrics.Empty);
        return new EyeTrackingState(
            HeadsetState.For(model, ConnectionStatus.Connected) with
            {
                Name = name,
                IsGazeCalibrating = calibrating
            },
            CameraFrameState.Waiting(EyeSide.Left),
            CameraFrameState.Waiting(EyeSide.Right),
            eye,
            eye,
            true);
    }
}
