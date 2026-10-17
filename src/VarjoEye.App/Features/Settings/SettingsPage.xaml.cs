using Microsoft.UI.Xaml.Controls;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App.Features.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = null!;
        InitializeComponent();
    }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
