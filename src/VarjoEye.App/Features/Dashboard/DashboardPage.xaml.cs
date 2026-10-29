using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using VarjoEye.App.Services;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App.Features.Dashboard;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        ViewModel = null!;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public DashboardViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.PropertyChanged += OnPromptChanged;
        PositionPrompt();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) ViewModel.PropertyChanged -= OnPromptChanged;
    }

    private void OnPromptChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.PromptTargetX) or nameof(DashboardViewModel.PromptTargetY))
            PositionPrompt();
    }

    private void OnPromptSizeChanged(object sender, SizeChangedEventArgs e) => PositionPrompt();

    private void PositionPrompt()
    {
        if (ViewModel is null || PromptCanvas is null || PromptMarker is null) return;
        if (PromptCenter is not null)
        {
            Canvas.SetLeft(PromptCenter, TargetPosition.Calculate(0.5, PromptCanvas.ActualWidth, PromptCenter.ActualWidth));
            Canvas.SetTop(PromptCenter, TargetPosition.Calculate(0.5, PromptCanvas.ActualHeight, PromptCenter.ActualHeight));
        }
        Canvas.SetLeft(PromptMarker, TargetPosition.Calculate(ViewModel.PromptTargetX, PromptCanvas.ActualWidth, PromptMarker.ActualWidth));
        Canvas.SetTop(PromptMarker, TargetPosition.Calculate(ViewModel.PromptTargetY, PromptCanvas.ActualHeight, PromptMarker.ActualHeight));
    }
}
