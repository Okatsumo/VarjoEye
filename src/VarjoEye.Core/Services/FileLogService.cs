using System.Diagnostics;
using System.Text;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services;

public sealed class FileLogService : ILogService, IDisposable
{
    public const string AppFolderName = "VarjoEye";
    public const string LogsFolderName = "logs";

    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLogService()
        : this(DefaultDirectory())
    {
    }

    public FileLogService(string directory)
    {
        Directory.CreateDirectory(directory);
        DirectoryPath = Path.GetFullPath(directory);
        CurrentFilePath = NextFilePath(DirectoryPath);
        _writer = new StreamWriter(
            new FileStream(CurrentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public string DirectoryPath { get; }

    public string CurrentFilePath { get; }

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

    public void Add(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _entries.Insert(0, entry);
            _writer.WriteLine(Format(entry));
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

    public OperationResult OpenDirectory() => OpenPath(DirectoryPath);

    public OperationResult OpenCurrentFile() => OpenPath(CurrentFilePath);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName,
            LogsFolderName);

    private static string NextFilePath(string directory)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(directory, $"varjoeye-{stamp}.log");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"varjoeye-{stamp}-{suffix}.log");
            suffix++;
        }

        return path;
    }

    private static string Format(LogEntry entry) =>
        $"{entry.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}";

    private static OperationResult OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return OperationResult.Failure("Log path was not found.");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(exception.Message);
        }
    }
}
