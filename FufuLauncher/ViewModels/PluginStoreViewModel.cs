/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;

namespace FufuLauncher.ViewModels;

public class PluginStoreViewModel : INotifyPropertyChanged
{
    private readonly PluginStoreService _storeService;
    private readonly LuaPluginInstaller _luaInstaller;
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

    private CancellationTokenSource? _installCts;

    public PluginStoreViewModel(PluginStoreService storeService, LuaPluginInstaller luaInstaller)
    {
        _pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        _storeService = storeService;
        _luaInstaller = luaInstaller;

        _luaInstaller.ProgressChanged += OnInstallProgress;
        _luaInstaller.LogReceived += OnInstallLog;

        RefreshCommand = new RelayCommand(async () => await LoadPluginsAsync());
        SearchCommand = new RelayCommand(async () => await SearchAsync());
        SortCommand = new RelayCommand<string>(async (s) => await SortAsync(s!));
        SelectCategoryCommand = new RelayCommand<PluginStoreCategory>(async (cat) => await SelectCategoryAsync(cat!));
        InstallCommand = new RelayCommand<PluginStoreItem>(async (item) => await InstallPluginAsync(item!));
        UninstallCommand = new RelayCommand<PluginStoreItem>(async (item) => await UninstallPluginAsync(item!));
        NextPageCommand = new RelayCommand(async () => await GoToPageAsync(_currentPage + 1));
        PrevPageCommand = new RelayCommand(async () => await GoToPageAsync(_currentPage - 1));
    }

    public ObservableCollection<PluginStoreItem> Plugins
    {
        get => _plugins;
        set { _plugins = value; OnPropertyChanged(); }
    }

    public ObservableCollection<PluginStoreCategory> Categories
    {
        get => _categories;
        set { _categories = value; OnPropertyChanged(); }
    }

    public PluginStoreCategory? SelectedCategory
    {
        get => _selectedCategory;
        set { _selectedCategory = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    public string SortMode
    {
        get => _sortMode;
        set { _sortMode = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set { _isEmpty = value; OnPropertyChanged(); }
    }

    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set { _currentPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoPrev)); OnPropertyChanged(nameof(CanGoNext)); OnPropertyChanged(nameof(PageInfo)); }
    }

