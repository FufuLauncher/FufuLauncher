/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.PluginMirror;
using Microsoft.UI.Dispatching;
using FufuLauncher.Helpers;

namespace FufuLauncher.ViewModels;

public partial class PluginStoreViewModel : INotifyPropertyChanged
{
    #region Core State, Commands & Lifecycle

    private readonly PluginStoreService _storeService;
    private readonly LuaPluginInstaller _luaInstaller;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly string _pluginsDir;
    private DispatcherQueue? _dispatcher;

    private ObservableCollection<PluginStoreItem> _plugins = new();
    private ObservableCollection<PluginStoreCategory> _categories = new();
    private PluginStoreCategory? _selectedCategory;
    private string _searchText = string.Empty;
    private string _sortMode = "popular";
    private bool _isLoading;
    private bool _isEmpty;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalPlugins;

    private bool _hasContent;

    private bool _isMirrorAccelerationEnabled = true;

    private CancellationTokenSource? _installCts;

    private readonly HashSet<string> _installingPluginIds = new(StringComparer.Ordinal);

    private static readonly string CurrentAppVersion =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

    public PluginStoreViewModel(PluginStoreService storeService, LuaPluginInstaller luaInstaller,
        ILocalSettingsService localSettingsService)
    {
        _pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        _storeService = storeService;
        _luaInstaller = luaInstaller;
        _localSettingsService = localSettingsService;

        _luaInstaller.ProgressChanged += OnInstallProgress;

        RefreshCommand = new RelayCommand(async () => await LoadPluginsAsync());
        SearchCommand = new RelayCommand(async () => await SearchAsync());
        SortCommand = new RelayCommand<string>(async (s) => await SortAsync(s!));
        SelectCategoryCommand = new RelayCommand<PluginStoreCategory>(async (cat) => await SelectCategoryAsync(cat!));
        InstallCommand = new RelayCommand<PluginStoreItem>(async (item) => await InstallPluginAsync(item!));
        UninstallCommand = new RelayCommand<PluginStoreItem>(async (item) => await UninstallPluginAsync(item!));
        NextPageCommand = new RelayCommand(async () => await GoToPageAsync(_currentPage + 1));
        PrevPageCommand = new RelayCommand(async () => await GoToPageAsync(_currentPage - 1));
        AddPrivatePluginCommand = new RelayCommand(async () => await AddPrivatePluginAsync());
        LuaTestCommand = new RelayCommand(async () => await ExecuteLuaTestAsync());
    }

    public ObservableCollection<PluginStoreItem> Plugins
    {
        get => _plugins;
        set
        {
            _plugins = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<PluginStoreCategory> Categories
    {
        get => _categories;
        set
        {
            _categories = value;
            OnPropertyChanged();
        }
    }

    public PluginStoreCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            SyncCategorySelection();
            OnPropertyChanged();
        }
    }

    private void SyncCategorySelection()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = ReferenceEquals(category, _selectedCategory);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
        }
    }

    public string SortMode
    {
        get => _sortMode;
        set
        {
            _sortMode = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
            OnPageStateChanged();
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            _isEmpty = value;
            OnPropertyChanged();
            OnPageStateChanged();
        }
    }

    public bool HasError
    {
        get => _hasError;
        set
        {
            _hasError = value;
            OnPropertyChanged();
            OnPageStateChanged();
        }
    }

    public bool ShowSkeleton => IsLoading && !_hasContent;
    public bool IsRefreshing => IsLoading && _hasContent;
    public bool ShowError => HasError && !IsLoading;
    public bool ShowEmpty => IsEmpty && !IsLoading && !HasError;
    public bool ShowGrid => !HasError && !IsEmpty && (_hasContent || !IsLoading);
    public bool ShowPagination => ShowGrid && TotalPages > 1;

    private void OnPageStateChanged()
    {
        OnPropertyChanged(nameof(ShowSkeleton));
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowPagination));
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(PageInfo));
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        set
        {
            _totalPages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(ShowPagination));
        }
    }

    public int TotalPlugins
    {
        get => _totalPlugins;
        set
        {
            _totalPlugins = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageInfo));
        }
    }

    public bool CanGoPrev => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;
    public string PageInfo => TotalPages > 0 ? $"{CurrentPage} / {TotalPages}" : "";

    private bool _isStoreBusy;

    public bool IsStoreBusy
    {
        get => _isStoreBusy;
        private set
        {
            if (_isStoreBusy == value) return;
            _isStoreBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInteractWithStore));
        }
    }

    public bool CanInteractWithStore => !_isStoreBusy;

    public bool IsMirrorAccelerationEnabled
    {
        get => _isMirrorAccelerationEnabled;
        set
        {
            if (_isMirrorAccelerationEnabled == value) return;
            _isMirrorAccelerationEnabled = value;
            OnPropertyChanged();
            _ = _localSettingsService.SaveSettingAsync(PluginMirrorDownloadService.SettingKey, value);
        }
    }

    private async Task LoadMirrorAccelerationSettingAsync()
    {
        try
        {
            var json = await _localSettingsService.ReadSettingAsync(PluginMirrorDownloadService.SettingKey);
            _isMirrorAccelerationEnabled = json == null || Convert.ToBoolean(json);
            OnPropertyChanged(nameof(IsMirrorAccelerationEnabled));
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel",
                $"[PluginStoreVM] Failed to load mirror acceleration setting: {ex.Message}");
        }
    }

    public ICommand RefreshCommand
    {
        get;
    }

    public ICommand SearchCommand
    {
        get;
    }

    public ICommand SortCommand
    {
        get;
    }

    public ICommand SelectCategoryCommand
    {
        get;
    }

    public ICommand InstallCommand
    {
        get;
    }

    public ICommand UninstallCommand
    {
        get;
    }

    public ICommand NextPageCommand
    {
        get;
    }

    public ICommand PrevPageCommand
    {
        get;
    }

    public ICommand AddPrivatePluginCommand
    {
        get;
    }

    public ICommand LuaTestCommand
    {
        get;
    }

    public async Task InitializeAsync()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        await LoadMirrorAccelerationSettingAsync();
        await LoadCategoriesAsync();
        await LoadPluginsAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}