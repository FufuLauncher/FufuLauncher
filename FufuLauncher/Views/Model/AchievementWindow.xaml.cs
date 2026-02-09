using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow : Window
{
    public AchievementViewModel ViewModel { get; } = new();

    private readonly string _workFilePath;
    private readonly string _assetsFilePath;
    private bool _isDataLoaded;
    private HttpListener _listener;
    private bool _keepRunning = true;
    private bool _isBatchProcessing;
    private readonly string _archivesDir;
    private readonly string _profileRecordPath;
    private string _currentProfileName = "默认存档";
    public string CurrentProfileName
    {
        get => _currentProfileName;
        set
        {
            if (_currentProfileName != value)
            {
                _currentProfileName = value;
                Bindings.Update(); 
            }
        }
    }

    public AchievementWindow()
    {
        InitializeComponent();
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        string docPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu");
        
        if (!Directory.Exists(docPath)) Directory.CreateDirectory(docPath);
        _archivesDir = Path.Combine(docPath, "archives");
        if (!Directory.Exists(_archivesDir)) Directory.CreateDirectory(_archivesDir);
    
        _profileRecordPath = Path.Combine(docPath, "current_profile.txt");
        
        if (File.Exists(_profileRecordPath))
        {
            CurrentProfileName = File.ReadAllText(_profileRecordPath).Trim();
        }
        else
        {
            CurrentProfileName = "未命名存档";
        }
        
        _workFilePath = Path.Combine(docPath, "achievements.json");
        _assetsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "genshin_achievements_linked.json");

        LoadData();
        StartLocalServer();
        Closed += (s, e) => { _keepRunning = false; _listener?.Close(); };
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }
    
    private async Task SyncWithAssetsDatabase()
{
    if (_isBatchProcessing) return;
    _isBatchProcessing = true;
    ViewModel.IsLoading = true;
    ViewModel.StatusMessage = "正在对比数据库版本...";

    try
    {
        if (!File.Exists(_assetsFilePath))
        {
            await ShowDialogAsync("错误", "找不到内置数据库文件 (Assets/genshin_achievements_linked.json)");
            return;
        }
        
        string masterJson = await File.ReadAllTextAsync(_assetsFilePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        var masterCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(masterJson, options);
        
        if (!File.Exists(_workFilePath))
        {
            File.Copy(_assetsFilePath, _workFilePath, true);
            LoadData();
            await ShowDialogAsync("同步完成", "未发现旧存档，已直接初始化为最新数据库。");
            return;
        }

        var userJson = await File.ReadAllTextAsync(_workFilePath);
        var userCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(userJson, options) ?? new List<AchievementCategory>();

        var addedCount = 0;
        var newCategoriesCount = 0;
        
        foreach (var masterCat in masterCategories)
        {
            var userCat = userCategories.FirstOrDefault(c => c.Name == masterCat.Name);

            if (userCat == null)
            {
                userCategories.Add(masterCat);
                newCategoriesCount++;
                addedCount += masterCat.Achievements.Count;
            }
            else
            {
                var userAchievementIds = new HashSet<int>(userCat.Achievements.Select(a => a.Id));

                foreach (var masterItem in masterCat.Achievements)
                {
                    if (!userAchievementIds.Contains(masterItem.Id))
                    {
                        userCat.Achievements.Add(masterItem);
                        addedCount++;
                    }
                }
            }
        }
        
        if (addedCount > 0)
        {
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string newJsonContent = JsonSerializer.Serialize(userCategories, writeOptions);
            await File.WriteAllTextAsync(_workFilePath, newJsonContent);
            
            LoadData();
            
            string msg = $"同步成功！\n新增分类: {newCategoriesCount} 个\n新增成就: {addedCount} 个";
            await ShowDialogAsync("数据库更新", msg);
        }
        else
        {
            ViewModel.StatusMessage = "当前已是最新数据库";
            await ShowDialogAsync("数据库更新", "您的存档已经是最新版本，无需更新。");
        }
    }
    catch (Exception ex)
    {
        await ShowDialogAsync("更新失败", $"同步过程中发生错误：\n{ex.Message}");
    }
    finally
    {
        ViewModel.IsLoading = false;
        _isBatchProcessing = false;
        if (_isDataLoaded) CalculateGlobalStats(); 
    }
}
    
    private async void OnUpdateDbClick(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "更新成就数据库",
            Content = "此操作将读取软件内置的最新成就列表，并将缺失的新成就添加到您当前的存档中。\n\n您的现有进度（已完成的成就）将保留不会丢失。\n\n是否继续？",
            PrimaryButtonText = "开始更新",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await SyncWithAssetsDatabase();
        }
    }
    
    private async void OnArchiveManageClick(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_profileRecordPath) || CurrentProfileName == "未命名存档")
        {
            var nameResult = await ShowInputAsync("保存当前存档", "检测到当前存档未命名，在切换或新建前，请先为当前进度取一个名字：");
            if (string.IsNullOrWhiteSpace(nameResult)) return;

            SaveData(); 
            string targetPath = Path.Combine(_archivesDir, $"{nameResult}.json");
            File.Copy(_workFilePath, targetPath, true);
            File.WriteAllText(_profileRecordPath, nameResult);
            CurrentProfileName = nameResult;
            await ShowDialogAsync("保存成功", $"当前进度已保存为：{nameResult}");
        }
        else
        {
            SaveData();
            string currentBackupPath = Path.Combine(_archivesDir, $"{CurrentProfileName}.json");
            File.Copy(_workFilePath, currentBackupPath, true);
        }
        
        var rootPanel = new StackPanel { Spacing = 12, MinWidth = 340 };
        
        var btnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        btnContent.Children.Add(new FontIcon { Glyph = "\uE710", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 12 });
        btnContent.Children.Add(new TextBlock { Text = "新建空白存档" });
        var createBtn = new Button { Content = btnContent, HorizontalAlignment = HorizontalAlignment.Stretch };

        var listHeader = new TextBlock { Text = "现有存档列表:", Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };

        var listContainer = new StackPanel { Spacing = 8 };
        var scrollViewer = new ScrollViewer { Content = listContainer, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        rootPanel.Children.Add(createBtn);
        rootPanel.Children.Add(listHeader);
        rootPanel.Children.Add(scrollViewer);

        var dialog = new ContentDialog
        {
            Title = "存档管理",
            Content = rootPanel,
            CloseButtonText = "关闭",
            XamlRoot = Content.XamlRoot
        };

        void RefreshList()
        {
            listContainer.Children.Clear();
            
            var files = Directory.GetFiles(_archivesDir, "*.json")
                                 .Select(Path.GetFileNameWithoutExtension)
                                 .OrderBy(x => x)
                                 .ToList();
            
            if (files.Count == 0)
            {
                listContainer.Children.Add(new TextBlock 
                { 
                    Text = "暂无备份存档", 
                    Opacity = 0.5, 
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Margin = new Thickness(0, 20, 0, 0) 
                });
            }

            foreach (var file in files)
            {
                var itemGrid = new Grid 
                { 
                    ColumnDefinitions = 
                    { 
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, 
                        new ColumnDefinition { Width = GridLength.Auto } 
                    },
                    Margin = new Thickness(0, 0, 0, 4)
                };
                
                var switchBtn = new Button 
                { 
                    HorizontalAlignment = HorizontalAlignment.Stretch, 
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(10, 255, 255, 255)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10, 8, 10, 8)
                };
                
                bool isCurrent = file == CurrentProfileName;
                string displayText = isCurrent ? $"{file} (当前)" : file;
                
                var txtBlock = new TextBlock { Text = displayText, VerticalAlignment = VerticalAlignment.Center };
                if (isCurrent) 
                {
                    txtBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 255, 100)); // 高亮绿色
                    txtBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }
                
                switchBtn.Content = txtBlock;
                switchBtn.Click += async (_, _) => 
                {
                    if (isCurrent) return;
                    dialog.Hide();
                    await SwitchToArchive(file, false);
                };
                
                var deleteBtn = new Button 
                { 
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                
                ToolTipService.SetToolTip(deleteBtn, "删除此存档");

                deleteBtn.Content = new FontIcon 
                { 
                    Glyph = "\uE74D", 
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), 
                    FontSize = 14, 
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 80, 80)) 
                };
                
                if (isCurrent)
                {
                    deleteBtn.IsEnabled = false;
                    deleteBtn.Opacity = 0.3;
                }
                else
                {
                    
                    var confirmPanel = new StackPanel { Spacing = 10, Padding = new Thickness(10) };
                    confirmPanel.Children.Add(new TextBlock { Text = "确定要永久删除吗？", FontSize = 12 });

                    var confirmDeleteBtn = new Button 
                    { 
                        Content = "确认删除", 
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 50, 50)),
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        FontSize = 12
                    };

                    confirmPanel.Children.Add(confirmDeleteBtn);
                    
                    var flyout = new Flyout { Content = confirmPanel };
                    deleteBtn.Flyout = flyout;
                    
                    confirmDeleteBtn.Click += (_, _) =>
                    {
                        try 
                        {
                            string pathToDelete = Path.Combine(_archivesDir, $"{file}.json");
                            if (File.Exists(pathToDelete)) 
                            {
                                File.Delete(pathToDelete);
                            }
                            
                            flyout.Hide();
                            
                            RefreshList(); 
                        }
                        catch (Exception)
                        {
                            flyout.Hide();
                        }
                    };
                }

                Grid.SetColumn(switchBtn, 0);
                Grid.SetColumn(deleteBtn, 1);
                itemGrid.Children.Add(switchBtn);
                itemGrid.Children.Add(deleteBtn);
                
                listContainer.Children.Add(itemGrid);
            }
        }
        
        RefreshList();
        
        createBtn.Click += async (_, _) =>
        {
            dialog.Hide();
            var newName = await ShowInputAsync("新建存档", "请输入新存档的名称：");
            if (string.IsNullOrWhiteSpace(newName)) return;
            
            if (File.Exists(Path.Combine(_archivesDir, $"{newName}.json")))
            {
                await ShowDialogAsync("错误", "该存档名称已存在！");
                return;
            }

            await SwitchToArchive(newName, true);
        };

        await dialog.ShowAsync();
    }

    private async Task SwitchToArchive(string profileName, bool isNew)
    {
        try
        {
            ViewModel.IsLoading = true;
            ViewModel.StatusMessage = "正在切换存档...";
            
            if (isNew)
            {
                if (File.Exists(_assetsFilePath))
                {
                    File.Copy(_assetsFilePath, _workFilePath, true);
                }
                else
                {
                    File.WriteAllText(_workFilePath, "[]");
                }
            }
            else
            {
                string sourceArchive = Path.Combine(_archivesDir, $"{profileName}.json");
                if (!File.Exists(sourceArchive))
                {
                    await ShowDialogAsync("错误", "找不到目标存档文件！");
                    return;
                }
                File.Copy(sourceArchive, _workFilePath, true);
            }
            
            File.WriteAllText(_profileRecordPath, profileName);
            CurrentProfileName = profileName;
            
            LoadData(); 
            
            if (isNew)
            {
                string newBackupPath = Path.Combine(_archivesDir, $"{profileName}.json");
                File.Copy(_workFilePath, newBackupPath, true);
            }

            ViewModel.StatusMessage = $"已切换至：{profileName}";
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("切换失败", ex.Message);
            LoadData();
        }
    }

    private async Task<string> ShowInputAsync(string title, string instruction)
    {
        var inputTextBox = new TextBox 
        { 
            PlaceholderText = "请输入名称...",
            MaxLength = 20
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Spacing = 10,
                Children = { new TextBlock { Text = instruction }, inputTextBox }
            },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string text = inputTextBox.Text.Trim();
            if (text.IndexOfAny(invalid) >= 0)
            {
                await ShowDialogAsync("名称无效", "名称包含非法字符，请重试。");
                return null;
            }
            return text;
        }
        return null;
    }
    
    [DllImport("user32.dll")]
    private static extern bool SetWindowText(IntPtr hWnd, string text);
    
    private void PlayEntranceAnimation(UIElement target)
    {
        Storyboard sb = new();
        
        DoubleAnimation translateAnim = new()
        {
            From = 30,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(400)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(translateAnim, target.RenderTransform);
        Storyboard.SetTargetProperty(translateAnim, "Y");
        
        DoubleAnimation opacityAnim = new()
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300))
        };
        Storyboard.SetTarget(opacityAnim, target);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");
        
        sb.Children.Add(translateAnim);
        sb.Children.Add(opacityAnim);
        sb.Begin();
    }
    
    
    private async void StartLocalServer()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:15655/");
            _listener.Start();

            await Task.Run(async () =>
            {
                while (_keepRunning)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        _ = HandleIncomingFile(context);
                    }
                    catch { break; }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine("端口开启失败: " + ex.Message);
        }
    }
    
    private async Task HandleIncomingFile(HttpListenerContext context)
    {
        if (_isBatchProcessing)
        {
            context.Response.StatusCode = 503;
            context.Response.Close();
            return;
        }

        try
        {
            if (context.Request.HttpMethod == "POST")
            {
                string tempFile = Path.GetTempFileName(); 
            
                using (var input = context.Request.InputStream)
                using (var output = File.Create(tempFile))
                {
                    await input.CopyToAsync(output);
                }
            
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await RunImportLogic(tempFile);
                
                    try { File.Delete(tempFile); } catch { }
                });
            
                byte[] b = "Import Started"u8.ToArray();
                context.Response.StatusCode = 200;
                context.Response.OutputStream.Write(b, 0, b.Length);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SearchText) ||
            e.PropertyName == nameof(ViewModel.HideCompleted) ||
            e.PropertyName == nameof(ViewModel.SelectedVersion))
        {
            ApplyFilters();
        }
        
        if (e.PropertyName == nameof(AchievementViewModel.IsCategoryGridMode))
        {
            if (ViewModel.IsCategoryGridMode)
            {
                PlayEntranceAnimation(CategoryGridView);
            }
            else
            {
                PlayEntranceAnimation(DetailView);
            }
        }
    }

    private void LoadData()
    {
        ViewModel.IsLoading = true;
        ViewModel.StatusMessage = "正在读取数据...";
        _isDataLoaded = false;

        try
        {
            if (!File.Exists(_workFilePath))
            {
                if (File.Exists(_assetsFilePath)) File.Copy(_assetsFilePath, _workFilePath);
                else { ViewModel.StatusMessage = "找不到源数据"; return; }
            }

            string jsonContent = File.ReadAllText(_workFilePath);
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var rawCategories = JsonSerializer.Deserialize<ObservableCollection<AchievementCategory>>(jsonContent, options);

            if (rawCategories != null)
            {
                ViewModel.Categories.Clear();
                
                foreach (var cat in rawCategories)
                {
                    var groupedList = new ObservableCollection<AchievementItem>();
                    
                    var groups = cat.Achievements.GroupBy(x => !string.IsNullOrEmpty(x.SeriesId) ? x.SeriesId : Guid.NewGuid().ToString());

                    foreach (var g in groups)
                    {
                        var items = g.OrderBy(x => x.StageIndex).ToList();

                        if (items.Count == 1)
                        {
                            var item = items.First();
                            SetupItemEvents(cat, item);
                            groupedList.Add(item);
                        }
                        else
                        {
                            var firstChild = items.First();
                            
                            var parentItem = new AchievementItem
                            {
                                Title = !string.IsNullOrEmpty(firstChild.SeriesMasterTitle) ? firstChild.SeriesMasterTitle : firstChild.Title,
                                Description = firstChild.Description,
                                Version = firstChild.Version,
                                ItemIconUrl = firstChild.ItemIconUrl,
                                SeriesId = firstChild.SeriesId,
                                Children = new ObservableCollection<AchievementItem>(items)
                            };
                            
                            foreach (var child in parentItem.Children)
                            {
                                SetupItemEvents(cat, child, parentItem);
                            }
                            
                            parentItem.RefreshGroupStatus();
                            groupedList.Add(parentItem);
                        }
                    }

                    cat.Achievements = groupedList;
                    cat.RefreshProgress();
                    ViewModel.Categories.Add(cat);
                }
                
                var versions = new HashSet<string> { "所有版本" };
                foreach (var cat in ViewModel.Categories)
                {
                    foreach (var item in cat.Achievements)
                    {
                        if(item.IsGroup)
                        {
                            foreach(var child in item.Children) if (!string.IsNullOrEmpty(child.Version)) versions.Add(child.Version);
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(item.Version)) versions.Add(item.Version);
                        }
                    }
                }
                
                ViewModel.AvailableVersions = new ObservableCollection<string>(versions.OrderBy(v => v));
                ViewModel.SelectedCategory = ViewModel.Categories.FirstOrDefault();
                
                ApplyFilters();
                
                ViewModel.StatusMessage = $"共 {ViewModel.Categories.Sum(c => c.TotalCount)} 个成就";
                CalculateGlobalStats();
            
                ViewModel.StatusMessage = $"数据加载完成";
                _isDataLoaded = true;
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"初始化失败: {ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            ViewModel.IsLoading = false;
        }
    }
    
    private void CalculateGlobalStats()
    {
        if (ViewModel.Categories == null) return;

        int totalPrimos = 0;
        int obtainedPrimos = 0;
        int totalCount = 0;
        int completedCount = 0;

        foreach (var cat in ViewModel.Categories)
        {
            foreach (var item in cat.Achievements)
            {
                if (item.IsGroup)
                {
                    foreach (var child in item.Children)
                    {
                        totalCount++;
                        totalPrimos += child.RewardValue;

                        if (child.IsCompleted)
                        {
                            completedCount++;
                            obtainedPrimos += child.RewardValue;
                        }
                    }
                }
                else
                {
                    totalCount++;
                    totalPrimos += item.RewardValue;

                    if (item.IsCompleted)
                    {
                        completedCount++;
                        obtainedPrimos += item.RewardValue;
                    }
                }
            }
        }
        
        ViewModel.PrimogemStatText = $"{obtainedPrimos} / {totalPrimos}";
        
        double percent = totalCount == 0 ? 0 : (double)completedCount / totalCount * 100;
        ViewModel.ProgressStatText = $"{completedCount} / {totalCount} ({percent:F1}%)";
        ViewModel.GlobalProgressPercent = percent;
    }

    private void SetupItemEvents(AchievementCategory cat, AchievementItem item, AchievementItem parent = null)
    {
        item.PropertyChanged += (s, e) =>
        {
            if (_isBatchProcessing) return;

            if (e.PropertyName == nameof(AchievementItem.IsCompleted))
            {
                parent?.RefreshGroupStatus();
                cat.RefreshProgress(); 
                CalculateGlobalStats();
                if (ViewModel.HideCompleted) ApplyFilters(); 
                SaveData(); 
            }
        };
    }

