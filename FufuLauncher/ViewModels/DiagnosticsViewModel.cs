using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Models;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    [ObservableProperty]
    private SystemDiagnosticsInfo _info = new();

    [ObservableProperty]
    private bool _isLoading = true;

    public async void InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var service = new SystemDiagnosticsService();
            Info = await service.GetSystemInfoAsync();
        }
        catch
        {
            Info.Suggestion = "初始化诊断服务失败";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CloseWindow(object window)
    {
        (window as Microsoft.UI.Xaml.Window)?.Close();
    }
}