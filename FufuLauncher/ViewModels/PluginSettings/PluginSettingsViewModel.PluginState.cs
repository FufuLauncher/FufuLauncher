/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    #region 插件状态与路径管理

    private string GetMainPluginFolderName() =>
        IsLightweightMode
            ? LightweightPluginService.LitePluginFolderName
            : LightweightPluginService.MainPluginFolderName;

    private string GetMainPluginDirectory() =>
        IsLightweightMode ? LightweightPluginService.LitePluginDir : LightweightPluginService.MainPluginDir;

    private string GetMainPluginEnabledPath() =>
        IsLightweightMode ? LightweightPluginService.LitePluginDllPath : LightweightPluginService.MainPluginDllPath;

    private string GetMainPluginDisabledPath() =>
        IsLightweightMode
            ? LightweightPluginService.FindLitePluginDisabledPath() ?? LightweightPluginService.LitePluginDisabledPath
            : LightweightPluginService.FindMainPluginDisabledPath() ?? LightweightPluginService.MainPluginDisabledPath;

    private bool _isMainPluginEnabled;
    private bool _mainPluginVersionBlocked;
    private string _mainPluginVersionBlockMessage = string.Empty;
    private bool _fpsPluginVersionBlocked;
    private string _fpsPluginVersionBlockMessage = string.Empty;

    public bool IsMainPluginEnabled
    {
        get => _isMainPluginEnabled;
        set
        {
            if (_isMainPluginEnabled != value)
            {
                ChangeMainPluginState(value);
            }
        }
    }

    private bool _isFpsPluginEnabled;

    public bool IsFpsPluginEnabled
    {
        get => _isFpsPluginEnabled;
        set
        {
            if (_isFpsPluginEnabled != value)
            {
                ChangeFpsPluginState(value);
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility SettingsOverlayVisibility =>
        (SelectedPluginIndex == 0 && !_isMainPluginEnabled) || (SelectedPluginIndex == 1 && !_isFpsPluginEnabled)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool IsSettingsInteractable => (!_deferConfigurationLoading || IsConfigurationReady) &&
                                          ((SelectedPluginIndex == 0 && _isMainPluginEnabled) ||
                                           (SelectedPluginIndex == 1 && _isFpsPluginEnabled));

    public string OverlayWarningText
    {
        get
        {
            if (SelectedPluginIndex == 0)
            {
                if (_mainPluginVersionBlocked) return _mainPluginVersionBlockMessage;

                return IsLightweightMode
                    ? "LightweightMode_LiteDisabledOverlay".GetLocalized()
                    : "PluginSettings_MainPluginDisabledOverlay".GetLocalized();
            }

            if (SelectedPluginIndex == 1)
            {
                if (_fpsPluginVersionBlocked) return _fpsPluginVersionBlockMessage;

                return "PluginSettings_FpsPluginDisabledOverlay".GetLocalized();
            }

            return string.Empty;
        }
    }

    private static string GetFpsPluginDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS");

    private static string GetFpsPluginEnabledPath() =>
        Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.dll");

    private static string GetFpsPluginDisabledPath()
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.dll.disabled");
        if (File.Exists(fullPath)) return fullPath;

        return Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.disabled");
    }

    private void CheckPluginStates()
    {
        RefreshGameVersionBlockState();

        string fpsEnabledPath = GetFpsPluginEnabledPath();
        string fpsDisabledPath = GetFpsPluginDisabledPath();

        string mainEnabledPath = GetMainPluginEnabledPath();
        string mainDisabledPath = GetMainPluginDisabledPath();

        if (File.Exists(mainEnabledPath) && File.Exists(mainDisabledPath))
        {
            try
            {
                File.Delete(mainDisabledPath);
            }
            catch
            {
            }
        }

        _isMainPluginEnabled = File.Exists(mainEnabledPath);
        OnPropertyChanged(nameof(IsMainPluginEnabled));

        if (File.Exists(fpsEnabledPath) && File.Exists(fpsDisabledPath))
        {
            try
            {
                File.Delete(fpsDisabledPath);
            }
            catch
            {
            }
        }

        bool fpsEnabled = File.Exists(fpsEnabledPath);

        if (_isFpsPluginEnabled != fpsEnabled)
        {
            _isFpsPluginEnabled = fpsEnabled;
            OnPropertyChanged(nameof(IsFpsPluginEnabled));
        }

        RefreshUIState();
    }


    private void RefreshGameVersionBlockState()
    {
        var guard = App.GetService<PluginGameVersionGuard>();

        var mainStatus = guard.Inspect(GetMainPluginDirectory());
        if (mainStatus.IsBlocked && File.Exists(GetMainPluginEnabledPath()))
        {
            guard.TryDisablePlugin(GetMainPluginDirectory(), null, out _);
        }

        var fpsStatus = guard.Inspect(GetFpsPluginDirectory());
        if (fpsStatus.IsBlocked && File.Exists(GetFpsPluginEnabledPath()))
        {
            guard.TryDisablePlugin(GetFpsPluginDirectory(), null, out _);
        }

        _mainPluginVersionBlocked = mainStatus.IsBlocked;
        _mainPluginVersionBlockMessage = mainStatus.BlockMessage;
        _fpsPluginVersionBlocked = fpsStatus.IsBlocked;
        _fpsPluginVersionBlockMessage = fpsStatus.BlockMessage;

        OnPropertyChanged(nameof(OverlayWarningText));
    }

    private void ChangeMainPluginState(bool enable)
    {
        if (enable && ApplyGameVersionBlock(mainPlugin: true))
        {
            OnPropertyChanged(nameof(IsMainPluginEnabled));
            NotifyGameVersionBlocked(_mainPluginVersionBlockMessage);
            return;
        }

        if (enable && App.GetService<ConstraintService>().IsRestricted)
        {
            OnPropertyChanged(nameof(IsMainPluginEnabled));
            return;
        }

        if (enable && IsMainPluginDllMissing())
        {
            OnPropertyChanged(nameof(IsMainPluginEnabled));
            NotifyMainPluginDllMissing();
            return;
        }

        string mainDir = GetMainPluginDirectory();
        string enabledPath = GetMainPluginEnabledPath();
        string disabledPath = GetMainPluginDisabledPath();

        if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir);

        try
        {
            if (enable && File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath);
            }
            else if (!enable && File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath);
            }

            SetProperty(ref _isMainPluginEnabled, enable, nameof(IsMainPluginEnabled));
            RefreshUIState();
        }
        catch (Exception ex)
        {
            OnPropertyChanged(nameof(IsMainPluginEnabled));

            var lockedFile = FileLockHelper.FindLockedFile(enabledPath, disabledPath);
            if (lockedFile != null)
            {
                NotifyLockedPluginFile(lockedFile);
                return;
            }

            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "PluginSettings_StateSwitchFailedTitle".GetLocalized(),
                string.Format("PluginSettings_ModifyExtensionFailedDetail_Format".GetLocalized(), ex.Message),
                NotificationType.Error,
                6000
            ));
        }
    }

    private void ChangeFpsPluginState(bool enable)
    {
        string enabledPath = GetFpsPluginEnabledPath();
        string disabledPath = GetFpsPluginDisabledPath();

        if (enable && ApplyGameVersionBlock(mainPlugin: false))
        {
            OnPropertyChanged(nameof(IsFpsPluginEnabled));
            NotifyGameVersionBlocked(_fpsPluginVersionBlockMessage);
            return;
        }

        if (enable && IsFpsPluginDllMissing())
        {
            OnPropertyChanged(nameof(IsFpsPluginEnabled));
            NotifyFpsPluginDllMissing();
            return;
        }

        try
        {
            if (enable && File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath);
            }
            else if (!enable && File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath);
            }

            SetProperty(ref _isFpsPluginEnabled, enable, nameof(IsFpsPluginEnabled));
            RefreshUIState();
        }
        catch (Exception ex)
        {
            var lockedFile = FileLockHelper.FindLockedFile(enabledPath, disabledPath);
            if (lockedFile != null)
            {
                NotifyLockedPluginFile(lockedFile);
                return;
            }

            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "PluginSettings_StateSwitchFailedTitle".GetLocalized(),
                string.Format("PluginSettings_ModifyExtensionFailedDetail_Format".GetLocalized(), ex.Message),
                NotificationType.Error,
                6000
            ));
        }
    }

    private bool ApplyGameVersionBlock(bool mainPlugin)
    {
        var guard = App.GetService<PluginGameVersionGuard>();
        var status = mainPlugin
            ? guard.Inspect(GetMainPluginDirectory())
            : guard.Inspect(GetFpsPluginDirectory());

        if (mainPlugin)
        {
            _mainPluginVersionBlocked = status.IsBlocked;
            _mainPluginVersionBlockMessage = status.BlockMessage;
        }
        else
        {
            _fpsPluginVersionBlocked = status.IsBlocked;
            _fpsPluginVersionBlockMessage = status.BlockMessage;
        }

        OnPropertyChanged(nameof(OverlayWarningText));
        return status.IsBlocked;
    }

    private static void NotifyGameVersionBlocked(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "PluginGameVersion_BlockedTitle".GetLocalized(),
            message,
            NotificationType.Warning,
            6000));
    }

    private static void NotifyLockedPluginFile(string lockedFilePath)
    {
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            FileLockHelper.GetLockedFileTitle(),
            FileLockHelper.GetLockedFileMessage(lockedFilePath),
            NotificationType.Error,
            8000));
    }

    private void NotifyMainPluginDllMissing()
    {
        bool lightweight = IsLightweightMode;
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            lightweight
                ? "LightweightMode_LiteMissing_Title".GetLocalized()
                : "Plugin_MainMissing_Title".GetLocalized(),
            lightweight
                ? "LightweightMode_LiteMissing_Content".GetLocalized()
                : "Plugin_MainMissing_Content".GetLocalized(),
            NotificationType.Error,
            6000));
    }

    private static void NotifyFpsPluginDllMissing()
    {
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "Fps_Missing_Title".GetLocalized(),
            "Fps_Missing_Content".GetLocalized(),
            NotificationType.Error,
            6000));
    }

    public void RefreshPluginStates()
    {
        CheckPluginStates();
    }

    private void RefreshUIState()
    {
        OnPropertyChanged(nameof(SettingsOverlayVisibility));
        OnPropertyChanged(nameof(IsSettingsInteractable));
        OnPropertyChanged(nameof(OverlayWarningText));
        UpdatePaths();
    }

    private void UpdatePaths()
    {
        bool isLightweightMain = SelectedPluginIndex == 0 && IsLightweightMode;
        string subDir = SelectedPluginIndex == 0
            ? GetMainPluginFolderName()
            : "FPS";
        _pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", subDir);

        if (isLightweightMain)
        {
            _iniPath = LightweightPluginService.LitePluginConfigPath;
            _dllPath = LightweightPluginService.FindLitePluginDisabledPath() ??
                       LightweightPluginService.LitePluginDllPath;
        }
        else
        {
            _iniPath = Path.Combine(_pluginDir, "config.ini");
            if (SelectedPluginIndex == 0)
            {
                string mainEnabledPath = Path.Combine(_pluginDir, "FufuLauncher.UnlockerIsland.dll");
                string mainDisabledPath = Path.Combine(_pluginDir, "FufuLauncher.UnlockerIsland.disabled");
                _dllPath = File.Exists(mainDisabledPath) ? mainDisabledPath : mainEnabledPath;
            }
            else
            {
                string fpsEnabledPath = Path.Combine(_pluginDir, "FPS.dll");
                string fpsDisabledPath = GetFpsPluginDisabledPath();
                _dllPath = File.Exists(fpsDisabledPath) ? fpsDisabledPath : fpsEnabledPath;
            }
        }

        _presetsDir = PluginPresetStorage.GetDirectory(subDir);

        if (!string.IsNullOrEmpty(_iniPath))
        {
            _iniFile = new IniFile(_iniPath);
        }
        else
        {
            _iniFile = null;
        }

        if (!Directory.Exists(_presetsDir))
        {
            try
            {
                Directory.CreateDirectory(_presetsDir);
            }
            catch (UnauthorizedAccessException)
            {
                _presetsDir = PluginPresetStorage.GetFallbackDirectory(subDir);
                Directory.CreateDirectory(_presetsDir);
            }
        }
    }

    public bool IsMainPluginDllMissing()
    {
        return !File.Exists(GetMainPluginEnabledPath()) && !File.Exists(GetMainPluginDisabledPath());
    }

    public bool IsFpsPluginDllMissing()
    {
        return !File.Exists(GetFpsPluginEnabledPath()) && !File.Exists(GetFpsPluginDisabledPath());
    }

    public bool IsPluginCorrupted()
    {
        if (File.Exists(_dllPath))
        {
            var fileInfo = new FileInfo(_dllPath);
            return fileInfo.Length < 10 * 1024;
        }

        return false;
    }

    #endregion
}