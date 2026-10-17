using System.Runtime.InteropServices;

namespace VarjoEye.Core.Services.Varjo;

internal sealed class WindowsTimerResolution : IDisposable
{
    private const uint PeriodMilliseconds = 1;
    private readonly bool _active;

    private WindowsTimerResolution(bool active) => _active = active;

    public bool IsActive => _active;

    public static WindowsTimerResolution Request()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsTimerResolution(false);
        }

        return new WindowsTimerResolution(TimeBeginPeriod(PeriodMilliseconds) == 0);
    }

    public void Dispose()
    {
        if (_active)
        {
            TimeEndPeriod(PeriodMilliseconds);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", ExactSpelling = true)]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", ExactSpelling = true)]
    private static extern uint TimeEndPeriod(uint period);
}
