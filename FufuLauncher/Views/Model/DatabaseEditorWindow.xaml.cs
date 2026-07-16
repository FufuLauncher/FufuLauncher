/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Data.Entities;

namespace FufuLauncher.Views
{
    public class SettingItem : INotifyPropertyChanged
    {
        private string _key;
        private string _value;

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(nameof(Key)); }
        }
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(nameof(Value)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed partial class DatabaseEditorWindow : Window
    {
        private readonly LocalSettingsRepository _repository;
        public ObservableCollection<SettingItem> SettingsItems { get; } = new();

        public DatabaseEditorWindow()
        {
            InitializeComponent();

            _repository = App.GetService<LocalSettingsRepository>();

            SettingsListView.ItemsSource = SettingsItems;
            LoadData();
        }

        private void LoadData()
        {
            SettingsItems.Clear();

            try
            {
                var entities = _repository.GetAllSettingEntitiesAsync().GetAwaiter().GetResult();
                foreach (var entity in entities)
                {
                    SettingsItems.Add(new SettingItem { Key = entity.Key, Value = entity.Value ?? "" });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取数据库失败: {ex.Message}");
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadData();

        private void OnDeleteDbClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dbPath = Helpers.AppPaths.LocalSettingsDb;
                if (File.Exists(dbPath))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    File.Delete(dbPath);
                    SettingsItems.Clear();

                    ShowDialog("成功", "数据库文件已成功删除");
                }
            }
            catch (Exception ex)
            {
                ShowDialog("失败", $"无法删除文件，可能应用正在占用它\n{ex.Message}");
            }
        }

        private void OnDeleteItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is SettingItem item)
            {
                SettingsItems.Remove(item);
            }
        }

        private void OnAddNewItemClick(object sender, RoutedEventArgs e)
        {
            SettingsItems.Add(new SettingItem { Key = "NewKey", Value = "" });
        }

        private async void OnSaveChangesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var entities = SettingsItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .Select(item => new SettingEntity { Key = item.Key, Value = item.Value ?? "" })
                    .ToList();

                await _repository.ReplaceAllSettingsAsync(entities);
                ShowDialog("成功", "所有的更改已保存到数据库");
            }
            catch (Exception ex)
            {
                ShowDialog("失败", ex.Message);
            }
        }

        private async void ShowDialog(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
