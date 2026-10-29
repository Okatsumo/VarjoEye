using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeBaballoniaCalibrator : IBaballoniaCalibrator
{
    public string StatusText { get; set; } = "Waiting for Varjo calibration";

    public bool IsCollecting { get; set; }

    public BaballoniaCalibration? Current { get; set; }

    public int BeginCount { get; private set; }

    public event EventHandler? Changed;

    public void BeginCollection()
    {
        BeginCount++;
        IsCollecting = true;
        StatusText = "Waiting for Varjo calibration";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public BaballoniaEyeResult Map(BaballoniaEyeResult raw) => raw;

    public void Dispose()
    {
    }
}
