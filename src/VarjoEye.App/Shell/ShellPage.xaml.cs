using Microsoft.UI.Xaml.Controls;
using VarjoEye.App.Services;

namespace VarjoEye.App.Shell;

public sealed partial class ShellPage : Page
{
    private readonly NavigationService _navigation;
    private bool _syncing;

    public ShellPage(NavigationService navigation)
    {
        _navigation = navigation;
        InitializeComponent();
        _navigation.Navigated += OnNavigated;
    }

    public Frame ContentFrame => ShellFrame;

    private void NavView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncing || args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        _navigation.Navigate(tag);
    }

    private void OnNavigated(object? sender, string tag)
    {
        _syncing = true;
        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag is string itemTag)
            {
                item.IsSelected = string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase);
            }
        }

        _syncing = false;
    }
}
