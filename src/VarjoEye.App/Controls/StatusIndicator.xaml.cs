using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VarjoEye.Core.Models;
using Windows.UI;

namespace VarjoEye.App.Controls;

public sealed partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusIndicator),
            new PropertyMetadata("—", OnChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(ConnectionStatus), typeof(StatusIndicator),
            new PropertyMetadata(ConnectionStatus.Disconnected, OnChanged));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(StatusIndicator),
            new PropertyMetadata("auto", OnChanged));

    public StatusIndicator()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ConnectionStatus Status
    {
        get => (ConnectionStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusIndicator indicator)
        {
            indicator.Apply();
        }
    }

    private void Apply()
    {
        if (LabelText is null || Dot is null)
        {
            return;
        }

        LabelText.Text = Text;
        var color = ResolveColor();
        Dot.Fill = new SolidColorBrush(color);
        LabelText.Foreground = new SolidColorBrush(color);
    }

    private Color ResolveColor()
    {
        if (string.Equals(Tone, "success", StringComparison.OrdinalIgnoreCase) ||
            Text is "Active" or "Ready" or "Connected" or "Good")
        {
            return Color.FromArgb(255, 0x18, 0xD5, 0x7B);
        }

        if (string.Equals(Tone, "warning", StringComparison.OrdinalIgnoreCase) ||
            Text is "Connecting" or "Waiting" or "Starting")
        {
            return Color.FromArgb(255, 0xF4, 0xB8, 0x4B);
        }

        if (string.Equals(Tone, "error", StringComparison.OrdinalIgnoreCase) ||
            Status == ConnectionStatus.Error || Text is "Error" or "Unavailable")
        {
            return Color.FromArgb(255, 0xF0, 0x52, 0x52);
        }

        return Status switch
        {
            ConnectionStatus.Connected => Color.FromArgb(255, 0x18, 0xD5, 0x7B),
            ConnectionStatus.Connecting => Color.FromArgb(255, 0xF4, 0xB8, 0x4B),
            ConnectionStatus.Error => Color.FromArgb(255, 0xF0, 0x52, 0x52),
            _ => Color.FromArgb(255, 0x9D, 0xB5, 0xD2)
        };
    }
}
