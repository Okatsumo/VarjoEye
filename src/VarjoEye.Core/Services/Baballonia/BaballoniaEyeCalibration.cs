using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public sealed record BaballoniaEyeCalibration(
    double GazeA, double GazeB, double GazeC,
    double GazeD, double GazeE, double GazeF,
    double OpennessScale, double OpennessBias)
{
    public static BaballoniaEyeCalibration Identity { get; } = new(1, 0, 0, 0, 1, 0, 1, 0);

    public bool IsIdentity =>
        GazeA == 1 && GazeB == 0 && GazeC == 0 && GazeD == 0 && GazeE == 1 && GazeF == 0 &&
        OpennessScale == 1 && OpennessBias == 0;

    public BaballoniaEye Map(BaballoniaEye raw) => new(
        Math.Clamp(GazeA * raw.GazeX + GazeB * raw.GazeY + GazeC, -1, 1),
        Math.Clamp(GazeD * raw.GazeX + GazeE * raw.GazeY + GazeF, -1, 1),
        Math.Clamp(OpennessScale * raw.Openness + OpennessBias, 0, 1));
}
