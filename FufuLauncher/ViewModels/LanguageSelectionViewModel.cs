/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FufuLauncher.ViewModels
{
    public partial class LanguageOption : ObservableObject
    {
        public string FlagImagePath { get; init; } = string.Empty;
        public string CountryName { get; init; } = string.Empty;
        public string NativeName { get; init; } = string.Empty;
        public string LanguageCode { get; init; } = string.Empty;
        public AppLanguage LanguageEnum { get; init; }
        public string CultureCode { get; init; } = string.Empty;
        public bool IsFontIcon { get; init; }
        public string Glyph { get; init; } = string.Empty;

        public Microsoft.UI.Xaml.Visibility FontIconVisibility =>
            IsFontIcon ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility FlagImageVisibility =>
            !IsFontIcon ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        
        public Microsoft.UI.Xaml.Visibility NormalContentVisibility =>
            IsSelected ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility SelectedContentVisibility =>
            IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public double NormalOpacity => IsSelected ? 0.0 : 1.0;
        public double SelectedOpacity => IsSelected ? 1.0 : 0.0;

        [ObservableProperty]
        private bool _isSelected;

        partial void OnIsSelectedChanged(bool value)
        {
            OnPropertyChanged(nameof(NormalContentVisibility));
            OnPropertyChanged(nameof(SelectedContentVisibility));
            OnPropertyChanged(nameof(NormalOpacity));
            OnPropertyChanged(nameof(SelectedOpacity));
        }
    }

    public partial class LanguageSelectionViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;

        public ObservableCollection<LanguageOption> Languages { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsLanguageSelected))]
        private LanguageOption? _selectedLanguage;

        public bool IsLanguageSelected => SelectedLanguage != null;

        public IAsyncRelayCommand ConfirmLanguageCommand { get; }

        public LanguageSelectionViewModel(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;

            ConfirmLanguageCommand = new AsyncRelayCommand(ConfirmLanguageAsync, () => IsLanguageSelected);

            InitializeLanguages();
        }

        private static string FlagPath(string name) => $"ms-appx:///Assets/Flags/{name}.png";

        private void InitializeLanguages()
        {
            var options = new List<LanguageOption>
            {
                new() { IsFontIcon = true,  Glyph = "\uE12B",        NativeName = "System Default",        CountryName = "System Default",       LanguageCode = "auto",  LanguageEnum = AppLanguage.Default, CultureCode = "" },
                new() { FlagImagePath = FlagPath("china"),              NativeName = "中国",                   CountryName = "China",                LanguageCode = "zh-CN", LanguageEnum = AppLanguage.zhCN,   CultureCode = "zh-CN" },
                new() { IsFontIcon = true,  Glyph = "\uF2B7",        NativeName = "繁體中文",               CountryName = "Traditional Chinese",  LanguageCode = "zh-TW", LanguageEnum = AppLanguage.zhTW,   CultureCode = "zh-TW" },
                new() { FlagImagePath = FlagPath("usa"),                NativeName = "English",               CountryName = "United States",        LanguageCode = "en-US", LanguageEnum = AppLanguage.enUS,   CultureCode = "en-US" },
                new() { FlagImagePath = FlagPath("france"),             NativeName = "Français",              CountryName = "France",               LanguageCode = "fr-FR", LanguageEnum = AppLanguage.fr,     CultureCode = "fr-FR" },
                new() { FlagImagePath = FlagPath("germany"),            NativeName = "Deutsch",               CountryName = "Germany",              LanguageCode = "de-DE", LanguageEnum = AppLanguage.de,     CultureCode = "de-DE" },
                new() { FlagImagePath = FlagPath("russia"),             NativeName = "Русский",               CountryName = "Russia",               LanguageCode = "ru-RU", LanguageEnum = AppLanguage.ru,     CultureCode = "ru-RU" },
                new() { FlagImagePath = FlagPath("japan"),              NativeName = "日本語",                 CountryName = "Japan",                LanguageCode = "ja-JP", LanguageEnum = AppLanguage.ja,     CultureCode = "ja-JP" },
                new() { FlagImagePath = FlagPath("spain"),              NativeName = "Español",               CountryName = "Spain",                LanguageCode = "es-ES", LanguageEnum = AppLanguage.es,     CultureCode = "es-ES" },
                new() { FlagImagePath = FlagPath("south_korea"),        NativeName = "한국어",                 CountryName = "South Korea",          LanguageCode = "ko-KR", LanguageEnum = AppLanguage.ko,     CultureCode = "ko-KR" },
                new() { FlagImagePath = FlagPath("italy"),              NativeName = "Italiano",              CountryName = "Italy",                LanguageCode = "it-IT", LanguageEnum = AppLanguage.it,     CultureCode = "it-IT" },
                new() { FlagImagePath = FlagPath("indonesia"),          NativeName = "Bahasa Indonesia",      CountryName = "Indonesia",            LanguageCode = "id-ID", LanguageEnum = AppLanguage.id,     CultureCode = "id-ID" },
                new() { FlagImagePath = FlagPath("brazil"),             NativeName = "Português (Brasil)",    CountryName = "Brazil",               LanguageCode = "pt-BR", LanguageEnum = AppLanguage.pt,     CultureCode = "pt-BR" },
                new() { FlagImagePath = FlagPath("mexico"),             NativeName = "Español (México)",      CountryName = "Mexico",               LanguageCode = "es-MX", LanguageEnum = AppLanguage.esMX,   CultureCode = "es-MX" },
            };

            foreach (var opt in options)
            {
                Languages.Add(opt);
            }
        }

        public void SelectLanguage(LanguageOption language)
        {
            foreach (var lang in Languages)
            {
                lang.IsSelected = false;
            }
            
            language.IsSelected = true;
            SelectedLanguage = language;
            
            (ConfirmLanguageCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }

        private async Task ConfirmLanguageAsync()
        {
            if (SelectedLanguage == null) return;

            try
            {
                var language = SelectedLanguage.LanguageEnum;
                
                await _localSettingsService.SaveSettingAsync("AppLanguage", (int)language);

                var culture = language switch
                {
                    AppLanguage.zhCN => "zh-CN",
                    AppLanguage.zhTW => "zh-TW",
                    AppLanguage.enUS => "en-US",
                    AppLanguage.fr => "fr-FR",
                    AppLanguage.de => "de-DE",
                    AppLanguage.ru => "ru-RU",
                    AppLanguage.ja => "ja-JP",
                    AppLanguage.es => "es-ES",
                    AppLanguage.ko => "ko-KR",
                    AppLanguage.it => "it-IT",
                    AppLanguage.id => "id-ID",
                    AppLanguage.pt => "pt-BR",
                    AppLanguage.esMX => "es-MX",
                    _ => (string?)null
                };

                ResourceExtensions.SetLanguage(
                    language == AppLanguage.Default ? null : culture);
                
                App.FirstRunSelectedLanguage = language;

                Debug.WriteLine($"[LangSelect] Language confirmed: {language}, culture='{culture ?? "null"}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LangSelect] Failed to apply language: {ex.Message}");
            }
        }
    }
}
