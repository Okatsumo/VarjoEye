using System.Diagnostics;
using VarjoEye.Core.Services.Varjo;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class HighResolutionPollTimerTests
{
    [Fact]
    public async Task Cancellation_InterruptsLongWait()
    {
        using var stop = new CancellationTokenSource();
        using var timer = new HighResolutionPollTimer(TimeSpan.FromSeconds(30), stop.Token);
        var wait = Task.Factory.StartNew(timer.WaitForNextTick, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        stop.Cancel();
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Timer_DeliversTicksWithoutGlobalResolutionRequest()
    {
        using var stop = new CancellationTokenSource();
        using var timer = new HighResolutionPollTimer(TimeSpan.FromMilliseconds(5), stop.Token);
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 10; i++) Assert.True(timer.WaitForNextTick());
        // Check semantics, not machine-dependent real-time performance.
        Assert.True(Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 40);
        stop.Cancel();
        Assert.False(timer.WaitForNextTick());
    }
}
