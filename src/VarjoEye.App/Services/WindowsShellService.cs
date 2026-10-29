using System.Diagnostics;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.App.Services;

public sealed class WindowsShellService : IFileLauncher
{
    private readonly Action<ProcessStartInfo> _start;

    public WindowsShellService() : this(info => { Process.Start(info); }) { }

    internal WindowsShellService(Action<ProcessStartInfo> start) => _start = start;

    public OperationResult Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            return OperationResult.Failure("Log path was not found.");
        try
        {
            _start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(exception.Message);
        }
    }
}
