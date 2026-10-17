namespace VarjoEye.Core.Models;

public sealed record OperationResult(bool IsSuccess, string? ErrorMessage = null)
{
    public static OperationResult Success() => new(true);

    public static OperationResult Failure(string message) => new(false, message);
}
