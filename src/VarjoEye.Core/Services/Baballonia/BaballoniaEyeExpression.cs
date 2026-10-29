using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public static class BaballoniaEyeExpression
{
    private const double ClosedLid = 0.18;

    public static (BaballoniaEye Left, BaballoniaEye Right) FromRaw(ReadOnlySpan<float> values)
    {
        var leftOpen = ToOpenness(values[2]);
        var rightOpen = ToOpenness(values[5]);
        var leftYaw = ToAxis(values[1]);
        var rightYaw = ToAxis(values[4]);
        var leftPitch = ToAxis(values[0]);
        var rightPitch = ToAxis(values[3]);
        var leftVisible = leftOpen > ClosedLid;
        var rightVisible = rightOpen > ClosedLid;

        if (leftVisible && rightVisible)
        {
            var pitch = (leftPitch * leftOpen + rightPitch * rightOpen) /
                Math.Max(leftOpen + rightOpen, 0.001);
            var leftCorrected = rightYaw * (1 - leftOpen) + leftYaw * leftOpen;
            var rightCorrected = leftYaw * (1 - rightOpen) + rightYaw * rightOpen;
            var center = (leftCorrected + rightCorrected) / 2;
            var convergence = Math.Max((rightCorrected - leftCorrected) / 2, 0);
            return (
                new BaballoniaEye(center + convergence, pitch, leftOpen),
                new BaballoniaEye(center - convergence, pitch, rightOpen));
        }

        if (leftVisible)
        {
            return (
                new BaballoniaEye(leftYaw, leftPitch, leftOpen),
                new BaballoniaEye(leftYaw, leftPitch, rightOpen));
        }

        if (rightVisible)
        {
            return (
                new BaballoniaEye(rightYaw, rightPitch, leftOpen),
                new BaballoniaEye(rightYaw, rightPitch, rightOpen));
        }

        return (
            new BaballoniaEye(leftYaw, leftPitch, leftOpen),
            new BaballoniaEye(rightYaw, rightPitch, rightOpen));
    }

    public static double ToAxis(float value) => Math.Clamp(value * 2d - 1d, -1d, 1d);

    public static double ToOpenness(float lid) => Math.Clamp(1d - lid, 0d, 1d);

    public static double ToOnnxAxis(double ui) => Math.Clamp((ui + 1d) / 2d, 0d, 1d);

    public static double ToOnnxLid(double openness) => Math.Clamp(1d - openness, 0d, 1d);
}
