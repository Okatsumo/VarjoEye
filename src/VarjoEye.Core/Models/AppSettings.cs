namespace VarjoEye.Core.Models;

public sealed class AppSettings
{
    public bool TrackingDiagnosticsEnabled { get; set; } = true;
    public int OutputFrequencyIndex { get; set; } = 2;
    public bool BaballoniaSmoothingEnabled { get; set; } = true;
    public double GazeSmoothing { get; set; } = 35;
    public double BlinkSensitivity { get; set; } = 50;
    public bool RawTrackingMode { get; set; }
    public int InferenceDeviceIndex { get; set; } = -1;
    public bool SendTrackingToOsc { get; set; } = true;
    public string OscHost { get; set; } = "127.0.0.1";
    public int OscPort { get; set; } = 9000;
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;
}
