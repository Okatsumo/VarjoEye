using VarjoEye.Core.Contracts;

namespace VarjoEye.Tests;

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
