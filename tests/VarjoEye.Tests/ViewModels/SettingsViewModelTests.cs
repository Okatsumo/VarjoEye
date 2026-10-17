using Xunit;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void ChangingSettings_PersistsInMemoryService()
    {
        var store = new InMemoryAppSettingsService();
        var first = new SettingsViewModel(store);
        first.GazeSmoothing = 80;
        first.BlinkSensitivity = 20;
        first.RawTrackingMode = true;
        first.InferenceDeviceIndex = 2;
        first.SendTrackingToOsc = false;
        first.OscHost = "10.0.0.8";
        first.OscPort = 9100;
        first.OutputFrequencyIndex = 1;
        first.ThemeModeIndex = 1;

        var second = new SettingsViewModel(store);
        Assert.Equal(80, second.GazeSmoothing);
        Assert.Equal(20, second.BlinkSensitivity);
        Assert.True(second.RawTrackingMode);
        Assert.Equal(2, second.InferenceDeviceIndex);
        Assert.False(second.SendTrackingToOsc);
        Assert.Equal("10.0.0.8", second.OscHost);
        Assert.Equal(9100, second.OscPort);
        Assert.Equal(1, second.OutputFrequencyIndex);
        Assert.Equal("80%", second.GazeSmoothingLabel);
        Assert.Equal(1, second.ThemeModeIndex);
        Assert.Equal(AppThemeMode.Light, store.Current.ThemeMode);
    }

    [Fact]
    public void ThemeMode_NotifiesSettingsService()
    {
        var store = new InMemoryAppSettingsService();
        var raised = 0;
        store.Changed += (_, _) => raised++;
        var vm = new SettingsViewModel(store);
        vm.ThemeModeIndex = 2;
        Assert.Equal(AppThemeMode.Auto, store.Current.ThemeMode);
        Assert.True(raised > 0);
    }
}
