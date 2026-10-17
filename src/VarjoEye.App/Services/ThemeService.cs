using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using Windows.UI;
using WinRT.Interop;

namespace VarjoEye.App.Services;

public sealed class ThemeService
{
    private readonly IAppSettingsService _settings;

    public ThemeService(IAppSettingsService settings)
    {
        _settings = settings;
        _settings.Changed += (_, _) => Apply();
    }

    public void Apply()
    {
        var window = App.MainWindow;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyCore);
    }

    private void ApplyCore()
    {
        var window = App.MainWindow;
        if (window is null)
        {
            return;
        }

        var requested = _settings.Current.ThemeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        ApplyToTree(window.Content, requested);

        var dark = requested switch
        {
            ElementTheme.Light => false,
            ElementTheme.Dark => true,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
        };

        UpdateCaptionButtons(window, dark);
    }

    private static void ApplyToTree(UIElement? content, ElementTheme requested)
    {
        if (content is not FrameworkElement root)
        {
            return;
        }

        if (root.RequestedTheme == requested)
        {
            return;
        }

        try
        {
            root.RequestedTheme = requested;
        }
        catch (Exception)
        {
            root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (root.RequestedTheme != requested)
                {
                    root.RequestedTheme = requested;
                }
            });
        }
    }

    private static void UpdateCaptionButtons(Window window, bool dark)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;

        var foreground = dark ? Colors.White : Colors.Black;
        var inactiveForeground = dark
            ? Color.FromArgb(255, 157, 181, 210)
            : Color.FromArgb(255, 91, 107, 128);

        var hoverBackground = Color.FromArgb(24, 0, 0, 0);
        var pressedBackground = Color.FromArgb(40, 0, 0, 0);

        if (dark)
        {
            hoverBackground = Color.FromArgb(24, 255, 255, 255);
            pressedBackground = Color.FromArgb(40, 255, 255, 255);
        }

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;

        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }
}
