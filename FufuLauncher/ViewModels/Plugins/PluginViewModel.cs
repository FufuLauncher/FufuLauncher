/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
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
            _plugins = value;
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

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            _isEmpty = value;
            OnPropertyChanged();
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
        catch
        {
        }

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
            var versionGuard = App.GetService<PluginGameVersionGuard>();

            foreach (var dir in subDirs)
            {
                var iniFile = dir.GetFiles("config.ini").FirstOrDefault()
                              ?? dir.GetFiles("*.ini").FirstOrDefault();

                var versionStatus = versionGuard.Inspect(dir.FullName, iniFile?.FullName);
                if (versionStatus.IsBlocked)
                {
                    versionGuard.TryDisablePlugin(dir.FullName, iniFile?.FullName, out _);
                }

                var dllFile = dir.GetFiles("*.dll").FirstOrDefault();
                var disabledFile = dir.GetFiles("*.dll.disabled").FirstOrDefault()
                                   ?? dir.GetFiles("*.disabled").FirstOrDefault();

                FileInfo? targetFile = dllFile ?? disabledFile;
                if (targetFile == null) continue;

                bool isEnabled = dllFile != null && !versionStatus.IsBlocked;

                if (iniFile == null)
                {
                    string newConfigPath = Path.Combine(dir.FullName, "config.ini");
                    string configContent =
                        $"[General]\r\nFile = {targetFile.Name}\r\nName = {dir.Name}\r\nDescription = Auto-generated config\r\n";
                    File.WriteAllText(newConfigPath, configContent, Encoding.UTF8);
                    iniFile = new FileInfo(newConfigPath);
                }

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
                    IsVersionBlocked = versionStatus.IsBlocked,
                    VersionBlockMessage = versionStatus.BlockMessage,
                    FileSize = targetFile.Length,
                    DateModified = targetFile.LastWriteTime
                });
            }

            StatusMessage = string.Format("Plugin_LoadDoneCount_Format".GetLocalized(), Plugins.Count);
            CheckForDuplicates(nameConflictTracker);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format("Common_LoadFailed_Format".GetLocalized(), ex.Message);
            DebugLog.Info("PluginViewModel", $"[PluginVM] Load Error: {ex}");
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
            sb.AppendLine("PluginPage_DuplicateDetectedHeader".GetLocalized());
            sb.AppendLine();

            foreach (var item in duplicates)
            {
                sb.AppendLine(string.Format("PluginPage_DuplicatePluginNameFormat".GetLocalized(), item.Key));
                sb.AppendLine("PluginPage_DuplicateConflictsHeader".GetLocalized());
                foreach (var folder in item.Value)
                {
                    sb.AppendLine(string.Format("PluginPage_DuplicateFolderItemFormat".GetLocalized(), folder));
                }

                sb.AppendLine();
            }

            sb.AppendLine("PluginPage_DuplicateHint".GetLocalized());

            DuplicateDetected?.Invoke(this, sb.ToString());
        }
    }

    private async void AddPluginAsync()
    {
        try
        {
            var path = await FilePickerService.PickOpenFileAsync(
                null,
                new[] { ("Plugin_PluginFiles".GetLocalized(), new[] { ".dll" }) },
                PickerLocationId.ComputerFolder,
                msg => StatusMessage = msg);
            if (!string.IsNullOrEmpty(path))
            {
                EnsureDirectoryExists();

                var fileName = Path.GetFileName(path);
                var folderName = Path.GetFileNameWithoutExtension(fileName);
                var destFolderPath = Path.Combine(_pluginsPath, folderName);

                var blockReason = App.GetService<LightweightPluginService>()
                    .GetInstallBlockReason(folderName, fileName);
                if (blockReason != null)
                {
                    StatusMessage = blockReason;
                    NotifyPluginOperationBlocked(blockReason);
                    return;
                }

                if (!Directory.Exists(destFolderPath))
                {
                    Directory.CreateDirectory(destFolderPath);
                }

                var destPath = Path.Combine(destFolderPath, fileName);
                File.Copy(path, destPath, true);

                var configPath = Path.Combine(destFolderPath, "config.ini");
                if (!File.Exists(configPath))
                {
                    string configContent =
                        $"[General]\r\nFile = {fileName}\r\nName = {folderName}\r\nDescription = Auto-generated config\r\n";
                    File.WriteAllText(configPath, configContent, Encoding.UTF8);
                }

                StatusMessage = string.Format("Plugin_AddPlugin_Format".GetLocalized(), folderName);
                LoadPlugins();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format("Plugin_AddFailed_Format".GetLocalized(), ex.Message);
        }
    }

    private void TogglePlugin(PluginItem? item)
    {
        if (item == null || !File.Exists(item.FullPath)) return;

        bool enabling = item.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

        if (enabling)
        {
            var versionStatus = App.GetService<PluginGameVersionGuard>()
                .Inspect(item.DirectoryPath, item.ConfigFilePath);
            item.IsVersionBlocked = versionStatus.IsBlocked;
            item.VersionBlockMessage = versionStatus.BlockMessage;

            if (versionStatus.IsBlocked)
            {
                StatusMessage = versionStatus.BlockMessage;
                NotifyPluginOperationBlocked(versionStatus.BlockMessage);
                item.RefreshState();
                return;
            }

            var constraint = App.GetService<ConstraintService>();
            if (constraint.IsRestricted &&
                LightweightPluginService.IsMainPluginPackage(Path.GetFileName(item.DirectoryPath),
                    Path.GetFileName(item.FullPath)))
            {
                StatusMessage = constraint.BlockMessage;
                NotifyPluginOperationBlocked(constraint.BlockMessage);
                item.RefreshState();
                return;
            }

            var blockReason = App.GetService<LightweightPluginService>()
                .GetInstallBlockReason(null, Path.GetFileName(item.FullPath));
            if (blockReason != null)
            {
                StatusMessage = blockReason;
                NotifyPluginOperationBlocked(blockReason);
                item.RefreshState();
                return;
            }
        }

        string newPath = string.Empty;

        try
        {
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
                    StatusMessage = "Plugin_UnrecognizedFile".GetLocalized();
                    return;
                }

                newPath = item.FullPath + ".disabled";
                targetState = false;
            }

            if (File.Exists(newPath))
            {
                StatusMessage = "Plugin_AddFailedAlreadyExists".GetLocalized();
                item.RefreshState();
                return;
            }

            File.Move(item.FullPath, newPath);
            item.FullPath = newPath;
            item.FileName = Path.GetFileName(newPath);
            item.IsEnabled = targetState;

            StatusMessage = string.Format("Plugin_StateChanged_Format".GetLocalized(),
                targetState ? "Common_Enable".GetLocalized() : "Common_Disable".GetLocalized(), item.DisplayName);
        }
        catch (Exception ex)
        {
            var lockedFile = FileLockHelper.FindLockedFile(item.FullPath, newPath);
            if (lockedFile != null)
            {
                StatusMessage = FileLockHelper.GetLockedFileMessage(lockedFile);
                NotifyPluginLockedFile(lockedFile);
                item.RefreshState();
                return;
            }

            StatusMessage = string.Format("Plugin_SwitchStateFailed_Format".GetLocalized(), ex.Message);
            item.RefreshState();
        }
    }

    private static void NotifyPluginLockedFile(string lockedFilePath)
    {
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            FileLockHelper.GetLockedFileTitle(),
            FileLockHelper.GetLockedFileMessage(lockedFilePath),
            NotificationType.Error,
            8000));
    }

    private static void NotifyPluginOperationBlocked(string message)
    {
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "AdminWarningTitle".GetLocalized(),
            message,
            NotificationType.Warning,
            5000));
    }

    private void DeletePlugin(PluginItem? item)
    {
        if (item == null) return;

        if (Directory.Exists(item.DirectoryPath))
        {
            var lockedFile = FileLockHelper.FindLockedFileInDirectory(item.DirectoryPath);
            if (lockedFile != null)
            {
                StatusMessage = FileLockHelper.GetLockedFileMessage(lockedFile);
                NotifyPluginLockedFile(lockedFile);
                return;
            }
        }

        try
        {
            if (Directory.Exists(item.DirectoryPath))
            {
                Directory.Delete(item.DirectoryPath, true);
                Plugins.Remove(item);
                UpdateIsEmpty();
                StatusMessage = string.Format("Plugin_Deleted_Format".GetLocalized(), item.DisplayName);
            }
        }
        catch (Exception ex)
        {
            var lockedFile = FileLockHelper.FindLockedFileInDirectory(item.DirectoryPath);
            if (lockedFile != null)
            {
                StatusMessage = FileLockHelper.GetLockedFileMessage(lockedFile);
                NotifyPluginLockedFile(lockedFile);
                return;
            }

            StatusMessage = string.Format("Common_DeleteFailed_Format".GetLocalized(), ex.Message);
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
                StatusMessage = "Plugin_RenameFailedExists".GetLocalized();
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

            StatusMessage = "Plugin_RenameSuccess".GetLocalized();
        }
        catch (Exception ex)
        {
            var lockedFile = FileLockHelper.FindLockedFileInDirectory(item.DirectoryPath);
            if (lockedFile != null)
            {
                StatusMessage = FileLockHelper.GetLockedFileMessage(lockedFile);
                NotifyPluginLockedFile(lockedFile);
                return;
            }

            StatusMessage = string.Format("Plugin_RenameFailed_Format".GetLocalized(), ex.Message);
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

        StatusMessage = "Plugin_ListSorted".GetLocalized();
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
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}