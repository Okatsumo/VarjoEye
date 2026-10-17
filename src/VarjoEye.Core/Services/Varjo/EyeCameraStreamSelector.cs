using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Varjo;

public readonly record struct EyeCameraStreamOption(
    long StreamId,
    ulong ChannelFlags,
    int FrameRate,
    int Width,
    int Height);

public static class EyeCameraStreamSelector
{
    public static EyeCameraStreamOption? Select(
        IEnumerable<EyeCameraStreamOption> options,
        EyeTrackingFrequency frequency)
    {
        var list = options as IList<EyeCameraStreamOption> ?? [.. options];
        if (list.Count == 0)
        {
            return null;
        }

        var targetHz = frequency switch
        {
            EyeTrackingFrequency.Hz100 => 100,
            EyeTrackingFrequency.Hz200 => 200,
            _ => (int?)null
        };

        IEnumerable<EyeCameraStreamOption> pool = list;
        if (targetHz is { } hz)
        {
            var exact = list.Where(option => option.FrameRate == hz).ToList();
            if (exact.Count > 0)
            {
                pool = exact;
            }
            else
            {
                var below = list.Where(option => option.FrameRate > 0 && option.FrameRate <= hz).ToList();
                if (below.Count > 0)
                {
                    var bestRate = below.Max(option => option.FrameRate);
                    pool = below.Where(option => option.FrameRate == bestRate);
                }
                else
                {
                    var bestRate = list.Min(option => option.FrameRate);
                    pool = list.Where(option => option.FrameRate == bestRate);
                }
            }
        }
        else
        {
            var bestRate = list.Max(option => option.FrameRate);
            pool = list.Where(option => option.FrameRate == bestRate);
        }

        return pool
            .OrderByDescending(option => option.Width * (long)option.Height)
            .ThenByDescending(option => option.Width)
            .First();
    }
}
