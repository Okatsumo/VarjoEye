using VarjoEye.Core.Contracts;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeBaballoniaTrainer : IBaballoniaTrainer
{
    public string StatusText { get; set; } = "Last-layer training idle";

    public bool IsBusy { get; set; }

    public bool BeginResult { get; set; } = true;

    public int BeginCount { get; private set; }

    public event EventHandler? Changed;

    public bool TryBegin()
    {
        BeginCount++;
        if (!BeginResult)
        {
            return false;
        }

        IsBusy = true;
        StatusText = "Training: look at the center";
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
    }
}
