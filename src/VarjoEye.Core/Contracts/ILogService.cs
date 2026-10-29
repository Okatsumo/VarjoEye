using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface ILogService
{
    IReadOnlyList<LogEntry> Entries { get; }

    event EventHandler? Changed;

    void Add(LogLevel level, string message);

    void Clear();

    string? DirectoryPath { get; }

    string? CurrentFilePath { get; }

}
