using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal static class BlinkLabelPolicy
{
    public const double ClosedOpenness = 0.25;
    public const double OpenOpenness = 0.75;

    public static bool TryLabel(bool blinkPhase, EyeTrackingEye? eye, out double lid)
    {
        if (!blinkPhase)
        {
            lid = 0;
            return true;
        }

        if (eye is null || !double.IsFinite(eye.Openness))
        {
            lid = 0;
            return false;
        }

        if (eye.Openness < ClosedOpenness)
        {
            lid = 1;
            return true;
        }

        if (eye.Openness >= OpenOpenness)
        {
            lid = 0;
            return true;
        }

        lid = 0;
        return false;
    }
}
