using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VarjoEye.Core.ViewModels;

namespace VarjoEye.App.Features.Dashboard.Components;

public sealed partial class EyeTrackingData : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(DashboardViewModel), typeof(EyeTrackingData),
            new PropertyMetadata(null, OnViewModelChanged));

    public EyeTrackingData() => InitializeComponent();

    public DashboardViewModel ViewModel
    {
        get => (DashboardViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EyeTrackingData data)
        {
            data.DataContext = e.NewValue;
        }
    }
}
