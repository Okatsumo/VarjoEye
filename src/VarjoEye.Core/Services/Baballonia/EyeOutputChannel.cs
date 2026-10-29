namespace VarjoEye.Core.Services.Baballonia;

internal enum EyeOutputChannel
{
    LeftY = 0,
    LeftX = 1,
    RightOpenness = 2,
    RightY = 3,
    RightX = 4,
    LeftOpenness = 5
}

internal static class EyeOutputChannels
{
    public const int Count = 6;

    public static readonly int[] Gaze = [(int)EyeOutputChannel.LeftY, (int)EyeOutputChannel.LeftX, (int)EyeOutputChannel.RightY, (int)EyeOutputChannel.RightX];
    public static readonly int[] Lids = [(int)EyeOutputChannel.RightOpenness, (int)EyeOutputChannel.LeftOpenness];

    public static bool IsLid(int output) =>
        output is (int)EyeOutputChannel.RightOpenness or (int)EyeOutputChannel.LeftOpenness;
}
