using Xunit;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.ViewModels;
using VarjoEye.Tests.Fakes;

namespace VarjoEye.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void ChangingSettings_PersistsInMemoryService()
    {
        var store = new InMemoryAppSettingsService();
        var first = new SettingsViewModel(store, new FakeBaballoniaTrackingService());
        first.TrackingDiagnosticsEnabled = false;
        first.GazeSmoothing = 80;
        first.BaballoniaSmoothingEnabled = false;
        first.BlinkSensitivity = 20;
        first.RawTrackingMode = true;
        first.InferenceDeviceIndex = 1;
        first.SendTrackingToOsc = false;
        first.OscHost = "10.0.0.8";
        first.OscPort = 9100;
        first.OutputFrequencyIndex = 1;
        first.ThemeModeIndex = 1;

        var second = new SettingsViewModel(store, new FakeBaballoniaTrackingService());
        Assert.False(second.TrackingDiagnosticsEnabled);
        Assert.Equal(80, second.GazeSmoothing);
        Assert.False(second.BaballoniaSmoothingEnabled);
        Assert.Equal(20, second.BlinkSensitivity);
        Assert.True(second.RawTrackingMode);
        Assert.Equal(1, second.InferenceDeviceIndex);
        Assert.False(second.SendTrackingToOsc);
        Assert.Equal("10.0.0.8", second.OscHost);
        Assert.Equal(9100, second.OscPort);
        Assert.Equal(1, second.OutputFrequencyIndex);
        Assert.Equal("80%", second.GazeSmoothingLabel);
        Assert.Equal(1, second.ThemeModeIndex);
        Assert.Equal(AppThemeMode.Light, store.Current.ThemeMode);
    }

    [Fact]
    public void Smoothing_NotifiesAndDoesNotReloadModel()
    {
        var store = new InMemoryAppSettingsService();
        var babble = new FakeBaballoniaTrackingService();
        var vm = new SettingsViewModel(store, babble);
        var raised = 0;
        store.Changed += (_, _) => raised++;
        vm.BaballoniaSmoothingEnabled = false;
        vm.GazeSmoothing = 10;
        Assert.Equal(2, raised);
        Assert.False(store.Current.BaballoniaSmoothingEnabled);
        Assert.Equal(10, store.Current.GazeSmoothing);
    }

    [Fact]
    public void ThemeMode_NotifiesSettingsService()
    {
        var store = new InMemoryAppSettingsService();
        var raised = 0;
        store.Changed += (_, _) => raised++;
        var vm = new SettingsViewModel(store, new FakeBaballoniaTrackingService());
        vm.ThemeModeIndex = 2;
        Assert.Equal(AppThemeMode.Auto, store.Current.ThemeMode);
        Assert.True(raised > 0);
    }
}
