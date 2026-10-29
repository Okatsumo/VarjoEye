using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public sealed record BaballoniaCalibration(
    BaballoniaEyeCalibration Left,
    BaballoniaEyeCalibration Right,
    int SampleCount,
    long FittedAtUnixSeconds,
    int PipelineVersion = 0,
    BaballoniaOutputHead? OutputHead = null)
{
    public const int CurrentPipelineVersion = 9;

    public bool IsPlausible => PipelineVersion == CurrentPipelineVersion &&
        SampleCount >= 20 && IsPlausibleEye(Left) && IsPlausibleEye(Right) &&
        (OutputHead is null || OutputHead.IsPlausible);

    private static bool IsPlausibleEye(BaballoniaEyeCalibration eye) =>
        double.IsFinite(eye.GazeA) && double.IsFinite(eye.GazeB) && double.IsFinite(eye.GazeC) &&
        double.IsFinite(eye.GazeD) && double.IsFinite(eye.GazeE) && double.IsFinite(eye.GazeF) &&
        double.IsFinite(eye.OpennessScale) && double.IsFinite(eye.OpennessBias) &&
        Math.Abs(eye.GazeA) + Math.Abs(eye.GazeB) <= 2.0 &&
        Math.Abs(eye.GazeD) + Math.Abs(eye.GazeE) <= 2.0 &&
        Math.Abs(eye.GazeC) <= 1.0 && Math.Abs(eye.GazeF) <= 1.0 &&
        eye.OpennessScale is > 0 and <= 2 && Math.Abs(eye.OpennessBias) <= 0.7;
}

public readonly record struct BaballoniaCalibrationSample(
    double X, double Y, double Openness,
    double TargetX, double TargetY, double TargetOpenness,
    BaballoniaRawOutput? Onnx = null);

public readonly record struct BaballoniaHeadSample(
    BaballoniaRawOutput Onnx,
    double LeftX, double LeftY, double LeftOpenness,
    double RightX, double RightY, double RightOpenness);
