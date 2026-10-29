namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal sealed class SampleRateLimiter
{
    private readonly long _minSpacingNanoseconds;
    private long _lastLeftCapture;
    private long _lastRightCapture;

    public SampleRateLimiter(long minSpacingNanoseconds) =>
        _minSpacingNanoseconds = minSpacingNanoseconds;

    public bool TryAccept(bool left, long captureTimeNanoseconds)
    {
        if (_minSpacingNanoseconds <= 0)
            return true;

        ref var last = ref left ? ref _lastLeftCapture : ref _lastRightCapture;
        if (last > 0 && captureTimeNanoseconds - last < _minSpacingNanoseconds)
            return false;

        last = captureTimeNanoseconds;
        return true;
    }

    public bool WouldAccept(bool left, long captureTimeNanoseconds)
    {
        if (_minSpacingNanoseconds <= 0)
            return true;

        var last = left ? _lastLeftCapture : _lastRightCapture;
        return last <= 0 || captureTimeNanoseconds - last >= _minSpacingNanoseconds;
    }

    public void Mark(bool left, long captureTimeNanoseconds)
    {
        if (left)
            _lastLeftCapture = captureTimeNanoseconds;
        else
            _lastRightCapture = captureTimeNanoseconds;
    }

    public void Reset()
    {
        _lastLeftCapture = 0;
        _lastRightCapture = 0;
    }
}
