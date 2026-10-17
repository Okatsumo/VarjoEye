using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App.Features.Dashboard.Components;

public sealed partial class TrackingPreview : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(DashboardViewModel), typeof(TrackingPreview),
            new PropertyMetadata(null, OnViewModelChanged));

    public TrackingPreview() => InitializeComponent();

    public DashboardViewModel ViewModel
    {
        get => (DashboardViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrackingPreview preview)
        {
            preview.DataContext = e.NewValue;
        }
    }
}
