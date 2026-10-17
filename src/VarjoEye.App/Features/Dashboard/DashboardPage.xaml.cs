using Microsoft.UI.Xaml.Controls;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App.Features.Dashboard;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        ViewModel = null!;
        InitializeComponent();
    }

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public DashboardViewModel ViewModel { get; }
}
