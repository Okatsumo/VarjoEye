using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using VarjoEye.App.Features.Dashboard;
using VarjoEye.App.Features.Logs;
using VarjoEye.App.Features.Settings;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App.Services;

public sealed class NavigationService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, Page> _pages = new(StringComparer.OrdinalIgnoreCase);
    private Frame? _frame;

    public NavigationService(IServiceProvider services) => _services = services;

    public string CurrentTag { get; private set; } = "tracking";

    public event EventHandler<string>? Navigated;

    public void Initialize(Frame frame)
    {
        _frame = frame;
        Navigate("tracking");
    }

    public void Navigate(string tag)
    {
        if (_frame is null)
        {
            return;
        }

        if (!_pages.TryGetValue(tag, out var page))
        {
            page = Create(tag);
            _pages[tag] = page;
        }

        if (ReferenceEquals(_frame.Content, page))
        {
            CurrentTag = tag;
            Navigated?.Invoke(this, tag);
            return;
        }

        if (_frame.Content is DashboardPage leaving)
        {
            leaving.ViewModel.OnNavigatedFrom();
        }

        _frame.Content = page;
        CurrentTag = tag;

        if (page is DashboardPage dashboard)
        {
            dashboard.ViewModel.OnNavigatedTo();
        }

        Navigated?.Invoke(this, tag);
    }

    private Page Create(string tag) => tag switch
    {
        "settings" => new SettingsPage(_services.GetRequiredService<SettingsViewModel>()),
        "logs" => new LogsPage(_services.GetRequiredService<LogsViewModel>()),
        _ => new DashboardPage(_services.GetRequiredService<DashboardViewModel>())
    };
}
