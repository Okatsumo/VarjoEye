using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VarjoEye.Core.Services.Varjo;

/// <summary>Cancelable one-shot high-resolution waits, scheduled against a monotonic clock.</summary>
internal sealed class HighResolutionPollTimer : IDisposable
{
    private readonly TimerHandle _timer;
    private readonly WaitHandle[] _waits;
    private readonly long _periodTicks;
    private long _next;

    public HighResolutionPollTimer(TimeSpan period, CancellationToken cancellationToken)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x1F0003);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Cannot create high-resolution gaze poll timer.");
        }
        _timer = new TimerHandle(handle);
        _waits = [cancellationToken.WaitHandle, _timer];
        _periodTicks = Math.Max(1, (long)(period.TotalSeconds * Stopwatch.Frequency));
        _next = Stopwatch.GetTimestamp() + _periodTicks;
    }

    public bool WaitForNextTick()
    {
        var now = Stopwatch.GetTimestamp();
        // Skip overdue deadlines; never busy-spin or replay a burst of stale ticks.
        if (now >= _next) _next = now + _periodTicks;
        var due = -Math.Max(1, (long)((_next - now) * (10_000_000d / Stopwatch.Frequency)));
        if (!SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (WaitHandle.WaitAny(_waits) == 0) return false;
        _next += _periodTicks;
        return true;
    }

    public void Dispose() => _timer.Dispose();

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completion, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
