namespace VarjoEye.Core.Models;

public sealed class AppSettings
{
    public int OutputFrequencyIndex { get; set; } = 2;
    public double GazeSmoothing { get; set; } = 35;
    public double BlinkSensitivity { get; set; } = 50;
    public bool RawTrackingMode { get; set; }
    public int InferenceDeviceIndex { get; set; }
    public bool SendTrackingToOsc { get; set; } = true;
    public string OscHost { get; set; } = "127.0.0.1";
    public int OscPort { get; set; } = 9000;
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;
}
