namespace VarjoEye.App.Services;

internal static class TargetPosition
{
    // Normalized position refers to the marker center. Oversized markers stay at the origin.
    public static double Calculate(double normalized, double extent, double markerExtent) =>
        Math.Clamp((double.IsFinite(normalized) ? normalized : 0.5) * extent - markerExtent / 2,
            0, Math.Max(0, extent - markerExtent));
}