private void ApplyFilters()
{
    string search = ViewModel.SearchText?.Trim().ToLower();
    bool isGlobalSearch = !string.IsNullOrEmpty(search);
    
    IEnumerable<AchievementItem> sourceList;

    if (isGlobalSearch)
    {
        sourceList = ViewModel.Categories.SelectMany(c => c.Achievements);
    }
    else
    {
        if (ViewModel.SelectedCategory == null)
        {
            ViewModel.FilteredAchievements.Clear();
            return;
        }
        sourceList = ViewModel.SelectedCategory.Achievements;
    }
    
    var resultList = new List<AchievementItem>();
    bool isFilterVer = ViewModel.SelectedVersion != "所有版本" && !string.IsNullOrEmpty(ViewModel.SelectedVersion);
    
    foreach (var item in sourceList)
    {
        if (item.IsGroup)
        {
            bool matchGroup = false;
            
            if (isGlobalSearch)
            {
                if (item.Title != null && item.Title.ToLower().Contains(search)) matchGroup = true;
                else if (item.Children.Any(c => c.Description != null && c.Description.ToLower().Contains(search))) matchGroup = true;
            }
            else
            {
                matchGroup = true;
            }
            
            if (isFilterVer)
            {
                if (item.Version != ViewModel.SelectedVersion && !item.Children.Any(c => c.Version == ViewModel.SelectedVersion)) 
                    matchGroup = false;
            }
            
            if (ViewModel.HideCompleted)
            {
                if (item.Children.All(c => c.IsCompleted)) matchGroup = false;
            }

            if (matchGroup) resultList.Add(item);
        }
        else
        {
            bool match = true;
            
            if (isGlobalSearch)
            {
                if (!((item.Title != null && item.Title.ToLower().Contains(search)) || 
                      (item.Description != null && item.Description.ToLower().Contains(search))))
                    match = false;
            }

            if (ViewModel.HideCompleted && item.IsCompleted) match = false;

            if (isFilterVer && item.Version != ViewModel.SelectedVersion) match = false;

            if (match) resultList.Add(item);
        }
    }
    
    ViewModel.FilteredAchievements.Clear();
    foreach (var item in resultList) ViewModel.FilteredAchievements.Add(item);
}

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilters();
    private void OnToggleViewMode(object sender, RoutedEventArgs e) => ViewModel.IsCategoryGridMode = !ViewModel.IsCategoryGridMode;

    private void OnCategoryGridItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AchievementCategory cat)
        {
            ViewModel.SelectedCategory = cat;
            ViewModel.IsCategoryGridMode = false; 
            ApplyFilters();
        }
    }
    
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StatusMessage = "正在保存...";
        SaveData();
        ViewModel.StatusMessage = "数据已保存到文档目录";
    }
    
    private void OnSearchGuideClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AchievementItem item)
        {
            try
            {
                string keyword = WebUtility.UrlEncode(item.Title);
                string url = $"https://www.miyoushe.com/ys/search?keyword={keyword}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = "无法打开浏览器: " + ex.Message;
            }
        }
    }
    
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".txt");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        
        if (file != null)
        {
            await RunImportLogic(file.Path);
        }
    }

    private async void OnYaeImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string yaePath = Path.Combine(baseDir, "Assets", "Yae", "YaeAchievement.exe");

            if (!File.Exists(yaePath))
            {
                await ShowDialogAsync("找不到文件", $"未在以下路径找到导入工具：\n{yaePath}\n\n请重新安装本软件");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = yaePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(yaePath)
            };
            
            var process = Process.Start(psi);

            if (process != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000);

                        process.Refresh();

                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            SetWindowText(process.MainWindowHandle, "YaeAchievement");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"重命名窗口失败: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("启动失败", $"无法启动导入工具：\n{ex.Message}");
        }
    }
    
