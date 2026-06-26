/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.ViewModels
{
    public partial class CalculatorViewModel : ObservableRecipient
    {
        [ObservableProperty]
        private bool _isLoading = true;

        [ObservableProperty]
        private string _statusMessage = "正在初始化...";
    }
}
