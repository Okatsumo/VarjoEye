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
        Assert.Equal("headset-unknown.png", vm.Headset.ImageAssetKey);
        Assert.False(vm.HasTrackingData);
        Assert.Equal("—", vm.LeftMetrics.PupilDiameter);
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
        Assert.Equal("headset-xr-4.png", vm.Headset.ImageAssetKey);
        Assert.True(vm.HasTrackingData);
        Assert.Equal("4.10 mm", vm.LeftMetrics.PupilDiameter);
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
    public void RecalibrateCommand_ReportsProviderResult()
    {
        var logs = new InMemoryLogService();
        var provider = new FakeTrackingDataProvider
        {
            CalibrationResult = OperationResult.Failure("No Varjo headset is connected.")
        };
        var vm = Create(logs, provider);
        vm.RecalibrateCommand.Execute(null);
        Assert.True(vm.IsStatusOpen);
        Assert.Contains("No Varjo headset", vm.StatusMessage);
        Assert.Contains(logs.Entries, e => e.Message.Contains("No Varjo headset"));
    }

    [Fact]
    public void TrainModelCommand_ShowsNotConnectedMessage()
    {
        var vm = Create();
        vm.TrainModelCommand.Execute(null);
        Assert.True(vm.IsStatusOpen);
        Assert.Contains("Train Model", vm.StatusMessage);
    }

    private static DashboardViewModel Create(ILogService? logs = null, FakeTrackingDataProvider? provider = null)
    {
        return new DashboardViewModel(
            provider ?? new FakeTrackingDataProvider(),
            logs ?? new InMemoryLogService(),
            new ImmediateDispatcher());
    }

    private static EyeTrackingState Live(HeadsetModel model, string name)
    {
        var visual = new EyeSourceVisual(0.9, 0.1, -0.05, 0.55);
        var varjo = new VarjoEyeMetrics(
            0.9, 4.1, 11.6, 0.35, 0.55, "High",
            new Vector3Value(-0.03, 0.02, -0.04),
            new Vector3Value(0.1, -0.05, 0.99));
        var eye = new EyeSourceState(visual, varjo, BabbleEyeMetrics.Empty, CombinedEyeMetrics.Empty);
        return new EyeTrackingState(
            HeadsetState.For(model, ConnectionStatus.Connected) with { Name = name },
            CameraState.Waiting("Left Eye Camera"),
            CameraState.Waiting("Right Eye Camera"),
            eye,
            eye,
            true);
    }
}
