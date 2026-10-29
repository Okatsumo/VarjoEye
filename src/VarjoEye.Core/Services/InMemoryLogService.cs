using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services;

public sealed class InMemoryLogService : ILogService
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public event EventHandler? Changed;

    public string? DirectoryPath => null;

    public string? CurrentFilePath => null;

    public void Add(LogLevel level, string message)
    {
        lock (_gate)
        {
            _entries.Insert(0, new LogEntry(DateTimeOffset.Now, level, message));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
