namespace VarjoEye.Core.Services.Baballonia;

internal sealed class BaballoniaSmoothingFilter
{
    private readonly double[] _previous = new double[6];
    private readonly double[] _previousDerivative = new double[6];
    private long _previousCaptureTime;
    private double _strength = double.NaN;

    public void Reset() => _previousCaptureTime = 0;

    public void Apply(float[] values, long captureTime, bool enabled, double strength)
    {
        if (!enabled)
        {
            Reset();
            return;
        }

        strength = double.IsFinite(strength) ? Math.Clamp(strength, 0, 100) : 35;
        if (strength != _strength) Reset();
        _strength = strength;
        if (_previousCaptureTime <= 0 || captureTime <= _previousCaptureTime ||
            captureTime - _previousCaptureTime > 250_000_000)
        {
            for (var i = 0; i < 6; i++)
            {
                _previous[i] = values[i];
                _previousDerivative[i] = 0;
            }
            _previousCaptureTime = captureTime;
            return;
        }

        // Logarithmic cutoff: 0% = 10 Hz, 35% = legacy 0.5 Hz, 100% = 0.1 Hz.
        var minCutoff = strength <= 35
            ? 0.5 * Math.Pow(20, (35 - strength) / 35)
            : 0.5 * Math.Pow(0.2, (strength - 35) / 65);
        var dt = Math.Clamp((captureTime - _previousCaptureTime) / 1_000_000_000d, 0.001, 0.1);
        var derivativeAlpha = Alpha(1, dt);
        for (var i = 0; i < 6; i++)
        {
            var derivative = (values[i] - _previous[i]) / dt;
            var filteredDerivative = derivativeAlpha * derivative + (1 - derivativeAlpha) * _previousDerivative[i];
            var alpha = Alpha(minCutoff + 3 * Math.Abs(filteredDerivative), dt);
            var filtered = alpha * values[i] + (1 - alpha) * _previous[i];
            values[i] = (float)filtered;
            _previous[i] = filtered;
            _previousDerivative[i] = filteredDerivative;
        }
        _previousCaptureTime = captureTime;
    }

    private static double Alpha(double cutoff, double dt)
    {
        var rate = 2 * Math.PI * cutoff * dt;
        return rate / (rate + 1);
    }
}
