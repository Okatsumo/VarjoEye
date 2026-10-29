using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal sealed class TrackingSampleSynchronizer
{
    private readonly Queue<GazeSample> _gaze = new();
    private readonly int _capacity;
    private readonly long _maxPairDeltaNanoseconds;

    public TrackingSampleSynchronizer(int capacity, long maxPairDeltaNanoseconds)
    {
        _capacity = capacity;
        _maxPairDeltaNanoseconds = maxPairDeltaNanoseconds;
    }

    public int Count => _gaze.Count;

    public GazeSample? Latest => _gaze.Count == 0 ? null : _gaze.Last();

    public long MaxPairDeltaNanoseconds => _maxPairDeltaNanoseconds;

    public void Push(GazeSample sample)
    {
        _gaze.Enqueue(sample);
        while (_gaze.Count > _capacity)
            _gaze.Dequeue();
    }

    public GazeSample? FindNearest(long captureTimeNanoseconds)
    {
        GazeSample? best = null;
        var bestDelta = long.MaxValue;
        foreach (var frame in _gaze)
        {
            var delta = Math.Abs(frame.CaptureTimeNanoseconds - captureTimeNanoseconds);
            if (delta >= bestDelta)
                continue;

            bestDelta = delta;
            best = frame;
        }

        return bestDelta <= _maxPairDeltaNanoseconds ? best : null;
    }

    public void Clear() => _gaze.Clear();
}
