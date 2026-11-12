/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using System.Text;
using System.Diagnostics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FufuLauncher.ViewModels
{
    public partial class AgreementViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        private bool _isAgreementChecked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HintVisibility))]
        [NotifyPropertyChangedFor(nameof(CanAcceptAgreement))]
        private bool _hasReadAgreement;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        [NotifyPropertyChangedFor(nameof(CompleteVisibility))]
        private bool _isIconCheckMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        [NotifyPropertyChangedFor(nameof(TrustVisibility))]
        [NotifyPropertyChangedFor(nameof(CompleteVisibility))]
        private bool _isDataDirMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        [NotifyPropertyChangedFor(nameof(TrustVisibility))]
        [NotifyPropertyChangedFor(nameof(CompleteVisibility))]
        private bool _isTrustMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        [NotifyPropertyChangedFor(nameof(TrustVisibility))]
        [NotifyPropertyChangedFor(nameof(CompleteVisibility))]
        private bool _isCompleteMode;

        [ObservableProperty] private string _dataPath = Helpers.AppPaths.DataDir;

        [ObservableProperty] private string _cachePath = Helpers.AppPaths.CacheDir;

        [ObservableProperty] private bool _isTelemetryEnabled;

        public Visibility AgreementVisibility =>
            (!IsIconCheckMode && !IsDataDirMode && !IsTrustMode && !IsCompleteMode)
                ? Visibility.Visible
                : Visibility.Collapsed;

        public Visibility IconCheckVisibility =>
            (IsIconCheckMode && !IsDataDirMode && !IsTrustMode && !IsCompleteMode)
                ? Visibility.Visible
                : Visibility.Collapsed;

        public Visibility DataDirVisibility =>
            (IsDataDirMode && !IsTrustMode && !IsCompleteMode) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility TrustVisibility =>
            (IsTrustMode && !IsCompleteMode) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility CompleteVisibility => IsCompleteMode ? Visibility.Visible : Visibility.Collapsed;

        public Visibility HintVisibility =>
            (HasReadAgreement || HasAgreementLoadFailed || IsAgreementLoading)
                ? Visibility.Collapsed
                : Visibility.Visible;

        [ObservableProperty] private string _agreementText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
        [NotifyPropertyChangedFor(nameof(HintVisibility))]
        private bool _isAgreementLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementErrorVisibility))]
        [NotifyPropertyChangedFor(nameof(HintVisibility))]
        [NotifyPropertyChangedFor(nameof(SkipHintVisibility))]
        [NotifyPropertyChangedFor(nameof(CanAcceptAgreement))]
        private bool _hasAgreementLoadFailed;

        public Visibility LoadingVisibility => IsAgreementLoading ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AgreementErrorVisibility =>
            HasAgreementLoadFailed ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SkipHintVisibility =>
            HasAgreementLoadFailed ? Visibility.Visible : Visibility.Collapsed;

        public bool CanAcceptAgreement => HasReadAgreement || HasAgreementLoadFailed;

        private static bool IsSimplifiedChinese
        {
            get
            {
                var culture = ResourceExtensions.CurrentCulture;
                if (string.IsNullOrWhiteSpace(culture))
                {
                    culture = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault();
                }

                return culture != null && culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task InitializeAgreementTextAsync()
        {
            if (!string.IsNullOrEmpty(AgreementText)) return;

            var wasLoadFailed = HasAgreementLoadFailed;

            IsAgreementLoading = true;
            HasAgreementLoadFailed = false;

            try
            {
                var url = IsSimplifiedChinese ? ApiEndpoints.CnUserAgreementUrl : ApiEndpoints.EnUserAgreementUrl;
                var remoteText = await FetchAgreementTextAsync(url);

                if (IsUsableAgreement(remoteText))
                {
                    if (wasLoadFailed)
                    {
                        IsAgreementChecked = false;
                    }

                    AgreementText = remoteText;
                }
                else
                {
                    HasAgreementLoadFailed = true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("AgreementViewModel", $"[Agreement] Remote agreement fetch failed: {ex.Message}");
                HasAgreementLoadFailed = true;
            }
            finally
            {
                IsAgreementLoading = false;
            }
        }

        private static bool IsUsableAgreement(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 80) return false;

            var head = text[..Math.Min(200, text.Length)];
            return !head.Contains("<html", StringComparison.OrdinalIgnoreCase) &&
                   !head.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> FetchAgreementTextAsync(string url)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            var bytes = await client.GetByteArrayAsync(url);
            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        public IAsyncRelayCommand TroubleshootIconsCommand
        {
            get;
        }

        public IAsyncRelayCommand BrowseDataPathCommand
        {
            get;
        }

        public IAsyncRelayCommand BrowseCachePathCommand
        {
            get;
        }

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(PathErrorVisibility))]
        private string _pathError = string.Empty;

        public Visibility PathErrorVisibility =>
            string.IsNullOrEmpty(PathError) ? Visibility.Collapsed : Visibility.Visible;

        public AgreementViewModel(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;

            TroubleshootIconsCommand = new AsyncRelayCommand(OnIconsMissingAsync);
            BrowseDataPathCommand = new AsyncRelayCommand(PickDataPathAsync);
            BrowseCachePathCommand = new AsyncRelayCommand(PickCachePathAsync);
        }

        public void EnterIconCheckStep()
        {
            IsIconCheckMode = true;
        }

        public void EnterDataDirStep()
        {
            IsDataDirMode = true;
        }

        public async Task InitTelemetryChoiceAsync()
        {
            IsTelemetryEnabled = await TelemetryOptIn.ResolveEnabledAsync(_localSettingsService);
        }

        public async Task<bool> TryPrepareDataDirStepAsync()
        {
            try
            {
                var validationError = Helpers.AppPaths.ValidateCustomPaths(DataPath, CachePath);
                if (validationError != null)
                {
                    PathError = validationError;
                    return false;
                }

                Helpers.AppPaths.SaveCustomPaths(DataPath, CachePath);
                Helpers.AppPaths.FinalizeFirstRun();

                await TelemetryOptIn.SaveUserChoiceAsync(_localSettingsService, IsTelemetryEnabled);

                PathError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                PathError = string.Format("StoragePath_Error_SaveFailed".GetLocalized(), ex.Message);
                DebugLog.Info("AgreementViewModel", $"[Agreement] TryPrepareDataDirStep failed: {ex}");
                return false;
            }
        }

        public void EnterTrustStep()
        {
            IsTrustMode = true;
        }

        public void EnterCompleteStep()
        {
            IsCompleteMode = true;
        }

        public async Task<bool> SaveAgreementAcceptedAsync()
        {
            try
            {
                if (await _localSettingsService.TrySaveSettingAsync("UserAgreementAccepted", true))
                {
                    return true;
                }

                PathError = string.Format("StoragePath_Error_SaveFailed".GetLocalized(), "UserAgreementAccepted");
                return false;
            }
            catch (Exception ex)
            {
                PathError = string.Format("StoragePath_Error_SaveFailed".GetLocalized(), ex.Message);
                DebugLog.Info("AgreementViewModel", $"[Agreement] SaveAgreementAcceptedAsync failed: {ex}");
                return false;
            }
        }

        public bool TryRestart()
        {
            if (AppRestartHelper.TryRestart())
            {
                return true;
            }

            DebugLog.Info("AgreementViewModel", "[Agreement] Manual restart failed");
            return false;
        }

        private async Task OnIconsMissingAsync()
        {
            var helpUrl = ApiEndpoints.IconTroubleshootUrl;

            if (!string.IsNullOrEmpty(helpUrl) && Uri.TryCreate(helpUrl, UriKind.Absolute, out var uri))
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }

        private async Task PickDataPathAsync()
        {
            var folder = await PickFolderAsync();
            if (folder != null)
            {
                PathError = string.Empty;
                DataPath = folder;
            }
        }

        private async Task PickCachePathAsync()
        {
            var folder = await PickFolderAsync();
            if (folder != null)
            {
                PathError = string.Empty;
                CachePath = folder;
            }
        }

        private static async Task<string?> PickFolderAsync()
            => await FilePickerService.PickFolderAsync(null, PickerLocationId.ComputerFolder);
    }
}