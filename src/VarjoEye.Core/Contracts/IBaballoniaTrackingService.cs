using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface IBaballoniaTrackingService : IDisposable
{
    BaballoniaTrackingSample Current { get; }

    IReadOnlyList<InferenceDevice> AvailableDevices { get; }

    InferenceDevice SelectedDevice { get; }

    string ActiveDeviceText { get; }

    /// <summary>
    /// Ordered, sequential delivery on a dedicated worker. When a consumer is slow,
    /// pending snapshots are replaced by the latest; Current continues advancing.
    /// Consumers must use the event snapshot and its timestamp for sample pairing.
    /// StopAsync stops inference; an already executing callback can still finish afterwards.
    /// </summary>
    event EventHandler<BaballoniaTrackingSample>? Updated;

    event EventHandler? DevicesChanged;

    void Start();

    Task StopAsync();

    void SelectDevice(InferenceDevice device);

    void ReloadModel();

    void SetPreviewEnabled(bool enabled);

    void SetTrainingFeaturesEnabled(bool enabled);
}
