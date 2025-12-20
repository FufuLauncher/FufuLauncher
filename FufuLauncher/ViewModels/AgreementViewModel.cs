using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;

namespace FufuLauncher.ViewModels
{
    public partial class AgreementViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;

        [ObservableProperty]
        private bool _isAgreementChecked;

        public IAsyncRelayCommand ViewAgreementCommand
        {
            get;
        }
        public IAsyncRelayCommand NextCommand
        {
            get;
        }

        public AgreementViewModel(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;

            ViewAgreementCommand = new AsyncRelayCommand(ViewAgreementAsync);
            NextCommand = new AsyncRelayCommand(NextAsync);
        }



        private async Task ViewAgreementAsync()
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://philia093.cyou/"));
        }



        private async Task NextAsync()
        {
            if (!IsAgreementChecked) return;

            await _localSettingsService.SaveSettingAsync("UserAgreementAccepted", true);
            WeakReferenceMessenger.Default.Send(new AgreementAcceptedMessage());
        }
    }
}