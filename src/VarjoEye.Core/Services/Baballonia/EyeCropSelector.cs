using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia.Sampling;

namespace VarjoEye.Core.Services.Baballonia;

/// <summary>Choose a fixed ROI from stable, forward-looking Varjo observations; never follow gaze.</summary>
internal sealed class EyeCropSelector
{
    private const int ObservationCount = 24;
    private readonly List<(int X, int Y)> _centers = new(ObservationCount);
    private long _lastFrame = -1;
    private int _width;
    private int _height;

    public (int X, int Y, int Size)? Crop { get; private set; }

    public bool Observe(EyeCameraFrame frame, GazeSample? gaze = null)
    {
        if (frame.Width != _width || frame.Height != _height || frame.FrameNumber < _lastFrame)
        {
            Crop = null;
            _centers.Clear();
            _width = frame.Width;
            _height = frame.Height;
        }
        if (Crop is not null || frame.FrameNumber == _lastFrame) return false;
        _lastFrame = frame.FrameNumber;
        if (frame.Width < 192 || frame.Height < 192 ||
            frame.Pixels.Length != (long)frame.Width * frame.Height) return false;

        if (!CanUseGaze(frame, gaze))
        {
            _centers.Clear();
            return false;
        }

        var found = FindPupil(frame);
        if (found is not { } center)
        {
            _centers.Clear();
            return false;
        }
        _centers.Add(center);
        if (_centers.Count < ObservationCount) return false;
        var xs = _centers.Select(p => p.X).Order().ToArray();
        var ys = _centers.Select(p => p.Y).Order().ToArray();
        var centerX = xs[xs.Length / 2];
        var centerY = ys[ys.Length / 2];
        if (_centers.Count(p => Math.Abs(p.X - centerX) + Math.Abs(p.Y - centerY) > 24) > ObservationCount / 4)
        {
            _centers.Clear();
            return false;
        }

        var size = Math.Min(352, Math.Min(frame.Width, frame.Height));
        var x = Math.Clamp(centerX - size / 2, 0, frame.Width - size);
        var y = Math.Clamp(centerY - size / 2, 0, frame.Height - size);
        // Refuse a clipped pupil instead of silently pinning a bad ROI against the frame edge.
        var margin = size / 4;
        if (centerX - x < margin || x + size - centerX < margin ||
            centerY - y < margin || y + size - centerY < margin)
        {
            _centers.Clear();
            return false;
        }
        Crop = (x, y, size);
        return true;
    }

    private static bool CanUseGaze(EyeCameraFrame frame, GazeSample? gaze)
    {
        if (gaze is null || frame.CaptureTimeNanoseconds <= 0 || gaze.CaptureTimeNanoseconds <= 0 ||
            Math.Abs(frame.CaptureTimeNanoseconds - gaze.CaptureTimeNanoseconds) > 12_000_000 ||
            TrackingSampleValidator.GazeFrame(gaze.Status, gaze.Stability) != SampleRejectionReason.None ||
            !TrackingSampleValidator.IsTrackedEye(frame.IsLeft ? gaze.LeftStatus : gaze.RightStatus)) return false;
        var eye = frame.IsLeft ? gaze.Left : gaze.Right;
        var ray = eye.GazeDirection;
        // A ray is a direction, not a camera pixel. Use it only to validate the neutral pose (~12 degrees).
        return double.IsFinite(eye.Openness) && eye.Openness >= 0.65 &&
            double.IsFinite(ray.X) && double.IsFinite(ray.Y) && double.IsFinite(ray.Z) && ray.Z > 0.5 &&
            Math.Abs(ray.X / ray.Z) <= 0.22 && Math.Abs(ray.Y / ray.Z) <= 0.22;
    }

    private static readonly int[] Radii = [12, 20, 32, 48];

    private static readonly (double X, double Y)[] Ring =
    [ (1, 0), (0.707, 0.707), (0, 1), (-0.707, 0.707), (-1, 0), (-0.707, -0.707), (0, -1), (0.707, -0.707) ];

    private static (int X, int Y)? FindPupil(EyeCameraFrame frame)
    {
        var bestScore = 12d;
        (int X, int Y)? best = null;
        // Require bright surroundings in seven directions. A lens edge or eyelid has a dark half-plane,
        // unlike a compact dark pupil. Multiple radii support different camera resolutions/pupil sizes.
        foreach (var radius in Radii)
        {
            for (var y = Math.Max(radius + 4, frame.Height / 5); y < Math.Min(frame.Height - radius - 4, frame.Height * 4 / 5); y += 6)
            for (var x = Math.Max(radius + 4, frame.Width / 7); x < Math.Min(frame.Width - radius - 4, frame.Width * 6 / 7); x += 6)
            {
                var inner = Mean(frame, x, y, Math.Max(3, radius / 3));
                if (inner > 130) continue;
                var bright = 0;
                var sum = 0d;
                foreach (var point in Ring)
                {
                    var outer = Mean(frame, x + (int)(point.X * radius), y + (int)(point.Y * radius), 3);
                    if (outer - inner >= 12) bright++;
                    sum += outer;
                }
                var score = sum / Ring.Length - inner;
                if (bright >= 7 && score > bestScore)
                {
                    bestScore = score;
                    best = (x, y);
                }
            }
        }
        return best;
    }

    private static double Mean(EyeCameraFrame frame, int x, int y, int radius)
    {
        var sum = 0;
        var count = 0;
        for (var row = y - radius; row <= y + radius; row += 2)
        for (var col = x - radius; col <= x + radius; col += 2)
        {
            sum += frame.Pixels[row * frame.Width + col];
            count++;
        }
        return sum / (double)count;
    }
}
