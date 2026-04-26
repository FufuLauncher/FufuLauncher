using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
        private readonly string _dbPath;
        public ObservableCollection<SettingItem> SettingsItems { get; } = new();

        public DatabaseEditorWindow()
        {
            InitializeComponent();
            
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher/ApplicationData");
            _dbPath = Path.Combine(folderPath, "LocalSettings.db");
            
            SettingsListView.ItemsSource = SettingsItems;
            LoadData();
        }

        private void LoadData()
        {
            SettingsItems.Clear();
            if (!File.Exists(_dbPath)) return;

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT [Key], [Value] FROM Settings";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    var val = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    SettingsItems.Add(new SettingItem { Key = key, Value = val });
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
                if (File.Exists(_dbPath))
                {
                    GC.Collect(); 
                    GC.WaitForPendingFinalizers();
                    
                    File.Delete(_dbPath);
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

        private void OnSaveChangesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                
                var clearCmd = connection.CreateCommand();
                clearCmd.CommandText = "DELETE FROM Settings";
                clearCmd.ExecuteNonQuery();

                foreach (var item in SettingsItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Key)) continue;

                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO Settings ([Key], [Value]) VALUES ($key, $value)";
                    insertCmd.Parameters.AddWithValue("$key", item.Key);
                    insertCmd.Parameters.AddWithValue("$value", item.Value ?? "");
                    insertCmd.ExecuteNonQuery();
                }
                
                transaction.Commit();
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