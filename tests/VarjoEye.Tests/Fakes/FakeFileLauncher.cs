using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeFileLauncher : IFileLauncher
{
    public string? LastPath { get; private set; }
    public OperationResult Result { get; set; } = OperationResult.Success();
    public OperationResult Open(string? path) { LastPath = path; return Result; }
}
