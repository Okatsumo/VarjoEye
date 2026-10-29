using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;

namespace VarjoEye.Core.Contracts;

public interface IBaballoniaCalibrator : IDisposable
{
    string StatusText { get; }

    bool IsCollecting { get; }

    BaballoniaCalibration? Current { get; }

    event EventHandler? Changed;

    void BeginCollection();

    BaballoniaEyeResult Map(BaballoniaEyeResult raw);
}