private async Task RunImportLogic(string filePath)
{
    if (_isBatchProcessing) return;
    _isBatchProcessing = true;
    
    var progressBar = new ProgressBar { Value = 0, Maximum = 100, Height = 10, Margin = new Thickness(0, 15, 0, 5) };
    var statusText = new TextBlock { Text = "正在准备读取数据...", FontSize = 13, Opacity = 0.8 };
    var stackPanel = new StackPanel { Width = 380, Spacing = 5 };
    stackPanel.Children.Add(statusText);
    stackPanel.Children.Add(progressBar);

    var progressDialog = new ContentDialog
    {
        Title = "正在导入成就",
        Content = stackPanel,
        CloseButtonText = null,
        XamlRoot = Content.XamlRoot
    };

    var dialogTask = progressDialog.ShowAsync();

    try
    {
        var lines = new List<string>();
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = (await sr.ReadLineAsync())!) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                }
            }
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            await ShowDialogAsync("读取失败", $"文件可能被占用或无法读取。\n{ex.Message}");
            return;
        }

        if (lines.Count == 0)
        {
            progressDialog.Hide();
            await ShowDialogAsync("空文件", "文件中没有内容。");
            return;
        }
        
        
        
        var result = await Task.Run(() => ParseAndImport(lines, (percent, msg) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                progressBar.Value = percent;
                statusText.Text = msg;
            });
        }));
        
        if (result.PendingUpdates.Count > 0)
        {
            statusText.Text = "正在应用更改";

            foreach (var update in result.PendingUpdates)
            {
                update.Item.CurrentProgress = update.Current;
                update.Item.MaxProgress = update.Max;
                
                if (update.ShouldComplete)
                {
                    update.Item.IsCompleted = true;
                }
            }
            
            foreach(var cat in ViewModel.Categories) cat.RefreshProgress();
            
            CalculateGlobalStats();
            SaveData();
            
            if (ViewModel.HideCompleted) ApplyFilters();
            
            ViewModel.StatusMessage = $"导入成功，导入 {result.UpdatedCount} 个成就";
        }
        else
        {
            ViewModel.StatusMessage = $"导入结束，没有新的变动";
        }
        
        progressDialog.Hide();
        await dialogTask;

        if (result.UpdatedCount > 0)
        {
            CalculateGlobalStats();
            SaveData();
            if (ViewModel.HideCompleted) ApplyFilters();
            ViewModel.StatusMessage = $"导入成功，导入 {result.UpdatedCount} 个成就";
        }

        string resultMsg = $"扫描行数: {result.TotalScanned}\n" +
                           $"跳过未完成: {result.SkippedIncomplete}\n" +
                           $"成功同步: {result.UpdatedCount}\n" +
                           $"已存在: {result.AlreadyDone}\n" +
                           $"无法识别: {result.FailedCount}\n\n" +
                           (result.Errors.Any() ? "部分未识别项:\n" + string.Join("\n", result.Errors.Take(3)) : "");

        await ShowDialogAsync("导入数据", resultMsg);
    }
    catch (Exception ex)
    {
        progressDialog.Hide();
        await ShowDialogAsync("错误", $"导入过程中发生异常：\n{ex.Message}");
    }
    finally
    {
        _isBatchProcessing = false;
    }
}
    
