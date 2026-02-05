using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Models;
using Windows.Storage.Pickers;

namespace FufuLauncher.ViewModels;

public class PluginViewModel : INotifyPropertyChanged
{
    private readonly string _pluginsPath;
    private ObservableCollection<PluginItem> _plugins;
    private string _statusMessage;
    private bool _isLoading;
    private bool _isEmpty;
    public event EventHandler<string>? DuplicateDetected;

    public ObservableCollection<PluginItem> Plugins
    {
        get => _plugins;
        set
        {
            _plugins = value; OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value; OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value; OnPropertyChanged();
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            _isEmpty = value; OnPropertyChanged();
        }
    }

    public ICommand RefreshCommand
    {
        get;
    }
    public ICommand AddPluginCommand
    {
        get;
    }
    public ICommand TogglePluginCommand
    {
        get;
    }
    public ICommand DeletePluginCommand
    {
        get;
    }
    public ICommand SortCommand
    {
        get;
    }
    public ICommand OpenFolderCommand
    {
        get;
    }

    public PluginViewModel()
    {
        _pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins");
        Plugins = new ObservableCollection<PluginItem>();

        RefreshCommand = new RelayCommand(LoadPlugins);
        AddPluginCommand = new RelayCommand(AddPluginAsync);
        TogglePluginCommand = new RelayCommand<PluginItem>(TogglePlugin);
        DeletePluginCommand = new RelayCommand<PluginItem>(DeletePlugin);
        SortCommand = new RelayCommand<string>(SortPlugins);
        OpenFolderCommand = new RelayCommand(OpenPluginFolder);
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_pluginsPath))
        {
            Directory.CreateDirectory(_pluginsPath);
        }
    }

    private void UpdateIsEmpty()
    {
        IsEmpty = Plugins == null || Plugins.Count == 0;
    }

    private (string? Name, string? Developer, string? Description) GetPluginInfoFromConfig(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return (null, null, null);

        string? name = null;
        string? dev = null;
        string? desc = null;

        try
        {
            var lines = File.ReadAllLines(configPath);
            bool inGeneralSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inGeneralSection = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inGeneralSection)
                {
                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();

                        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = val;
                        else if (key.Equals("Developer", StringComparison.OrdinalIgnoreCase)) dev = val;
                        else if (key.Equals("Description", StringComparison.OrdinalIgnoreCase)) desc = val;
                    }
                }
            }
        }
        catch { }
        return (name, dev, desc);
    }

    public void LoadPlugins()
    {
        try
        {
            IsLoading = true;
            EnsureDirectoryExists();
            Plugins.Clear();

            var rootDir = new DirectoryInfo(_pluginsPath);
            var subDirs = rootDir.GetDirectories();
            var nameConflictTracker = new Dictionary<string, List<string>>();
            
            foreach (var dir in subDirs)
            {
                var dllFile = dir.GetFiles("*.dll").FirstOrDefault();
                var disabledFile = dir.GetFiles("*.dll.disabled").FirstOrDefault();

                FileInfo targetFile = dllFile ?? disabledFile;
                if (targetFile == null) continue;

                bool isEnabled = dllFile != null;

                var iniFile = dir.GetFiles("config.ini").FirstOrDefault()
                              ?? dir.GetFiles("*.ini").FirstOrDefault();

                string displayName = targetFile.Name;
                string developer = "";
                string description = "";

                if (iniFile != null)
                {
                    var info = GetPluginInfoFromConfig(iniFile.FullName);
                    if (!string.IsNullOrEmpty(info.Name)) displayName = info.Name;
                    if (!string.IsNullOrEmpty(info.Developer)) developer = info.Developer;
                    if (!string.IsNullOrEmpty(info.Description)) description = info.Description;
                }
                if (!nameConflictTracker.ContainsKey(displayName))
                {
                    nameConflictTracker[displayName] = new List<string>();
                }
                nameConflictTracker[displayName].Add(dir.Name);

                Plugins.Add(new PluginItem
                {
                    FileName = targetFile.Name,
                    DisplayName = displayName,
                    Developer = developer,
                    Description = description,
                    FullPath = targetFile.FullName,
                    DirectoryPath = dir.FullName,
                    ConfigFilePath = iniFile?.FullName,
                    IsEnabled = isEnabled,
                    FileSize = targetFile.Length,
                    DateModified = targetFile.LastWriteTime
                });
            }
            StatusMessage = $"加载完成，共 {Plugins.Count} 个插件";
            CheckForDuplicates(nameConflictTracker);
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
            Debug.WriteLine($"[PluginVM] Load Error: {ex}");
        }
        finally
        {
            IsLoading = false;
            UpdateIsEmpty();
        }
    }
    private void CheckForDuplicates(Dictionary<string, List<string>> tracker)
    {
        var duplicates = tracker.Where(kv => kv.Value.Count > 1).ToList();

        if (duplicates.Any())
        {
            StringBuilder sb = new();
            sb.AppendLine("检测到以下重复插件：\n");

            foreach (var item in duplicates)
            {
                sb.AppendLine($"插件名称：{item.Key}");
                sb.AppendLine($"   冲突文件夹：");
                foreach (var folder in item.Value)
                {
                    sb.AppendLine($"    - {folder}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("建议手动删除旧版本插件");
            
            DuplicateDetected?.Invoke(this, sb.ToString());
        }
    }

    private async void AddPluginAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".dll");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                EnsureDirectoryExists();

                var folderName = Path.GetFileNameWithoutExtension(file.Name);
                var destFolderPath = Path.Combine(_pluginsPath, folderName);

                if (!Directory.Exists(destFolderPath))
                {
                    Directory.CreateDirectory(destFolderPath);
                }

                var destPath = Path.Combine(destFolderPath, file.Name);
                File.Copy(file.Path, destPath, true);

                StatusMessage = $"已添加插件: {folderName}";
                LoadPlugins();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加失败: {ex.Message}";
        }
    }

    private void TogglePlugin(PluginItem? item)
    {
        if (item == null || !File.Exists(item.FullPath)) return;

        try
        {
            string newPath;
            bool targetState;

            if (item.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                newPath = item.FullPath.Substring(0, item.FullPath.Length - ".disabled".Length);
                targetState = true;
            }
            else
            {
                if (!item.FullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "无法识别的文件类型";
                    return;
                }
                newPath = item.FullPath + ".disabled";
                targetState = false;
            }

            if (File.Exists(newPath))
            {
                StatusMessage = "操作失败：目标文件名已存在";
                item.RefreshState();
                return;
            }

            File.Move(item.FullPath, newPath);
            item.FullPath = newPath;
            item.FileName = Path.GetFileName(newPath);
            item.IsEnabled = targetState;

            StatusMessage = $"已{(targetState ? "启用" : "禁用")}: {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换状态失败: {ex.Message}";
            item.RefreshState();
        }
    }

    private void DeletePlugin(PluginItem? item)
    {
        if (item == null) return;
        try
        {
            if (Directory.Exists(item.DirectoryPath))
            {
                Directory.Delete(item.DirectoryPath, true);
                Plugins.Remove(item);
                UpdateIsEmpty();
                StatusMessage = $"已删除插件: {item.DisplayName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    public void PerformRename(PluginItem item, string newName)
    {
        try
        {
            var oldDir = item.DirectoryPath;
            var parentDir = Directory.GetParent(oldDir)?.FullName;
            if (parentDir == null) return;

            var newDir = Path.Combine(parentDir, newName);

            if (string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)) return;

            if (Directory.Exists(newDir))
            {
                StatusMessage = "重命名失败：文件夹名已存在";
                return;
            }

            Directory.Move(oldDir, newDir);

            item.DirectoryPath = newDir;
            var fileName = Path.GetFileName(item.FullPath);
            item.FullPath = Path.Combine(newDir, fileName);

            if (item.HasConfig)
            {
                var configName = Path.GetFileName(item.ConfigFilePath);
                item.ConfigFilePath = Path.Combine(newDir, configName);
            }

            StatusMessage = $"重命名成功";
        }
        catch (Exception ex)
        {
            StatusMessage = $"重命名失败: {ex.Message}";
        }
    }

    private void SortPlugins(string? sortType)
    {
        if (string.IsNullOrEmpty(sortType)) return;

        var sorted = sortType switch
        {
            "Name" => Plugins.OrderBy(x => x.DisplayName),
            "Size" => Plugins.OrderByDescending(x => x.FileSize),
            "Date" => Plugins.OrderByDescending(x => x.DateModified),
            "Status" => Plugins.OrderByDescending(x => x.IsEnabled),
            _ => Plugins.OrderBy(x => x.DisplayName)
        };

        var list = sorted.ToList();
        Plugins.Clear();
        foreach (var item in list) Plugins.Add(item);

        StatusMessage = "列表已排序";
    }

    private void OpenPluginFolder()
    {
        try
        {
            EnsureDirectoryExists();
            Process.Start(new ProcessStartInfo
            {
                FileName = _pluginsPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}