/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Models;

public class PluginItem : INotifyPropertyChanged
{
    private string _fileName;
    private string _displayName;
    private string _developer;
    private string _description;
    private string _fullPath;
    private string _directoryPath;
    private string? _configFilePath;
    private bool _isEnabled;
    private bool _isVersionBlocked;
    private string _versionBlockMessage = string.Empty;
    private long _fileSize;
    private DateTime _dateModified;

    public string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            OnPropertyChanged();
        }
    }

    public string Developer
    {
        get => _developer;
        set
        {
            _developer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDeveloper));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            _description = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDescription));
        }
    }

    public string FullPath
    {
        get => _fullPath;
        set
        {
            _fullPath = value;
            OnPropertyChanged();
        }
    }

    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            _directoryPath = value;
            OnPropertyChanged();
        }
    }

    public string? ConfigFilePath
    {
        get => _configFilePath;
        set
        {
            _configFilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConfig));
            OnPropertyChanged(nameof(ConfigVisibility));
        }
    }

    public bool HasConfig => !string.IsNullOrEmpty(ConfigFilePath);

    public bool HasDeveloper => !string.IsNullOrWhiteSpace(Developer);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public Visibility ConfigVisibility => HasConfig ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeveloperVisibility => HasDeveloper ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DescriptionVisibility => HasDescription ? Visibility.Visible : Visibility.Collapsed;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsVersionBlocked
    {
        get => _isVersionBlocked;
        set
        {
            if (_isVersionBlocked == value) return;
            _isVersionBlocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VersionBlockVisibility));
        }
    }

    public string VersionBlockMessage
    {
        get => _versionBlockMessage;
        set
        {
            if (_versionBlockMessage == value) return;
            _versionBlockMessage = value;
            OnPropertyChanged();
        }
    }

    public Visibility VersionBlockVisibility =>
        IsVersionBlocked ? Visibility.Visible : Visibility.Collapsed;

    public long FileSize
    {
        get => _fileSize;
        set
        {
            _fileSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileSizeDisplay));
        }
    }

    public DateTime DateModified
    {
        get => _dateModified;
        set
        {
            _dateModified = value;
            OnPropertyChanged();
        }
    }

    public string FileSizeDisplay => $"{(FileSize / 1024.0):F2} KB";

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsEnabled));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}