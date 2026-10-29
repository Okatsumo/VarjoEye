using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public sealed class BaballoniaCalibrationStore
{
    private readonly object _gate = new();
    private BaballoniaCalibration? _current;

    public BaballoniaCalibration? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
        set
        {
            lock (_gate)
            {
                _current = value;
            }
        }
    }

    public BaballoniaEyeResult Map(BaballoniaEyeResult raw)
    {
        var calibration = Current;
        if (calibration is null)
        {
            return raw;
        }

        var left = raw.Left;
        var right = raw.Right;
        if (calibration.OutputHead is { } head &&
            (raw.SmoothedOutput ?? raw.RawOutput) is { } onnx)
        {
            var values = onnx.ToFloatArray();
            head.Apply(values);
            (left, right) = BaballoniaEyeExpression.FromRaw(values);
        }

        return raw with
        {
            Left = calibration.Left.Map(left),
            Right = calibration.Right.Map(right)
        };
    }
}
