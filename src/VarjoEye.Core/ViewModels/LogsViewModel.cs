using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.ViewModels;

public partial class LogsViewModel : ObservableObject, IDisposable
{
    private readonly ILogService _logs;
    private readonly IUiDispatcher _dispatcher;

    public LogsViewModel(ILogService logs, IUiDispatcher dispatcher)
    {
        _logs = logs;
        _dispatcher = dispatcher;
        Refresh();
        _logs.Changed += OnChanged;
    }

    public ObservableCollection<LogEntry> VisibleEntries { get; } = [];

    public IReadOnlyList<string> LevelFilters { get; } = ["All", "Debug", "Information", "Warning", "Error"];

    [ObservableProperty]
    private int _selectedFilterIndex;

    [RelayCommand]
    private void Clear() => _logs.Clear();

    [RelayCommand]
    private void OpenFolder()
    {
        var result = _logs.OpenDirectory();
        if (!result.IsSuccess)
        {
            _logs.Add(LogLevel.Warning, result.ErrorMessage ?? "Could not open the logs folder.");
        }
    }

    [RelayCommand]
    private void OpenLatestFile()
    {
        var result = _logs.OpenCurrentFile();
        if (!result.IsSuccess)
        {
            _logs.Add(LogLevel.Warning, result.ErrorMessage ?? "Could not open the latest log file.");
        }
    }

    partial void OnSelectedFilterIndexChanged(int value) => Refresh();

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        VisibleEntries.Clear();
        IEnumerable<LogEntry> items = _logs.Entries;
        if (SelectedFilterIndex > 0 && Enum.TryParse<LogLevel>(LevelFilters[SelectedFilterIndex], out var level))
        {
            items = items.Where(entry => entry.Level == level);
        }

        foreach (var entry in items)
        {
            VisibleEntries.Add(entry);
        }
    }

    public void Dispose() => _logs.Changed -= OnChanged;
}
