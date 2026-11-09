using System.Collections.ObjectModel;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services;
using FufuLauncher.Services.MiHoYo;
using FufuLauncher.Services.Miyoushe;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace FufuLauncher.Views;

public sealed partial class MiyousheWindow : WindowEx
{
    private static MiyousheWindow? _instance;
    private readonly ObservableCollection<CommunityPost> _posts = [];
    private readonly CommunityLibrary _library = new(Path.Combine(AppPaths.DataDir, "miyoushe_library.json"));
    private readonly CancellationTokenSource _lifetime = new();

    private CancellationTokenSource _session = new(),
        _feedCancellation = new(),
        _readerCancellation = new(),
        _replyCancellation = new();

    private MiyousheClient _client = new();
    private IReadOnlyList<CommunityForum> _forums = [];
    private FeedRequest _feed = new(CommunityFeed.Forum);
    private string _feedCursor = "", _replyCursor = "";
    private CommunityPost? _post;

    private bool _initializing = true,
        _changingSelectors,
        _feedLoading,
        _replyLoading,
        _commentsLoaded,
        _closed,
        _verifying,
        _dialogOpen;

    private Func<Task>? _retry;
    private CommunityApiException? _lastApiError;
    private Task? _webInitialization;
    private string _articleDocumentUri = "";
    private int _selectedForumId = 26;
    private readonly Stack<PageState> _pageHistory = new();
    private bool _feedInitialized, _isPostOpen, _goingBack;
    private bool _refreshing, _readerLoading;
    private (string Id, int Game)? _readerTarget;
    private long _refreshFeedbackVersion;

