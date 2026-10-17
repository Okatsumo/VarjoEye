using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VarjoEye.App.Services;
using VarjoEye.App.Shell;
using Windows.Graphics;
using WinRT.Interop;

namespace VarjoEye.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(NavigationService navigation, ShellPage shell)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = MicaController.IsSupported()
            ? new MicaBackdrop { Kind = MicaKind.Base }
            : new DesktopAcrylicBackdrop();

        Title = "VarjoEye";
        ConfigureWindow();
        RootFrame.Content = shell;
        navigation.Initialize(shell.ContentFrame);
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon("Assets/Icons/app.ico");
        appWindow.Resize(new SizeInt32(1536, 1024));
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        var presenter = appWindow.Presenter as OverlappedPresenter;
        presenter?.SetBorderAndTitleBar(true, true);
        presenter?.PreferredMinimumWidth = 1100;
        presenter?.PreferredMinimumHeight = 760;
    }
}
