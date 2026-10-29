namespace VarjoEye.Core.Contracts;

public enum CalibrationOverlayPhase
{
    Hidden,
    Waiting,
    Sweep,
    Blink
}

public readonly record struct CalibrationOverlayHint(
    CalibrationOverlayPhase Phase,
    double TargetX,
    double TargetY,
    double Progress,
    double DurationSeconds = 0,
    long PhaseStartedNanoseconds = 0);

public interface ICalibrationOverlay : IDisposable
{
    string StatusText { get; }

    CalibrationOverlayHint CurrentHint { get; }

    event EventHandler? Changed;

    void Show(CalibrationOverlayHint hint);

    void Hide();
}
