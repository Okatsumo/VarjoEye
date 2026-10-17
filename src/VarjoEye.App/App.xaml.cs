using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VarjoEye.App.Services;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Varjo;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App;

public partial class App : Application
{
    private IServiceProvider? _services;

    public App()
    {
        InitializeComponent();
    }

    public static IServiceProvider Services { get; private set; } = null!;

    public static MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _services = BuildServices();
        Services = _services;
        MainWindow = _services.GetRequiredService<MainWindow>();
        _services.GetRequiredService<ThemeService>().Apply();
        _services.GetRequiredService<ILogService>().Add(LogLevel.Information, "VarjoEye started.");
        _services.GetRequiredService<ITrackingDataProvider>().Start();
        MainWindow.Closed += OnMainWindowClosed;
        MainWindow.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_services?.GetService<ITrackingDataProvider>() is { } tracking)
        {
            tracking.Dispose();
        }

        if (_services?.GetService<ILogService>() is IDisposable logs)
        {
            logs.Dispose();
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUiDispatcher>(_ => new WinUiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
        services.AddSingleton<IAppSettingsService, InMemoryAppSettingsService>();
        services.AddSingleton<ILogService, FileLogService>();
        services.AddSingleton<IVarjoRuntime, VarjoRuntime>();
        services.AddSingleton<ITrackingDataProvider, VarjoTrackingDataProvider>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<Shell.ShellPage>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
