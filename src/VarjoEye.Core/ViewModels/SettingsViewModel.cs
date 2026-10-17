using CommunityToolkit.Mvvm.ComponentModel;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;
    private readonly AppSettings _settings;

    public SettingsViewModel(IAppSettingsService settings)
    {
        _settingsService = settings;
        _settings = settings.Current;
        _outputFrequencyIndex = _settings.OutputFrequencyIndex;
        _gazeSmoothing = _settings.GazeSmoothing;
        _blinkSensitivity = _settings.BlinkSensitivity;
        _rawTrackingMode = _settings.RawTrackingMode;
        _inferenceDeviceIndex = _settings.InferenceDeviceIndex;
        _sendTrackingToOsc = _settings.SendTrackingToOsc;
        _oscHost = _settings.OscHost;
        _oscPort = _settings.OscPort;
        _themeModeIndex = (int)_settings.ThemeMode;
    }

    public IReadOnlyList<string> OutputFrequencies { get; } = ["Auto (maximum)", "100 Hz", "200 Hz"];

    public IReadOnlyList<string> InferenceDevices { get; } = ["Auto", "NVIDIA GPU", "CPU"];

    public IReadOnlyList<string> ThemeModes { get; } = ["Dark", "Light", "Use Windows setting"];

    [ObservableProperty] private int _outputFrequencyIndex;
    [ObservableProperty] private double _gazeSmoothing;
    [ObservableProperty] private double _blinkSensitivity;
    [ObservableProperty] private bool _rawTrackingMode;
    [ObservableProperty] private int _inferenceDeviceIndex;
    [ObservableProperty] private bool _sendTrackingToOsc;
    [ObservableProperty] private string _oscHost;
    [ObservableProperty] private int _oscPort;
    [ObservableProperty] private int _themeModeIndex;

    public double OscPortValue
    {
        get => OscPort;
        set => OscPort = (int)Math.Clamp(value, 1, 65535);
    }

    public string GazeSmoothingLabel => $"{GazeSmoothing:0}%";
    public string BlinkSensitivityLabel => $"{BlinkSensitivity:0}%";

    partial void OnOutputFrequencyIndexChanged(int value)
    {
        _settings.OutputFrequencyIndex = value;
        _settingsService.NotifyChanged();
    }
    partial void OnGazeSmoothingChanged(double value)
    {
        _settings.GazeSmoothing = value;
        OnPropertyChanged(nameof(GazeSmoothingLabel));
    }

    partial void OnBlinkSensitivityChanged(double value)
    {
        _settings.BlinkSensitivity = value;
        OnPropertyChanged(nameof(BlinkSensitivityLabel));
    }

    partial void OnRawTrackingModeChanged(bool value)
    {
        _settings.RawTrackingMode = value;
        _settingsService.NotifyChanged();
    }
    partial void OnInferenceDeviceIndexChanged(int value) => _settings.InferenceDeviceIndex = value;
    partial void OnSendTrackingToOscChanged(bool value) => _settings.SendTrackingToOsc = value;
    partial void OnOscHostChanged(string value) => _settings.OscHost = value;
    partial void OnOscPortChanged(int value)
    {
        _settings.OscPort = value;
        OnPropertyChanged(nameof(OscPortValue));
    }

    partial void OnThemeModeIndexChanged(int value)
    {
        _settings.ThemeMode = (AppThemeMode)Math.Clamp(value, 0, 2);
        _settingsService.NotifyChanged();
    }
}
