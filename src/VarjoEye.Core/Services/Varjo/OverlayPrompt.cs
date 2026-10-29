using VarjoEye.Core.Contracts;

namespace VarjoEye.Core.Services.Varjo;

public static class OverlayPrompt
{
    public static (string Title, string Hint) For(CalibrationOverlayHint hint) => hint.Phase switch
    {
        CalibrationOverlayPhase.Waiting => ("Look at the center",
            "Keep your head still. The same prompt is in the headset."),
        CalibrationOverlayPhase.Blink => ("Close both eyes",
            "Keep them closed until the ring finishes."),
        CalibrationOverlayPhase.Sweep when hint.DurationSeconds >= 60 =>
            ("Follow the purple target", "Do not move your head. Track the dot with your eyes."),
        CalibrationOverlayPhase.Sweep =>
            ("Follow the purple target", "Match the headset prompt."),
        _ => ("Training", "")
    };

    public static IReadOnlyList<string> HudLines(CalibrationOverlayHint hint)
    {
        var (title, _) = For(hint);
        return hint.Phase switch
        {
            CalibrationOverlayPhase.Waiting => [title, "Keep your head still"],
            CalibrationOverlayPhase.Blink => [title, "Keep them closed"],
            CalibrationOverlayPhase.Sweep when hint.DurationSeconds >= 60 =>
                [title, "Do not move your head", "Track the dot with your eyes"],
            CalibrationOverlayPhase.Sweep => [title, "Match this target"],
            _ => [title]
        };
    }
}
