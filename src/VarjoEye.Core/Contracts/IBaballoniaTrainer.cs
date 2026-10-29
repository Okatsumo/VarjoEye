namespace VarjoEye.Core.Contracts;

public interface IBaballoniaTrainer : IDisposable
{
    string StatusText { get; }

    bool IsBusy { get; }

    event EventHandler? Changed;

    bool TryBegin();
}
