using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public static class BaballoniaFramePreprocessor
{
    public const int Side = 128;

    public static float[] Prepare(EyeCameraFrame frame, (int X, int Y, int Size)? crop)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length != frame.Width * frame.Height)
        {
            throw new ArgumentException("Invalid grayscale camera frame.", nameof(frame));
        }

        var originX = crop?.X ?? 0;
        var originY = crop?.Y ?? 0;
        var sourceWidth = crop?.Size ?? frame.Width;
        var sourceHeight = crop?.Size ?? frame.Height;
        var resized = new byte[Side * Side];
        for (var y = 0; y < Side; y++)
        {
            var sy = Math.Clamp(originY + (y + 0.5) * sourceHeight / Side - 0.5, 0, frame.Height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, frame.Height - 1);
            var fy = sy - y0;
            for (var x = 0; x < Side; x++)
            {
                var sx = Math.Clamp(originX + (x + 0.5) * sourceWidth / Side - 0.5, 0, frame.Width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, frame.Width - 1);
                var fx = sx - x0;
                var top = frame.Pixels[y0 * frame.Width + x0] * (1 - fx) + frame.Pixels[y0 * frame.Width + x1] * fx;
                var bottom = frame.Pixels[y1 * frame.Width + x0] * (1 - fx) + frame.Pixels[y1 * frame.Width + x1] * fx;
                resized[y * Side + x] = (byte)Math.Clamp(Math.Round(top * (1 - fy) + bottom * fy), 0, 255);
            }
        }

        var histogram = new int[256];
        foreach (var pixel in resized)
        {
            histogram[pixel]++;
        }

        var cumulative = 0;
        var first = 0;
        var found = false;
        var map = new float[256];
        for (var i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (!found && cumulative > 0)
            {
                first = cumulative;
                found = true;
            }

            map[i] = cumulative == first
                ? 0
                : (float)Math.Clamp((cumulative - first) / (double)(resized.Length - first), 0, 1);
        }

        var normalized = new float[resized.Length];
        for (var i = 0; i < resized.Length; i++)
        {
            normalized[i] = map[resized[i]];
        }

        return normalized;
    }

    public static byte[] ToPreviewBytes(ReadOnlySpan<float> input)
    {
        var pixels = new byte[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            pixels[i] = (byte)Math.Clamp(Math.Round(input[i] * 255), 0, 255);
        }

        return pixels;
    }
}
