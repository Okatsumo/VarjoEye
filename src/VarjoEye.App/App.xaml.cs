using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VarjoEye.App.Services;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
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
        _services.GetRequiredService<TrackingDiagnosticsService>().Start();
        _services.GetRequiredService<ITrackingDataProvider>().Start();
        _services.GetRequiredService<IBaballoniaTrackingService>().Start();
        MainWindow.Closed += OnMainWindowClosed;
        MainWindow.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _services?.GetService<TrackingDiagnosticsService>()?.Dispose();

        if (_services?.GetService<IBaballoniaTrainer>() is { } trainer)
        {
            trainer.Dispose();
        }

        if (_services?.GetService<IBaballoniaCalibrator>() is { } calibrator)
        {
            calibrator.Dispose();
        }

        if (_services?.GetService<ICalibrationOverlay>() is { } overlay)
        {
            overlay.Dispose();
        }

        if (_services?.GetService<IBaballoniaTrackingService>() is { } baballonia)
        {
            baballonia.Dispose();
        }

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
        services.AddSingleton<TrackingDiagnosticsService>();
        services.AddSingleton<IFileLauncher, WindowsShellService>();
        services.AddSingleton(BaballoniaModelPaths.Create(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        services.AddSingleton<IVarjoRuntime, VarjoRuntime>();
        services.AddSingleton<ITrackingDataProvider, VarjoTrackingDataProvider>();
        services.AddSingleton<BaballoniaCalibrationStore>();
        services.AddSingleton<IBaballoniaTrackingService, BaballoniaTrackingService>();
        services.AddSingleton<ICalibrationOverlay, CalibrationOverlay>();
        services.AddSingleton<IBaballoniaCalibrator, BaballoniaCalibrator>();
        services.AddSingleton<IBaballoniaTrainer, BaballoniaTrainer>();
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
