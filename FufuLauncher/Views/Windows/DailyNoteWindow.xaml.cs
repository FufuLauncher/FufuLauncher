/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views
{
    public class MaterialGroup
    {
        public string CategoryName
        {
            get;
            set;
        }

        public ObservableCollection<MaterialItem> Items
        {
            get;
            set;
        } = new();
    }

    public class MaterialItem
    {
        public string? Name
        {
            get;
            set;
        }

        public ImageSource IconImage
        {
            get;
            set;
        }

        public string? DomainName
        {
            get;
            set;
        }

        public List<ImageSource> Materials
        {
            get;
            set;
        } = new();
    }

    public class ActivityItem
    {
        public string Title
        {
            get;
            set;
        }

        public string Subtitle
        {
            get;
            set;
        }

        public ImageSource Image
        {
            get;
            set;
        }

        public string Countdown
        {
            get;
            set;
        }
    }

    internal class RawMaterialItem
    {
        public string name
        {
            get;
            set;
        }

        public string icon
        {
            get;
            set;
        }

        public string domain
        {
            get;
            set;
        }

        public List<string> materials
        {
            get;
            set;
        }
    }

    internal class RawActivityItem
    {
        public string title
        {
            get;
            set;
        }

        public string subtitle
        {
            get;
            set;
        }

        public string img
        {
            get;
            set;
        }

        public string time
        {
            get;
            set;
        }
    }

    public sealed partial class DailyNoteWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<MaterialGroup> MaterialGroups
        {
            get;
        } = new();

        public ObservableCollection<ActivityItem> Activities
        {
            get;
        } = new();

        private string _todayDate = "---";

        public string TodayDate
        {
            get => _todayDate;
            set
            {
                _todayDate = value;
                OnPropertyChanged(nameof(TodayDate));
            }
        }

        private string _statusText = "DailyNote_Initializing".GetLocalized();

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private string _birthdayRoleName;

        public string? BirthdayRoleName
        {
            get => _birthdayRoleName;
            set
            {
                _birthdayRoleName = value;
                OnPropertyChanged(nameof(BirthdayRoleName));
                UpdateBirthdayVisibility();
            }
        }

        private ImageSource _birthdayRoleIcon;

        public ImageSource BirthdayRoleIcon
        {
            get => _birthdayRoleIcon;
            set
            {
                _birthdayRoleIcon = value;
                OnPropertyChanged(nameof(BirthdayRoleIcon));
            }
        }

        private Visibility _birthdayInfoVisibility = Visibility.Collapsed;

        public Visibility BirthdayInfoVisibility
        {
            get => _birthdayInfoVisibility;
            set
            {
                _birthdayInfoVisibility = value;
                OnPropertyChanged(nameof(BirthdayInfoVisibility));
            }
        }

        private bool _isLoadingMaterials = true;

        public bool IsLoadingMaterials
        {
            get => _isLoadingMaterials;
            set
            {
                _isLoadingMaterials = value;
                OnPropertyChanged(nameof(IsLoadingMaterials));
            }
        }

        private bool _isLoadingActivities = true;

        public bool IsLoadingActivities
        {
            get => _isLoadingActivities;
            set
            {
                _isLoadingActivities = value;
                OnPropertyChanged(nameof(IsLoadingActivities));
            }
        }

        private bool _isWebViewInitialized;
        private bool _isRefreshing;

        public DailyNoteWindow()
        {
            InitializeComponent();
            Activated += DailyNoteWindow_Activated;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}