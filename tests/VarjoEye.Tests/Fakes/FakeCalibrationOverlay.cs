using VarjoEye.Core.Contracts;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeCalibrationOverlay : ICalibrationOverlay
{
    public string StatusText { get; set; } = "Overlay idle";

    public CalibrationOverlayHint CurrentHint { get; private set; } =
        new(CalibrationOverlayPhase.Hidden, 0, 0, 0);

    public event EventHandler? Changed;

    public void Show(CalibrationOverlayHint hint)
    {
        CurrentHint = hint;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Hide()
    {
        CurrentHint = new(CalibrationOverlayPhase.Hidden, 0, 0, 0);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}
