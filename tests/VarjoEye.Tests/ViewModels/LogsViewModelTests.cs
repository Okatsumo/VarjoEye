using Xunit;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.Tests.ViewModels;

public sealed class LogsViewModelTests
{
    [Fact]
    public void Clear_RemovesEntries()
    {
        var logs = new InMemoryLogService();
        logs.Add(LogLevel.Information, "started");
        var vm = new LogsViewModel(logs, new ImmediateDispatcher(), new VarjoEye.Tests.Fakes.FakeFileLauncher());
        Assert.NotEmpty(vm.VisibleEntries);
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.VisibleEntries);
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public void Filter_ShowsOnlySelectedLevel()
    {
        var logs = new InMemoryLogService();
        logs.Clear();
        logs.Add(LogLevel.Debug, "d");
        logs.Add(LogLevel.Error, "e");
        var vm = new LogsViewModel(logs, new ImmediateDispatcher(), new VarjoEye.Tests.Fakes.FakeFileLauncher());
        vm.SelectedFilterIndex = Array.IndexOf(vm.LevelFilters.ToArray(), "Error");
        Assert.Single(vm.VisibleEntries);
        Assert.Equal(LogLevel.Error, vm.VisibleEntries[0].Level);
    }

    [Fact]
    public void OpenLatestFile_WithoutPersistence_AddsWarning()
    {
        var logs = new InMemoryLogService();
        var vm = new LogsViewModel(logs, new ImmediateDispatcher(), new VarjoEye.Tests.Fakes.FakeFileLauncher());
        vm.OpenLatestFileCommand.Execute(null);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not persisted"));
    }
}