    private sealed record Choice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SortChoice(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    public static void Show()
    {
        _instance ??= new MiyousheWindow();
        _instance.Activate();
    }

    public MiyousheWindow()
    {
        InitializeComponent();
        ShopPane.GamesLoaded += SetShopGames;
        InitializeBackNavigation();
        InitializeCommentControls();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        var area = Microsoft.UI.Windowing.DisplayArea
            .GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        double scale = WindowManagerHelper.GetScaleFactor(this);
        int width = (int)Math.Min(1440, area.Width / scale - 24), height = (int)Math.Min(860, area.Height / scale - 24);
        WindowManagerHelper.ResizeWithDpi(AppWindow, this, width, height);
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, width, height);
        MinWidth = 1050;
        MinHeight = 620;
        RootGrid.RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
        ApplyCommunityTheme();
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            ApplyCommunityTheme();
            RenderArticle();
        };
        PostsList.ItemsSource = _posts;
        Closed += (_, _) =>
        {
            _closed = true;
            ShopPane.Stop();
            if (_instance == this) _instance = null;
            _lifetime.Cancel();
            _session.Cancel();
            _feedCancellation.Cancel();
            _readerCancellation.Cancel();
            _replyCancellation.Cancel();
            ArticleView.Close();
            _client.Dispose();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_initializing) return;
        RootGrid.Loaded -= OnLoaded;
        try
        {
            try
            {
                await _library.LoadAsync(_lifetime.Token);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                ShowStatus("Miyoushe_LibraryError".GetLocalized(), InfoBarSeverity.Warning);
            }

            if (_closed) return;
            var manager = App.GetService<AccountManager>();
            var accounts = manager.GetAllAccounts().Where(a => a.ServerType == "cn").ToArray();
            var choices = new List<Choice> { new("", "Miyoushe_Guest".GetLocalized()) };
            choices.AddRange(accounts.Select(a =>
                new Choice(a.Id, string.IsNullOrWhiteSpace(a.Nickname) ? a.Stuid : a.Nickname)));
            AccountSelector.ItemsSource = choices;
            AccountSelector.SelectedItem = choices.FirstOrDefault(a => a.Id == manager.ActiveAccountId) ?? choices[0];
            await SelectAccountAsync();
            UpdateShopAccount();
            var navigation = await _client.GetNavigationAsync(_session.Token);
            if (_closed) return;
            _forums = navigation.Forums;
            SetCommunityGames(navigation.Games, 2);
            SetForums();
            _initializing = false;
            await NavigateAsync(CommunityFeed.Forum);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _initializing = false;
            _changingSelectors = true;
            SetCommunityGames(new[]
            {
                new CommunityGame(2, "原神", "ys"), new CommunityGame(6, "崩坏：星穹铁道", "sr"),
                new CommunityGame(8, "绝区零", "zzz")
            }, 2);
            _changingSelectors = false;
            ReportError(ex, () => ReloadNavigationAsync());
            UpdateRefreshButtonState();
        }
    }

    private async Task<bool> ReloadNavigationAsync()
    {
        bool completed = false;
        await RunAsync(async ct =>
        {
            var navigation = await _client.GetNavigationAsync(ct);
            _changingSelectors = true;
            _forums = navigation.Forums;
            SetCommunityGames(navigation.Games, _feed.GameId);
            SetForums();
            _changingSelectors = false;
            completed = await NavigateAsync(CommunityFeed.Forum);
        }, _session.Token);
        return completed;
    }

    private async Task SelectAccountAsync()
    {
        _session.Cancel();
        _feedCancellation.Cancel();
        _readerCancellation.Cancel();
        _session.Dispose();
        _session = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _session.Token;
        string id = (AccountSelector.SelectedItem as Choice)?.Id ?? "";
        _client.Dispose();
        _client = new();
        _retry = null;
        _lastApiError = null;
        StatusBar.IsOpen = false;
        _pageHistory.Clear();
        _feedInitialized = false;
        _postInteractionUpdates.Clear();
        SetPageMode(false);
        _post = null;
        ReaderGrid.Visibility = Visibility.Collapsed;
        ReaderPlaceholder.Visibility = Visibility.Visible;
        if (ArticleView.CoreWebView2 != null) SetArticleDocument("<html></html>");
        if (id.Length == 0) return;
        try
        {
            var context = await App.GetService<AccountIdentityService>().BuildAsync(id);
            ct.ThrowIfCancellationRequested();
            if (context.ServerType != Models.MiHoYo.Identity.ServerType.Cn) return;
            _client.Dispose();
            _client = new(context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ct.ThrowIfCancellationRequested();
            _changingSelectors = true;
            AccountSelector.SelectedIndex = 0;
            _changingSelectors = false;
            ShowStatus("Miyoushe_IdentityError".GetLocalized(), InfoBarSeverity.Warning);
        }
    }

    private async void OnAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _changingSelectors || _closed) return;
        UpdateShopAccount();
        try
        {
            await SelectAccountAsync();
            UpdateShopAccount();
            await NavigateAsync(_feed.Kind is CommunityFeed.Following or CommunityFeed.Favorites
                ? _feed.Kind
                : CommunityFeed.Forum);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private int GameId => _communityGame?.Id ?? 2;

    private void SetForums()
    {
        var forums = _forums.Where(f => f.GameId == GameId).ToArray();
        _selectedForumId = (forums.FirstOrDefault(f => f.Id == _selectedForumId) ?? forums.FirstOrDefault())?.Id ?? 26;
        SetTabs(ForumTabs, forums.Select(f => (f.Id, f.Name)), _selectedForumId, "ForumTabs", OnForumChanged);
    }

    private async void OnGameChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelectors) return;
        if (_shopOpen)
        {
            if (GameSelector.SelectedItem is MiyousheShopGame game)
            {
                _shopGameKey = game.Key;
                await ShopPane.SetGameAsync(game.Key);
            }
            return;
        }
        if (_initializing || GameSelector.SelectedItem is not CommunityGame selected) return;
        _communityGame = selected;
        _changingSelectors = true;
        SetForums();
        _changingSelectors = false;
        await NavigateAsync(_feed.Kind == CommunityFeed.News ? CommunityFeed.News : CommunityFeed.Forum);
    }

    private async void OnForumChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _changingSelectors || _feed.Kind != CommunityFeed.Forum) return;
        _selectedForumId = (int)((RadioButton)sender).Tag;
        _feed = _feed with { ForumId = _selectedForumId };
        await LoadFeedAsync(true);
    }

    private async void OnSortChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _changingSelectors) return;
        _feed = _feed with { Sort = (int)((RadioButton)sender).Tag };
        await LoadFeedAsync(true);
    }

    private async void OnNavigate(object sender, RoutedEventArgs args)
    {
        CloseShop();
        await NavigateAsync(Enum.Parse<CommunityFeed>((string)((RadioButton)sender).Tag));
    }

    private async Task<bool> NavigateAsync(CommunityFeed kind, string target = "", string? title = null,
        int? gameId = null,
        int? sort = null, int? forum = null, bool remember = true, bool load = true)
    {
        var previous = CapturePage();
        _changingSelectors = true;
        if (gameId is { } relatedGame)
        {
            var game = _communityGames.FirstOrDefault(g => g.Id == relatedGame);
            if (game != null)
            {
                _communityGame = game;
                if (!_shopOpen) GameSelector.SelectedItem = game;
                SetForums();
            }
        }

        var sorts = kind switch
        {
            CommunityFeed.News => new[]
            {
                new SortChoice(3, "Miyoushe_News".GetLocalized()), new SortChoice(1, "Miyoushe_Notices".GetLocalized()),
                new SortChoice(2, "Miyoushe_Events".GetLocalized())
            },
            CommunityFeed.Search => new[]
            {
                new SortChoice(1, "Miyoushe_Hot".GetLocalized()), new SortChoice(2, "Miyoushe_Latest".GetLocalized())
            },
            CommunityFeed.Topic => new[]
            {
                new SortChoice(0, "Miyoushe_Latest".GetLocalized()), new SortChoice(2, "Miyoushe_Hot".GetLocalized()),
                new SortChoice(1, "Miyoushe_Featured".GetLocalized())
            },
            CommunityFeed.Forum => new[]
            {
                new SortChoice(3, "Miyoushe_Hot".GetLocalized()), new SortChoice(2, "Miyoushe_Latest".GetLocalized()),
                new SortChoice(1, "Miyoushe_LatestReplies".GetLocalized())
            },
            _ => Array.Empty<SortChoice>()
        };
        int order = sorts.FirstOrDefault(s => s.Value == sort)?.Value ?? sorts.FirstOrDefault()?.Value ?? 3;
        if (forum.HasValue)
        {
            _selectedForumId = forum.Value;
            SetForums();
        }

        var nextFeed = new FeedRequest(kind, GameId, target, order, _selectedForumId);
        ClearNavigationError();
        if (remember && _feedInitialized && (_isPostOpen || nextFeed != _feed)) _pageHistory.Push(previous);
        SetTabs(SortTabs, sorts.Select(s => (s.Value, s.Label)), order, "SortTabs", OnSortChanged);
        _feed = nextFeed;
        _feedInitialized = true;
        _readerCancellation.Cancel();
        _replyCancellation.Cancel();
        _post = null;
        SetPageMode(false);
        UpdateNavigationSelection();
        FeedTitle.Text = title ?? ("Miyoushe_" + kind).GetLocalized();
        FeedDescription.Text = "";
        FeedDescription.Visibility = Visibility.Collapsed;
        _changingSelectors = false;
        if (load) return await LoadFeedAsync(true);
        else
        {
            _feedCancellation.Cancel();
            _feedCancellation.Dispose();
            _feedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
            _feedLoading = false;
            FeedProgress.Visibility = Visibility.Collapsed;
            UpdateRefreshButtonState();
        }

        return true;
    }

    private async Task<bool> LoadFeedAsync(bool reset)
    {
        if (_closed || (!reset && _feedLoading)) return false;
        if (reset)
        {
            _feedCancellation.Cancel();
            _feedCancellation.Dispose();
            _feedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
            _feedCursor = "";
            _posts.Clear();
            FindFeedScroll()?.ChangeView(null, 0, null, true);
        }

        var source = _feedCancellation;
        var feed = _feed;
        var client = _client;
        string cursor = _feedCursor;
        _feedLoading = true;
        FeedProgress.Visibility = Visibility.Visible;
        EmptyFeed.Visibility = Visibility.Collapsed;
        UpdateRefreshButtonState();
        MorePostsButton.Visibility = Visibility.Collapsed;
        bool completed = false;
        await RunAsync(async ct =>
        {
            if (feed.Kind is CommunityFeed.Bookmarks or CommunityFeed.History)
            {
                foreach (var item in feed.Kind == CommunityFeed.Bookmarks ? _library.Bookmarks : _library.History)
                    _posts.Add(ApplyPostInteractionState(item));
            }
            else
            {
                var page = await client.GetFeedAsync(feed, cursor, ct);
                ct.ThrowIfCancellationRequested();
                foreach (var post in page.Items)
                    if (!_posts.Any(p => p.Id == post.Id))
                        _posts.Add(MergePostFromServer(post));
                _feedCursor = page.Cursor;
                MorePostsButton.Visibility = page.IsLast ? Visibility.Collapsed : Visibility.Visible;
                if (reset && feed.Kind is CommunityFeed.User or CommunityFeed.Topic or CommunityFeed.Collection)
                {
                    var description = await client.GetDescriptionAsync(feed, ct);
                    ct.ThrowIfCancellationRequested();
                    FeedDescription.Text = description;
                    FeedDescription.Visibility = description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                }
            }

            EmptyFeed.Visibility = _posts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            completed = true;
        }, source.Token, () => LoadFeedAsync(reset));
        if (source == _feedCancellation && !_closed)
        {
            _feedLoading = false;
            FeedProgress.Visibility = Visibility.Collapsed;
            UpdateRefreshButtonState();
        }

        return completed && source == _feedCancellation && !source.IsCancellationRequested && !_closed;
    }

    private async void OnRefresh(object sender, RoutedEventArgs args) => await RefreshCurrentPageAsync();
    private async void OnMorePosts(object sender, RoutedEventArgs args) => await LoadFeedAsync(false);

    private async void OnPostClicked(object sender, ItemClickEventArgs e) =>
        await OpenPostAsync(((CommunityPost)e.ClickedItem).Id, ((CommunityPost)e.ClickedItem).GameId);

    private void OnCoverFailed(object sender, ExceptionRoutedEventArgs args) =>
        ((Image)sender).Visibility = Visibility.Collapsed;

    private async void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.QueryText.Trim();
        if (query.Length == 0) return;
        if (query.Length > 200)
        {
            ShowStatus("Miyoushe_SearchTooLong".GetLocalized(), InfoBarSeverity.Warning);
            return;
        }

        if (query.All(char.IsAsciiDigit))
        {
            await OpenPostAsync(query, GameId);
            return;
        }

        if (CommunityContent.TryWebUri(query, out var uri) &&
            CommunityContent.TryInternalLink(uri, out var kind, out var id))
        {
            await OpenInternalAsync(kind, id);
            return;
        }

        await NavigateAsync(CommunityFeed.Search, query, "Miyoushe_Search".GetLocalized() + " · " + query);
    }

    private async Task<bool> OpenPostAsync(string id, int game, bool remember = true)
    {
        ClearNavigationError();
        if (remember && _feedInitialized && (!_isPostOpen || _post is { } current && current.Id != id))
            _pageHistory.Push(CapturePage());
        _feedCancellation.Cancel();
        _feedLoading = false;
        FeedProgress.Visibility = Visibility.Collapsed;
        _readerCancellation.Cancel();
        _readerCancellation.Dispose();
        _readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        var source = _readerCancellation;
        var client = _client;
        _readerTarget = (id, game);
        _readerLoading = true;
        _post = null;
        _commentsLoaded = false;
        _replyLoading = false;
        RepliesPanel.Children.Clear();
        MoreRepliesButton.Visibility = Visibility.Collapsed;
        _replies.Clear();
        _hasMoreReplies = false;
        _commentActionRoot = "https://miyoushe-native.invalid/" + Guid.NewGuid().ToString("N") + "/";
        ReaderGrid.Visibility = Visibility.Collapsed;
        ReaderPlaceholder.Visibility = Visibility.Visible;
        SetPageMode(true);
        ApplyCommentLayout();
        ReaderProgress.Visibility = Visibility.Visible;
        bool completed = false;
        await RunAsync(async ct =>
        {
            var post = await client.GetPostAsync(id, game, ct);
            ct.ThrowIfCancellationRequested();
            post = MergePostFromServer(post);
            _post = post;
            ReaderGrid.Visibility = Visibility.Visible;
            ReaderPlaceholder.Visibility = Visibility.Collapsed;
            PostTitle.Text = post.Title;
            AuthorButton.Content = post.Author;
            AuthorButton.IsEnabled = post.CanOpenAuthor;
            AuthorAvatar.Avatar = post.Avatar;
            AuthorAvatar.Tag = post;
            AuthorAvatar.IsEnabled = post.CanOpenAuthor;
            AuthorAvatar.DisplayName = post.Author;
            PostMeta.Text = post.Published;
            MiyoushePostCounters.SetCounters(PostStats, post.Counters);
            _replyLikeOverrides.Clear();
            UpdatePostInteractionState();
            RepliesTitle.Text = "Miyoushe_Comments".GetLocalized();
            BookmarkButton.IsChecked = _library.Contains(post.Id);
            TopicLinks.Children.Clear();
            foreach (var topic in post.Topics.Take(3))
            {
                var button = new HyperlinkButton
                    { Content = "#" + topic.Name, Padding = new Thickness(0), FontSize = 12 };
                button.Click += async (_, _) =>
                    await NavigateAsync(CommunityFeed.Topic, topic.Id, "#" + topic.Name, post.GameId);
                TopicLinks.Children.Add(button);
            }

            if (post.Collection != null)
            {
                var collection = post.Collection;
                var button = new HyperlinkButton
                    { Content = "Miyoushe_Collection".GetLocalized(), Padding = new Thickness(0), FontSize = 12 };
                button.Click += async (_, _) => await NavigateAsync(CommunityFeed.Collection, collection.Id,
                    collection.Name, post.GameId);
                TopicLinks.Children.Add(button);
            }

            TopicLinks.Visibility = TopicLinks.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            _webInitialization ??= InitializeArticleViewAsync();
            await _webInitialization;
            ct.ThrowIfCancellationRequested();
            RenderArticle();
            try
            {
                await _library.VisitAsync(post, ct);
            }
            catch (IOException)
            {
                ShowStatus("Miyoushe_LibraryError".GetLocalized(), InfoBarSeverity.Warning);
            }

            await LoadRepliesAsync(true);
            completed = true;
        }, source.Token, () => OpenPostAsync(id, game, remember: false));
        if (source == _readerCancellation && !_closed)
        {
            _readerLoading = false;
            ReaderProgress.Visibility = Visibility.Collapsed;
            UpdateRefreshButtonState();
        }

        return completed && source == _readerCancellation && !source.IsCancellationRequested && !_closed;
    }

    private async Task InitializeArticleViewAsync()
    {
        try
        {
            await ArticleView.EnsureCoreWebView2Async();
            if (_closed) return;
            var core = ArticleView.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += async (_, e) =>
            {
                if (e.Uri == _articleDocumentUri || (!e.IsUserInitiated && e.Uri == "about:blank")) return;
                e.Cancel = true;
                if (CommunityContent.TryWebUri(e.Uri, out var uri)) await OpenLinkAsync(uri);
            };
            core.NewWindowRequested += async (_, e) =>
            {
                e.Handled = true;
                if (CommunityContent.TryWebUri(e.Uri, out var uri)) await OpenLinkAsync(uri);
            };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NavigationCompleted += async (_, e) =>
            {
                if (e.IsSuccess) await UpdateInlineCommentsAsync();
            };
        }
        catch
        {
            _webInitialization = null;
            throw;
        }
    }

    private void RenderArticle()
    {
        if (_post == null || ArticleView.CoreWebView2 == null || _closed) return;
        SetArticleDocument(CommunityContent.Render(_post, RootGrid.ActualTheme == ElementTheme.Dark));
    }

    private void ApplyCommunityTheme()
    {
        var palette =
            (ResourceDictionary)RootGrid.Resources.ThemeDictionaries[
                RootGrid.ActualTheme == ElementTheme.Light ? "Light" : "Default"];
        RootGrid.Background = (Brush)palette["CommunityCanvasBrush"];
    }

    private void SetArticleDocument(string html)
    {
        _articleDocumentUri = CommunityContent.ToDocumentUri(html);
        ArticleView.CoreWebView2.Navigate(_articleDocumentUri);
    }

    private async Task OpenInternalAsync(CommunityFeed kind, string id)
    {
        if (kind == CommunityFeed.Forum) await OpenPostAsync(id, GameId);
        else if (kind == CommunityFeed.User)
        {
            var author = _replies.SelectMany(r => new[] { r }.Concat(r.Children)).FirstOrDefault(r => r.AuthorId == id);
            await OpenAuthorProfileAsync(id, author?.Author ?? (_post?.AuthorId == id ? _post.Author : ""),
                _post?.GameId ?? GameId);
        }
        else await NavigateAsync(kind, id);
    }

    private async Task OpenLinkAsync(Uri uri)
    {
        if (await HandleCommentActionAsync(uri)) return;
        if (CommunityContent.TryInternalLink(uri, out var kind, out var id)) await OpenInternalAsync(kind, id);
        else if (_post?.Raw.Get("image_list").Items().Any(i => i.Text("url") == uri.AbsoluteUri) == true ||
                 System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"\.(png|jpe?g|webp|gif)$",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            await ShowImageAsync(uri);
        else await Launcher.LaunchUriAsync(uri);
    }

    private async void OnAuthor(object sender, RoutedEventArgs args)
    {
        if (_post != null) await OpenAuthorProfileAsync(_post.AuthorId, _post.Author, _post.GameId);
    }

    private async void OnMyProfile(object sender, RoutedEventArgs args)
    {
        CloseShop();
        if (_client.AccountUid.Length == 0)
        {
            UpdateNavigationSelection();
            ShowStatus("Miyoushe_LoginHint".GetLocalized(), InfoBarSeverity.Warning);
            return;
        }

        await NavigateAsync(CommunityFeed.User, _client.AccountUid, "Miyoushe_MyProfile".GetLocalized());
    }

    private async void OnBookmark(object sender, RoutedEventArgs args)
    {
        if (_post == null) return;
        await RunAsync(async ct =>
        {
            await _library.ToggleAsync(_post, ct);
            BookmarkButton.IsChecked = _library.Contains(_post.Id);
            if (_feed.Kind == CommunityFeed.Bookmarks) await LoadFeedAsync(true);
        }, _readerCancellation.Token);
    }

    private Uri OriginalUri => new(ApiEndpoints.MiyousheWebBase + "/" +
                                   (_communityGames.FirstOrDefault(g => g.Id == _post?.GameId)?.Slug ?? "ys") + "/article/" +
                                   _post!.Id);

    private void OnCopyLink(object sender, RoutedEventArgs args)
    {
        if (_post == null) return;
        var data = new DataPackage();
        data.SetText(OriginalUri.AbsoluteUri);
        Clipboard.SetContent(data);
        ShowStatus("Miyoushe_LinkCopied".GetLocalized(), InfoBarSeverity.Success);
    }

    private async void OnOriginal(object sender, RoutedEventArgs args)
    {
        if (_post != null) await Launcher.LaunchUriAsync(OriginalUri);
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken ct, Func<Task>? retry = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && !_closed) ReportError(ex, retry ?? (() => RunAsync(operation, ct)));
        }
    }

    private void ReportError(Exception error, Func<Task>? retry)
    {
        _retry = retry;
        _lastApiError = error as CommunityApiException;
        string message = error is OperationCanceledException ? "Miyoushe_Timeout".GetLocalized() : error.Message;
        if (_lastApiError?.NeedsVerification == true) message = "Miyoushe_RiskHint".GetLocalized();
        if (_lastApiError?.LoginExpired == true) message = "Miyoushe_LoginHint".GetLocalized();
        ShowStatus(message.Length > 400 ? message[..400] : message, InfoBarSeverity.Warning);
        StatusAction.Content = (_client.NeedsVerification ? "Miyoushe_Verify" : "Miyoushe_Retry").GetLocalized();
        StatusAction.Visibility = retry == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearNavigationError()
    {
        _retry = null;
        _lastApiError = null;
        StatusAction.Visibility = Visibility.Collapsed;
        if (!_client.NeedsVerification) StatusBar.IsOpen = false;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        if (_closed) return;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
        StatusAction.Visibility = Visibility.Collapsed;
    }

    private void OnStatusClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
    }

    private async void OnStatusAction(object sender, RoutedEventArgs args)
    {
        if (_verifying || _retry == null) return;
        var retry = _retry;
        var client = _client;
        var ct = _session.Token;
        if (client.NeedsVerification)
        {
            _verifying = true;
            StatusAction.IsEnabled = false;
            try
            {
                await client.VerifyAsync(MiyousheVerificationWindow.ShowAsync, ct);
                ct.ThrowIfCancellationRequested();
                StatusBar.IsOpen = false;
                await retry();
            }
            catch (OperationCanceledException)
            {
                if (!ct.IsCancellationRequested)
                {
                    ShowStatus("Miyoushe_VerifyCancelled".GetLocalized(), InfoBarSeverity.Informational);
                    StatusAction.Content = "Miyoushe_Verify".GetLocalized();
                    StatusAction.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) ReportError(ex, retry);
            }
            finally
            {
                _verifying = false;
                if (!_closed) StatusAction.IsEnabled = true;
            }
        }
        else
        {
            StatusBar.IsOpen = false;
            await retry();
        }
    }
}
