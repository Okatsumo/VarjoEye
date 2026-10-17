using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VarjoEye.Core.Models;
using Windows.UI;

namespace VarjoEye.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public sealed class ConnectionBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ConnectionStatus s ? s : ConnectionStatus.Disconnected;
        var color = status switch
        {
            ConnectionStatus.Connected => Color.FromArgb(255, 0x18, 0xD5, 0x7B),
            ConnectionStatus.Connecting => Color.FromArgb(255, 0xF4, 0xB8, 0x4B),
            ConnectionStatus.Error => Color.FromArgb(255, 0xF0, 0x52, 0x52),
            _ => Color.FromArgb(255, 0x9D, 0xB5, 0xD2)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class LogLevelBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var level = value is LogLevel l ? l : LogLevel.Information;
        var color = level switch
        {
            LogLevel.Debug => Color.FromArgb(255, 0x9D, 0xB5, 0xD2),
            LogLevel.Information => Color.FromArgb(255, 0x34, 0x7E, 0xFF),
            LogLevel.Warning => Color.FromArgb(255, 0xF4, 0xB8, 0x4B),
            LogLevel.Error => Color.FromArgb(255, 0xF0, 0x52, 0x52),
            _ => Color.FromArgb(255, 0xF4, 0xF7, 0xFC)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class HeadsetImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = HeadsetCatalog.Unknown.ImageAssetKey;
        }

        return new BitmapImage(new Uri($"ms-appx:///Assets/Images/{key}"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public static class MetricFormat
{
    public static string Number(bool hasData, double value, string format) =>
        hasData ? value.ToString(format) : "—";

    public static string Vector(bool hasData, Vector3Value value) =>
        hasData ? value.Display : "—";

    public static string Text(bool hasData, string value) =>
        hasData && !string.IsNullOrWhiteSpace(value) ? value : "—";

    public static string Percent(double value) => $"{value:0}%";
}
