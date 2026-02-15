using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels
{
    public partial class AgreementViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        private bool _isAgreementChecked;
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        private bool _isIconCheckMode;

        public Visibility AgreementVisibility => IsIconCheckMode ? Visibility.Collapsed : Visibility.Visible;
        
        public Visibility IconCheckVisibility => IsIconCheckMode ? Visibility.Visible : Visibility.Collapsed;

        public IAsyncRelayCommand ViewAgreementCommand { get; }
        
        public IAsyncRelayCommand NextCommand { get; }
        
        public IAsyncRelayCommand ConfirmIconsCommand { get; }
        
        public IAsyncRelayCommand TroubleshootIconsCommand { get; }

        public AgreementViewModel(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;

            ViewAgreementCommand = new AsyncRelayCommand(ViewAgreementAsync);

            NextCommand = new AsyncRelayCommand(GoToIconCheckAsync);
            ConfirmIconsCommand = new AsyncRelayCommand(FinalizeAgreementAsync);
            TroubleshootIconsCommand = new AsyncRelayCommand(OnIconsMissingAsync);
        }
        
        private async Task ViewAgreementAsync()
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://philia093.cyou/"));
        }
        
        private async Task GoToIconCheckAsync()
        {
            if (!IsAgreementChecked) return;
            IsIconCheckMode = true;
            await Task.CompletedTask;
        }
        
        private async Task FinalizeAgreementAsync()
        {
            await _localSettingsService.SaveSettingAsync("UserAgreementAccepted", true);
            WeakReferenceMessenger.Default.Send(new AgreementAcceptedMessage());
        }
        
        private async Task OnIconsMissingAsync()
        {
            var helpUrl = "https://wwaoi.lanzouu.com/ig75f3hedlaj"; 
            
            if (!string.IsNullOrEmpty(helpUrl) && Uri.TryCreate(helpUrl, UriKind.Absolute, out var uri))
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
    }
}