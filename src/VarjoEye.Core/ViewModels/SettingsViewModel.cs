using CommunityToolkit.Mvvm.ComponentModel;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;
    private readonly IBaballoniaTrackingService _baballonia;
    private readonly AppSettings _settings;
    private bool _syncingDevices;

    public SettingsViewModel(IAppSettingsService settings, IBaballoniaTrackingService baballonia)
    {
        _settingsService = settings;
        _baballonia = baballonia;
        _settings = settings.Current;
        _trackingDiagnosticsEnabled = _settings.TrackingDiagnosticsEnabled;
        _outputFrequencyIndex = _settings.OutputFrequencyIndex;
        _baballoniaSmoothingEnabled = _settings.BaballoniaSmoothingEnabled;
        _gazeSmoothing = _settings.GazeSmoothing;
        _blinkSensitivity = _settings.BlinkSensitivity;
        _rawTrackingMode = _settings.RawTrackingMode;
        _sendTrackingToOsc = _settings.SendTrackingToOsc;
        _oscHost = _settings.OscHost;
        _oscPort = _settings.OscPort;
        _themeModeIndex = (int)_settings.ThemeMode;
        ApplyStoredDevice();
        _inferenceDevices = DeviceNames();
        _inferenceDeviceIndex = SelectedIndex();
        _baballonia.DevicesChanged += (_, _) => RefreshDevices();
    }

    public IReadOnlyList<string> OutputFrequencies { get; } = ["Auto (maximum)", "100 Hz", "200 Hz"];

    public IReadOnlyList<string> ThemeModes { get; } = ["Dark", "Light", "Use Windows setting"];

    [ObservableProperty] private IReadOnlyList<string> _inferenceDevices;
    [ObservableProperty] private int _outputFrequencyIndex;
    [ObservableProperty] private double _gazeSmoothing;
    [ObservableProperty] private bool _baballoniaSmoothingEnabled;
    [ObservableProperty] private double _blinkSensitivity;
    [ObservableProperty] private bool _rawTrackingMode;
    [ObservableProperty] private bool _trackingDiagnosticsEnabled;
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

    partial void OnBaballoniaSmoothingEnabledChanged(bool value)
    {
        _settings.BaballoniaSmoothingEnabled = value;
        _settingsService.NotifyChanged();
    }

    partial void OnGazeSmoothingChanged(double value)
    {
        _settings.GazeSmoothing = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 35;
        _settingsService.NotifyChanged();
        OnPropertyChanged(nameof(GazeSmoothingLabel));
    }

    partial void OnBlinkSensitivityChanged(double value)
    {
        _settings.BlinkSensitivity = value;
        OnPropertyChanged(nameof(BlinkSensitivityLabel));
    }

    partial void OnTrackingDiagnosticsEnabledChanged(bool value)
    {
        _settings.TrackingDiagnosticsEnabled = value;
        _settingsService.NotifyChanged();
    }

    partial void OnRawTrackingModeChanged(bool value)
    {
        _settings.RawTrackingMode = value;
        _settingsService.NotifyChanged();
    }

    partial void OnInferenceDeviceIndexChanged(int value)
    {
        if (_syncingDevices || value < 0 || value >= _baballonia.AvailableDevices.Count)
        {
            return;
        }

        _settings.InferenceDeviceIndex = value;
        var device = _baballonia.AvailableDevices[value];
        if (device != _baballonia.SelectedDevice)
        {
            _baballonia.SelectDevice(device);
        }
    }

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

    private void RefreshDevices()
    {
        _syncingDevices = true;
        InferenceDevices = DeviceNames();
        InferenceDeviceIndex = SelectedIndex();
        _syncingDevices = false;
    }

    private void ApplyStoredDevice()
    {
        var index = _settings.InferenceDeviceIndex;
        if (index < 0 || index >= _baballonia.AvailableDevices.Count)
        {
            return;
        }

        var device = _baballonia.AvailableDevices[index];
        if (device != _baballonia.SelectedDevice)
        {
            _baballonia.SelectDevice(device);
        }
    }

    private IReadOnlyList<string> DeviceNames() =>
        [.. _baballonia.AvailableDevices.Select(device => device.Name)];

    private int SelectedIndex()
    {
        var index = _baballonia.AvailableDevices.ToList().IndexOf(_baballonia.SelectedDevice);
        return index < 0 ? 0 : index;
    }
}