    public int TotalPages
    {
        get => _totalPages;
        set { _totalPages = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); OnPropertyChanged(nameof(PageInfo)); }
    }

    public int TotalPlugins
    {
        get => _totalPlugins;
        set { _totalPlugins = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageInfo)); }
    }

    public bool CanGoPrev => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;
    public string PageInfo => TotalPages > 0 ? $"{CurrentPage} / {TotalPages}" : "";

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand SortCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    
    public async Task InitializeAsync()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        
        await LoadCategoriesAsync();
        await LoadPluginsAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var cats = await _storeService.GetCategoriesAsync();

            if (cats.Count > 0)
            {
                Categories.Clear();
                
                Categories.Add(new PluginStoreCategory
                {
                    Key = "",
                    DisplayName = "全部",
                    Icon = "\uE71D"
                });

                foreach (var cat in cats)
                {
                    Categories.Add(cat);
                }

                SelectedCategory = Categories.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreVM] Error loading categories: {ex.Message}");
            if (Categories.Count == 0)
            {
                Categories.Clear();
                Categories.Add(new PluginStoreCategory { Key = "", DisplayName = "全部", Icon = "\uE71D" });
                Categories.Add(new PluginStoreCategory { Key = "utility", DisplayName = "utility", Icon = "\uE90F" });
                Categories.Add(new PluginStoreCategory { Key = "gameplay", DisplayName = "gameplay", Icon = "\uE7FC" });
                Categories.Add(new PluginStoreCategory { Key = "visuals", DisplayName = "visuals", Icon = "\uE790" });
                SelectedCategory = Categories.FirstOrDefault();
            }
        }
    }

    public async Task LoadPluginsAsync()
    {
        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = "加载中...";

            var category = SelectedCategory?.Key;
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

            var response = await _storeService.GetPluginListAsync(
                category: string.IsNullOrEmpty(category) ? null : category,
                search: search,
                sort: SortMode,
                page: CurrentPage,
                pageSize: 20);

            Plugins.Clear();
            if (response.Plugins != null)
            {
                foreach (var plugin in response.Plugins)
                {
                    UpdateLocalState(plugin);
                    Plugins.Add(plugin);
                }
            }

            TotalPlugins = response.Total;
            TotalPages = response.Total > 0
                ? (int)Math.Ceiling((double)response.Total / 20)
                : 1;

            IsEmpty = Plugins.Count == 0;
            if (IsEmpty)
            {
                if (!string.IsNullOrWhiteSpace(SearchText) || (SelectedCategory != null && !string.IsNullOrEmpty(SelectedCategory.Key)))
                    StatusMessage = "没有找到匹配的插件";
                else
                    StatusMessage = "暂无可用的插件";
            }
            else
            {
                StatusMessage = $"共 {TotalPlugins} 个插件";
            }
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[PluginStoreVM] {ex.Message}");
            HasError = true;
            ErrorMessage = ex.Message;
            StatusMessage = "连接失败";
            IsEmpty = Plugins.Count == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreVM] Error loading plugins: {ex}");
            HasError = true;
            ErrorMessage = $"加载插件列表失败，请稍后重试";
            StatusMessage = "加载失败";
            IsEmpty = Plugins.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SearchAsync()
    {
        CurrentPage = 1;
        await LoadPluginsAsync();
    }

    private async Task SortAsync(string sortMode)
    {
        SortMode = sortMode;
        CurrentPage = 1;
        await LoadPluginsAsync();
    }

    private async Task SelectCategoryAsync(PluginStoreCategory category)
    {
        SelectedCategory = category;
        CurrentPage = 1;
        await LoadPluginsAsync();
    }

    public async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > TotalPages) return;
        CurrentPage = page;
        await LoadPluginsAsync();
    }
    

    private async Task InstallPluginAsync(PluginStoreItem item)
    {
        if (item == null || item.IsInstallInProgress) return;

        try
        {
            _installCts?.Cancel();
            _installCts = new CancellationTokenSource();

            item.State = StorePluginState.Installing;
            item.IsInstallInProgress = true;
            item.InstallProgress = 0;
            item.InstallStatusText = "验证中...";

            await _luaInstaller.ExecuteInstallScriptAsync(
                item.LuaInstallUrl,
                item.LuaHash,
                item.FileHash,
                _installCts.Token,
                item.DllFileName,
                item.Id);
            
            var pluginDir = Path.Combine(_pluginsDir, item.Id);
            _luaInstaller.EnsureConfigFileEntry(pluginDir, item.DllFileName);
            
            item.State = StorePluginState.Installed;
            item.InstallProgress = 100;
            item.InstallStatusText = "安装完成";
            StatusMessage = $"已安装: {item.Name}";
        }
        catch (HashMismatchException ex)
        {
            Debug.WriteLine($"[PluginStoreVM] Hash mismatch: {ex.Message}");
            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "校验失败";
            StatusMessage = $"安装失败: {ex.Message}";
        }
        catch (SecurityViolationException ex)
        {
            Debug.WriteLine($"[PluginStoreVM] Security violation: {ex.Message}");
            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "安全阻止";
            StatusMessage = $"安装已阻止: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "已取消";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreVM] Install error: {ex}");
            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "安装失败";
            StatusMessage = $"安装失败: {ex.Message}";
        }
        finally
        {
            item.IsInstallInProgress = false;
            _installCts?.Dispose();
            _installCts = null;
        }
    }

    private async Task UninstallPluginAsync(PluginStoreItem item)
    {
        if (item == null) return;

        try
        {
            item.IsInstallInProgress = true;
            item.State = StorePluginState.Installing;
            item.InstallStatusText = "卸载中...";

            if (!string.IsNullOrEmpty(item.LuaUninstallUrl))
            {
                await _luaInstaller.ExecuteInstallScriptAsync(item.LuaUninstallUrl);
            }
            else
            {
                var pluginDir = Path.Combine(_pluginsDir, item.Id);
                if (Directory.Exists(pluginDir))
                {
                    Directory.Delete(pluginDir, true);
                }
            }

            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "已卸载";
            StatusMessage = $"已卸载: {item.Name}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreVM] Uninstall error: {ex}");
            item.State = StorePluginState.Installed;
            item.InstallStatusText = "卸载失败";
        }
        finally
        {
            item.IsInstallInProgress = false;
        }
    }
    
    private void UpdateLocalState(PluginStoreItem storeItem)
    {
        if (!Directory.Exists(_pluginsDir)) return;

        var pluginDir = Path.Combine(_pluginsDir, storeItem.Id);

        if (Directory.Exists(pluginDir))
        {
            var configPath = Path.Combine(pluginDir, "config.ini");
            if (File.Exists(configPath))
            {
                try
                {
                    var lines = File.ReadAllLines(configPath);
                    string? localVersion = null;
                    var inGeneral = false;

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        if (inGeneral && trimmed.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = trimmed.Split('=', 2);
                            if (parts.Length == 2)
                                localVersion = parts[1].Trim();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(localVersion))
                    {
                        // Compare versions
                        if (localVersion != storeItem.Version)
                        {
                            storeItem.State = StorePluginState.UpdateAvailable;
                        }
                        else
                        {
                            storeItem.State = StorePluginState.Installed;
                        }
                    }
                    else
                    {
                        storeItem.State = StorePluginState.Installed;
                    }
                }
                catch
                {
                    storeItem.State = StorePluginState.Installed;
                }
            }
            else
            {
                storeItem.State = StorePluginState.Installed;
            }
        }
    }

    private void OnInstallProgress(int percent, string status)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            var installing = Plugins.FirstOrDefault(p => p.State == StorePluginState.Installing);
            if (installing != null)
            {
                installing.InstallProgress = percent;
                installing.InstallStatusText = status;
            }
        });
    }

    private void OnInstallLog(string message)
    {
        Debug.WriteLine($"[PluginStore] {message}");
    }
    

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
