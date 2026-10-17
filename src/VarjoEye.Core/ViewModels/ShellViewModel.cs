using CommunityToolkit.Mvvm.ComponentModel;

namespace VarjoEye.Core.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "VarjoEye";
}
