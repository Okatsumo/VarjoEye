using VarjoEye.App.Services;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.ViewModels;
using VarjoEye.Tests.Fakes;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class ArchitectureBoundaryTests
{
    [Theory]
    [InlineData(0.5, 420, 28, 196)]
    [InlineData(0.5, 280, 28, 126)]
    [InlineData(0.5, 100, 28, 36)]
    [InlineData(0.5, 840, 56, 392)]
    [InlineData(0, 420, 28, 0)]
    [InlineData(1, 420, 28, 392)]
    [InlineData(-10, 100, 28, 0)]
    [InlineData(10, 100, 28, 72)]
    [InlineData(0.5, 10, 28, 0)]
    [InlineData(double.NaN, 100, 28, 36)]
    public void MarkerCenter_UsesActualGeometryAndStaysInside(double normalized, double extent, double marker, double expected) =>
        Assert.Equal(expected, TargetPosition.Calculate(normalized, extent, marker));

    [Theory]
    [InlineData(-1, 1, 0.08, 0.08)]
    [InlineData(1, -1, 0.92, 0.92)]
    [InlineData(0, 0, 0.5, 0.5)]
    public void Prompt_NormalizesAxes(double x, double y, double expectedX, double expectedY)
    {
        var overlay = new FakeCalibrationOverlay();
        using var vm = Dashboard(overlay);
        overlay.Show(new(CalibrationOverlayPhase.Sweep, x, y, 0));
        Assert.True(vm.PromptShowTarget);
        Assert.Equal(expectedX, vm.PromptTargetX);
        Assert.Equal(expectedY, vm.PromptTargetY);
        overlay.Show(new(CalibrationOverlayPhase.Sweep, double.NaN, y, 0));
        Assert.False(vm.PromptShowTarget);
        Assert.True(double.IsFinite(vm.PromptTargetX));
    }

    private static DashboardViewModel Dashboard(FakeCalibrationOverlay overlay) => new(
        new FakeTrackingDataProvider(), new FakeBaballoniaTrackingService(), new FakeBaballoniaTrainer(),
        new FakeBaballoniaCalibrator(), overlay, new InMemoryLogService(), new ImmediateDispatcher());

    [Theory]
    [InlineData(EyeSide.Left, "Left Eye Camera")]
    [InlineData(EyeSide.Right, "Right Eye Camera")]
    public void CameraPresentation_PreservesPixelsAndHandlesReconnect(EyeSide eye, string label)
    {
        var pixels = new byte[128 * 128];
        var frame = new CameraFrameState(eye, CameraAvailability.Streaming, 128, 128, pixels, 123);
        var live = CameraState.From(frame);
        Assert.Equal(label, live.Label);
        Assert.True(live.IsAvailable);
        Assert.Same(pixels, live.Pixels);
        Assert.Same(live, CameraState.From(frame, live));
        var waiting = CameraState.From(CameraFrameState.Waiting(eye), live);
        Assert.False(waiting.IsAvailable);
        Assert.Null(waiting.Pixels);
        var restored = CameraState.From(frame, waiting);
        Assert.True(restored.IsAvailable);
        Assert.Same(pixels, restored.Pixels);
        Assert.False(CameraState.From(frame with { Availability = CameraAvailability.Error }).IsAvailable);
    }

    [Fact]
    public void UnchangedCameraMapping_DoesNotAllocate()
    {
        var frame = new CameraFrameState(EyeSide.Left, CameraAvailability.Streaming, 128, 128, new byte[128 * 128]);
        var state = CameraState.From(frame);
        for (var i = 0; i < 100; i++) state = CameraState.From(frame, state);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) state = CameraState.From(frame, state);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(frame.Pixels, state.Pixels);
    }

    [Theory]
    [InlineData("aero", "headset-aero.png")]
    [InlineData("vr-1", "headset-vr-1.png")]
    [InlineData("vr-2", "headset-vr-2.png")]
    [InlineData("vr-3", "headset-vr-3.png")]
    [InlineData("xr-1", "headset-xr-1.png")]
    [InlineData("xr-3", "headset-xr-3.png")]
    [InlineData("xr-4", "headset-xr-4.png")]
    [InlineData("future", "headset-unknown.png")]
    [InlineData(null, "headset-unknown.png")]
    public void HeadsetAssets_MapIdsWithFallback(string? id, string asset) =>
        Assert.Equal(asset, HeadsetImageCatalog.GetImageKey(id));

    [Fact]
    public void Shell_HandlesMissingPathsAndLaunchFailure()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var calls = new List<string>();
        var shell = new WindowsShellService(info => { Assert.True(info.UseShellExecute); calls.Add(info.FileName); });
        Assert.False(shell.Open(folder).IsSuccess);
        Assert.False(shell.Open(null).IsSuccess);
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "test.log");
            File.WriteAllText(file, "log");
            Assert.True(shell.Open(folder).IsSuccess);
            Assert.True(shell.Open(file).IsSuccess);
            Assert.Equal(new[] { folder, file }, calls);
            var broken = new WindowsShellService(_ => throw new InvalidOperationException("launch failed"));
            Assert.Equal("launch failed", broken.Open(file).ErrorMessage);
            File.Delete(file);
            Assert.False(shell.Open(file).IsSuccess);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void LogCommands_DelegatePathsAndDisplayLaunchErrors()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var logs = new FileLogService(folder);
            var launcher = new FakeFileLauncher();
            using var vm = new LogsViewModel(logs, new ImmediateDispatcher(), launcher);
            vm.OpenFolderCommand.Execute(null);
            Assert.Equal(logs.DirectoryPath, launcher.LastPath);
            launcher.Result = OperationResult.Failure("launch failed");
            vm.OpenLatestFileCommand.Execute(null);
            Assert.Equal(logs.CurrentFilePath, launcher.LastPath);
            Assert.Contains(vm.VisibleEntries, entry => entry.Level == LogLevel.Warning && entry.Message == "launch failed");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ModelPaths_PreservePublishLayoutAndUserPrecedence()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var model = BaballoniaModelPaths.Create(Path.Combine(folder, "publish"), Path.Combine(folder, "local"));
        try
        {
            Assert.Equal(Path.Combine(folder, "publish", "Models", "Baballonia.eyeModel.onnx"), model.StockPath);
            Assert.Equal(Path.Combine(folder, "local", "VarjoEye", "Models", "Baballonia.eyeModel.user.onnx"), model.UserPath);
            Assert.False(model.IsAvailable);
            Directory.CreateDirectory(Path.GetDirectoryName(model.StockPath)!);
            File.Copy(TestModelPaths.Create().StockPath, model.StockPath);
            Assert.True(model.IsAvailable);
            Assert.Equal(model.StockPath, model.ResolvedPath);
            Directory.CreateDirectory(model.UserDirectory);
            File.Copy(model.StockPath, model.UserPath);
            Assert.Equal(model.UserPath, model.ResolvedPath);
            using (var inference = new BaballoniaEyeInference(model.ResolvedPath, InferenceDevice.Cpu)) { }
            File.WriteAllText(model.UserPath, "invalid replacement");
            Assert.ThrowsAny<Exception>(() => new BaballoniaEyeInference(model.ResolvedPath, InferenceDevice.Cpu));
            File.Copy(model.StockPath, model.UserPath, true);
            using (var reloaded = new BaballoniaEyeInference(model.ResolvedPath, InferenceDevice.Cpu)) { }
            File.Delete(model.UserPath);
            Assert.Equal(model.StockPath, model.ResolvedPath);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.onnx")]
    [InlineData("C:\0invalid")]
    public void ModelPaths_RejectInvalidPaths(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => new BaballoniaEyeModel(path, TestModelPaths.Create().UserPath));
}
