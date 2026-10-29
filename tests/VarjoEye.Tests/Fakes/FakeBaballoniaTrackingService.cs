using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeBaballoniaTrackingService : IBaballoniaTrackingService
{
    public BaballoniaTrackingSample Current { get; set; } = BaballoniaTrackingSample.Empty;

    public IReadOnlyList<InferenceDevice> AvailableDevices { get; set; } =
        [InferenceDevice.Cpu, new InferenceDevice(0, "GPU 0 · Test")];

    public InferenceDevice SelectedDevice { get; private set; } = InferenceDevice.Cpu;

    public string ActiveDeviceText { get; set; } = "Waiting for camera";

    public event EventHandler<BaballoniaTrackingSample>? Updated;

    public event EventHandler? DevicesChanged;

    public bool TrainingFeaturesEnabled { get; private set; }

    public void SetTrainingFeaturesEnabled(bool enabled) => TrainingFeaturesEnabled = enabled;

    public bool PreviewEnabled { get; private set; }

    public void SetPreviewEnabled(bool enabled) => PreviewEnabled = enabled;

    public void Start()
    {
    }

    public Task StopAsync() => Task.CompletedTask;

    public void ReloadModel()
    {
    }

    public void SelectDevice(InferenceDevice device)
    {
        SelectedDevice = device;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Publish(BaballoniaTrackingSample sample)
    {
        Current = sample;
        Updated?.Invoke(this, sample);
    }

    public void Dispose()
    {
    }
}