private ImportStats ParseAndImport(List<string> lines, Action<double, string> reportProgress)
{
    var stats = new ImportStats();
    var total = lines.Count;
    
    var nameMap = new Dictionary<string, List<AchievementItem>>();

    foreach (var cat in ViewModel.Categories)
    {
        foreach (var item in cat.Achievements)
        {
            if (item.IsGroup)
                foreach (var child in item.Children) AddToNameMap(nameMap, child);
            else
                AddToNameMap(nameMap, item);
        }
    }

    reportProgress(10, "正在分析进度数据...");
    
    for (int i = 0; i < total; i++)
    {
        if (i % 100 == 0) reportProgress(10 + (double)i / total * 80, $"正在处理第 {i}/{total} 行...");

        string line = lines[i];
        if (string.IsNullOrWhiteSpace(line)) continue;

        string[] parts;
        if (line.Contains('\t')) parts = line.Split('\t');
        else parts = line.Split(',');

        if (parts.Length < 7) continue;
        if (parts[0].Contains("ID") || parts[1].Contains("状态")) continue;

        string nameInCsv = parts[3];
        string descInCsv = parts[4];
        
        int.TryParse(parts[5], out int currentVal);
        int.TryParse(parts[6], out int maxVal); 
        bool isCompletedInCsv = parts[1].Trim() == "已完成";

        string cleanName = Normalize(nameInCsv);
        AchievementItem targetItem = null;

        if (!string.IsNullOrEmpty(cleanName) && nameMap.TryGetValue(cleanName, out var candidates))
        {
            if (candidates.Count == 1)
            {
                targetItem = candidates[0];
            }
            else
            {
                string targetDesc = Normalize(descInCsv);
                
                targetItem = candidates
                    .OrderBy(c => ComputeLevenshteinDistance(Normalize(c.Description), targetDesc))
                    .First();
            }
        }
        
        if (targetItem != null)
        {
            bool needUpdate = false;
            
            if (currentVal > targetItem.CurrentProgress) needUpdate = true;
            if (isCompletedInCsv && !targetItem.IsCompleted) needUpdate = true;

            if (needUpdate)
            {
                stats.PendingUpdates.Add(new AchievementUpdateData
                {
                    Item = targetItem,
                    ShouldComplete = isCompletedInCsv,
                    Current = currentVal,
                    Max = targetItem.MaxProgress > 0 ? targetItem.MaxProgress : maxVal
                });
                stats.UpdatedCount++;
            }
            else
            {
                stats.AlreadyDone++;
            }
        }
        else
        {
            stats.FailedCount++;
            if (stats.Errors.Count < 5) stats.Errors.Add($"[{parts[0]}] {nameInCsv} 匹配失败");
        }
    }

    reportProgress(95, "正在刷新界面状态...");
    
    DispatcherQueue.TryEnqueue(() =>
    {
        foreach (var cat in ViewModel.Categories)
        {
            foreach (var item in cat.Achievements)
            {
                if (item.IsGroup)
                {
                    item.RefreshGroupStatus();
                }
            }
        }
    });

    return stats;
}

