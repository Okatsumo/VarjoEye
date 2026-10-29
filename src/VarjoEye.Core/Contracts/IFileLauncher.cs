using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface IFileLauncher
{
    OperationResult Open(string? path);
}
