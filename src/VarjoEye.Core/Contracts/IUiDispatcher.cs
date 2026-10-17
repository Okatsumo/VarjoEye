namespace VarjoEye.Core.Contracts;

public interface IUiDispatcher
{
    void Post(Action action);
}