private void AddToNameMap(Dictionary<string, List<AchievementItem>> map, AchievementItem item)
{
    string key = Normalize(item.Title);
    if (string.IsNullOrEmpty(key)) return;
    
    if (!map.ContainsKey(key))
    {
        map[key] = new List<AchievementItem>();
    }
    map[key].Add(item);
}

    private int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
    private string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var chars = input.Where(c => char.IsLetterOrDigit(c)).ToArray();
        return new string(chars).ToLowerInvariant();
    }
    
    private class ImportStats
    {
        public int TotalScanned { get; set; }
        public int SkippedIncomplete { get; set; }
        public int UpdatedCount { get; set; }
        public int AlreadyDone { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new();
        
        public List<AchievementUpdateData> PendingUpdates { get; set; } = new();
    }
    
    private class AchievementUpdateData
    {
        public AchievementItem Item { get; set; }
        public bool ShouldComplete { get; set; }
        public int Current { get; set; }
        public int Max { get; set; }
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
            CloseButtonText = "确定",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void SaveData()
    {
        if (!_isDataLoaded) return;
        try
        {
            var categoriesToSave = new List<AchievementCategory>();

            foreach (var uiCat in ViewModel.Categories)
            {
                var flatAchievements = new ObservableCollection<AchievementItem>();
                foreach (var item in uiCat.Achievements)
                {
                    if (item.IsGroup)
                    {
                        foreach (var child in item.Children) flatAchievements.Add(child);
                    }
                    else
                    {
                        flatAchievements.Add(item);
                    }
                }

                categoriesToSave.Add(new AchievementCategory
                {
                    Name = uiCat.Name,
                    IconUrl = uiCat.IconUrl,
                    Achievements = flatAchievements
                });
            }

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            var json = JsonSerializer.Serialize(categoriesToSave, options);
            File.WriteAllText(_workFilePath, json);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = "保存异常";
            Debug.WriteLine(ex);
        }
    }
}