using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.Miyoushe;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;
using Windows.System;

namespace FufuLauncher.Views;

public sealed partial class MiyousheWindow
{
    private int _replyOrder;
    private readonly List<CommunityReply> _replies = [];
    private bool _commentsRight, _changingReplyOptions, _hasMoreReplies;
    private string _commentActionRoot = "";

    private void InitializeCommentControls()
    {
        ReplySortSelector.ItemsSource = new[]
        {
            new SortChoice(0, "Miyoushe_HotReplies".GetLocalized()),
            new SortChoice(2, "Miyoushe_LatestReplies".GetLocalized()),
            new SortChoice(1, "Miyoushe_OldestReplies".GetLocalized())
        };
        ReplySortSelector.SelectedIndex = 0;
        ReaderBodyGrid.SizeChanged += (_, _) => ApplyCommentLayout();
        ApplyCommentLayout();
    }

    private void ApplyCommentLayout()
    {
        CommentsColumn.Width =
            new GridLength(_commentsRight ? Math.Clamp(ReaderBodyGrid.ActualWidth * .3, 320, 420) : 0);
        CommentsView.Visibility = _commentsRight ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnCommentsLayoutChanged(object sender, RoutedEventArgs args)
    {
        _commentsRight = DockCommentsButton.IsChecked == true;
        ApplyCommentLayout();
        await UpdateInlineCommentsAsync();
        if (_post != null && !_commentsLoaded && !_replyLoading) await LoadRepliesAsync(true);
    }

    private async void OnReplySortChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_initializing || _changingReplyOptions || ReplySortSelector.SelectedItem is not SortChoice sort) return;
        _replyOrder = sort.Value;
        if (_post != null && _isPostOpen) await LoadRepliesAsync(true);
    }

    private async void OnReplyFilterChanged(object sender, RoutedEventArgs args)
    {
        if (!_initializing && !_changingReplyOptions && _post != null && _isPostOpen) await LoadRepliesAsync(true);
    }

    private async void OnMoreReplies(object sender, RoutedEventArgs args) => await LoadRepliesAsync(false);

    private async Task LoadRepliesAsync(bool reset)
    {
        if (_post == null || (!reset && _replyLoading)) return;
        if (reset)
        {
            _replyCancellation.Cancel();
            _replyCancellation.Dispose();
            _replyCancellation = CancellationTokenSource.CreateLinkedTokenSource(_readerCancellation.Token);
        }

        var post = _post;
        var client = _client;
        var ct = _replyCancellation.Token;
        var source = _replyCancellation;
        int order = _replyOrder;
        bool onlyAuthor = OnlyAuthor.IsChecked == true;
        if (reset)
        {
            _replyCursor = "";
            _replies.Clear();
            RepliesPanel.Children.Clear();
            _commentsLoaded = false;
            _hasMoreReplies = false;
            RepliesScroll.ChangeView(null, 0, null);
        }

        _replyLoading = true;
        RepliesProgress.Visibility = Visibility.Visible;
        MoreRepliesButton.Visibility = Visibility.Collapsed;
        await UpdateInlineCommentsAsync();
        string cursor = _replyCursor;
        await RunAsync(async token =>
        {
            var page = await client.GetRepliesAsync(post, cursor, order, onlyAuthor, token);
            token.ThrowIfCancellationRequested();
            foreach (var reply in page.Items)
            {
                if (_replies.Any(r => r.Id == reply.Id)) continue;
                _replies.Add(reply);
                RepliesPanel.Children.Add(BuildReply(reply, post, false));
            }

            _replyCursor = page.Cursor;
            _commentsLoaded = true;
            _hasMoreReplies = !page.IsLast;
            MoreRepliesButton.Visibility = page.IsLast ? Visibility.Collapsed : Visibility.Visible;
            if (RepliesPanel.Children.Count == 0)
                RepliesPanel.Children.Add(new TextBlock { Text = "Miyoushe_NoComments".GetLocalized() });
        }, ct, () => LoadRepliesAsync(reset));
        if (source == _replyCancellation && !ct.IsCancellationRequested && !_closed)
        {
            _replyLoading = false;
            RepliesProgress.Visibility = Visibility.Collapsed;
            await UpdateInlineCommentsAsync();
        }
    }

    private async Task UpdateInlineCommentsAsync()
    {
        if (_closed || _post == null || ArticleView.CoreWebView2 == null || !_isPostOpen) return;
        string document = _articleDocumentUri;
        string html = _commentsRight
            ? ""
            : CommunityContent.RenderComments(_replies.Select(ApplyReplyLikeState), new CommunityCommentOptions
            {
                ActionRoot = _commentActionRoot, Title = RepliesTitle.Text,
                Hot = "Miyoushe_HotReplies".GetLocalized(), Latest = "Miyoushe_LatestReplies".GetLocalized(),
                Oldest = "Miyoushe_OldestReplies".GetLocalized(),
                OnlyAuthor = "Miyoushe_OnlyAuthor".GetLocalized(),
                Empty = _commentsLoaded
                    ? "Miyoushe_NoComments".GetLocalized()
                    : "Miyoushe_CommentsUnavailable".GetLocalized(),
                More = "Miyoushe_More".GetLocalized(), SubReplies = "Miyoushe_SubReplies".GetLocalized(),
                Loading = "Miyoushe_LoadingComments".GetLocalized(),
                Like = "Miyoushe_Like".GetLocalized(), Liked = "Miyoushe_Liked".GetLocalized(),
                Reply = "Miyoushe_Reply".GetLocalized(),
                Order = _replyOrder, OnlyAuthorEnabled = OnlyAuthor.IsChecked == true, IsLoading = _replyLoading,
                HasMore = _hasMoreReplies
            });
        try
        {
            await ArticleView.CoreWebView2.ExecuteScriptAsync("(() => { if (location.href !== " +
                                                              JsonSerializer.Serialize(document) +
                                                              ") return; const section = document.getElementById('community-comments'); if (!section) return; section.innerHTML = " +
                                                              JsonSerializer.Serialize(html) +
                                                              "; section.querySelectorAll('.comment-avatar img').forEach(img => { const fallback = () => img.remove(); img.addEventListener('error', fallback, { once: true }); if (img.complete && !img.naturalWidth) fallback(); }); })();");
        }
        catch (Exception ex)
        {
            if (!_closed && document == _articleDocumentUri) ReportError(ex, UpdateInlineCommentsAsync);
        }
    }

    private async Task<bool> HandleCommentActionAsync(Uri uri)
    {
        if (uri.Host != ApiEndpoints.MiyousheNativeVirtualHost) return false;
        if (!_isPostOpen || _post == null || _commentActionRoot.Length == 0 ||
            !uri.AbsoluteUri.StartsWith(_commentActionRoot, StringComparison.Ordinal)) return true;
        string action = uri.AbsoluteUri[_commentActionRoot.Length..];
        if (action.StartsWith("sort/", StringComparison.Ordinal) && int.TryParse(action[5..], out int order) &&
            order is 0 or 1 or 2)
        {
            _replyOrder = order;
            _changingReplyOptions = true;
            ReplySortSelector.SelectedItem = ReplySortSelector.Items.OfType<SortChoice>().First(s => s.Value == order);
            _changingReplyOptions = false;
            await LoadRepliesAsync(true);
        }
        else if (action == "only-author")
        {
            OnlyAuthor.IsChecked = OnlyAuthor.IsChecked != true;
            await LoadRepliesAsync(true);
        }
        else if (action == "more" && _hasMoreReplies) await LoadRepliesAsync(false);
        else if (action.StartsWith("like/", StringComparison.Ordinal) && FindReply(action[5..]) is { } likedReply)
            await SetReplyLikeAsync(_post, likedReply);
        else if (action.StartsWith("reply/", StringComparison.Ordinal) && FindReply(action[6..]) is { } target)
            await ReplyToAsync(_post, target);
        else if (action.StartsWith("floor/", StringComparison.Ordinal))
        {
            string floor = Uri.UnescapeDataString(action[6..]);
            if (_replies.Any(r => r.FloorId == floor && r.ChildCount > 0)) await ShowSubRepliesAsync(_post, floor);
        }

        return true;
    }

    private StackPanel BuildReply(CommunityReply reply, CommunityPost post, bool child)
    {
        reply = ApplyReplyLikeState(reply);
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(child ? 14 : 0, 8, 0, 14) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var avatar = new MiyousheAvatar
        {
            Avatar = reply.Avatar, DisplayName = reply.Author, Width = child ? 22 : 28, Height = child ? 22 : 28,
            IsEnabled = CommunityUser.IsValidId(reply.AuthorId)
        };
        avatar.Click += async (_, _) => await OpenAuthorProfileAsync(reply.AuthorId, reply.Author, post.GameId);
        header.Children.Add(avatar);
        var name = new HyperlinkButton
        {
            Content = reply.Author, Padding = new Thickness(0), FontSize = 13,
            IsEnabled = CommunityUser.IsValidId(reply.AuthorId)
        };
        name.Click += async (_, _) => await OpenAuthorProfileAsync(reply.AuthorId, reply.Author, post.GameId);
        header.Children.Add(name);
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock { Text = reply.Meta, FontSize = 11, Opacity = .55 });
        panel.Children.Add(new TextBlock
        {
            Text = reply.Content, FontSize = 15, LineHeight = 26, TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });
        foreach (var url in reply.Images.Take(9))
        {
            if (!CommunityContent.TryWebUri(url, out var uri)) continue;
            var button = new Button
            {
                Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
                Content = new Image
                {
                    Source = new BitmapImage(uri), MaxWidth = 250, MaxHeight = 160,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                }
            };
            button.Click += async (_, _) => await ShowImageAsync(uri);
            panel.Children.Add(button);
        }

        if (reply.Id.Length > 0 && reply.Id.All(char.IsAsciiDigit))
        {
            var actions = new Grid { ColumnSpacing = 8 };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var like = new ToggleButton
            {
                CornerRadius = new CornerRadius(18), Padding = new Thickness(12, 5, 12, 5), FontSize = 12
            };
            UpdateReplyLikeButton(like, reply.IsLiked, reply.LikeCount);
            Grid.SetColumn(like, 1);
            like.Click += async (_, _) => await SetReplyLikeAsync(post, reply, like);
            var respond = new Button
            {
                Content = "Miyoushe_Reply".GetLocalized(), CornerRadius = new CornerRadius(18),
                Padding = new Thickness(12, 5, 12, 5), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left
            };
            respond.Click += async (_, _) => await ReplyToAsync(post, reply);
            actions.Children.Add(respond);
            actions.Children.Add(like);
            panel.Children.Add(actions);
        }

        if (!child)
        {
            foreach (var sub in reply.Children.Take(2)) panel.Children.Add(BuildReply(sub, post, true));
            if (reply.ChildCount > 0)
            {
                var more = new HyperlinkButton
                {
                    Content = string.Format("Miyoushe_SubReplies".GetLocalized(), reply.ChildCount),
                    Padding = new Thickness(0)
                };
                more.Click += async (_, _) => await ShowSubRepliesAsync(post, reply.FloorId);
                panel.Children.Add(more);
            }
        }

        return panel;
    }

    private async Task ShowSubRepliesAsync(CommunityPost post, string floor)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_readerCancellation.Token);
            var panel = new StackPanel { Spacing = 12, Width = 450 };
            var more = new Button
                { Content = "Miyoushe_More".GetLocalized(), HorizontalAlignment = HorizontalAlignment.Stretch };
            var list = new StackPanel { Spacing = 8 };
            panel.Children.Add(list);
            panel.Children.Add(more);
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot, Title = "Miyoushe_Comments".GetLocalized(),
                CloseButtonText = "Miyoushe_Close".GetLocalized(),
                Content = new ScrollViewer { Content = panel, MaxHeight = 520 }
            };
            string cursor = "";
            var client = _client;

            async Task Load()
            {
                more.IsEnabled = false;
                try
                {
                    var page = await client.GetRepliesAsync(post, cursor, 0, false, cancel.Token, floor);
                    cancel.Token.ThrowIfCancellationRequested();
                    foreach (var item in page.Items) list.Children.Add(BuildReply(item, post, true));
                    cursor = page.Cursor;
                    more.Visibility = page.IsLast ? Visibility.Collapsed : Visibility.Visible;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (!cancel.IsCancellationRequested) ReportError(ex, () => ShowSubRepliesAsync(post, floor));
                }

                if (!cancel.IsCancellationRequested) more.IsEnabled = true;
            }

            more.Click += async (_, _) => await Load();
            await Load();
            if (!cancel.IsCancellationRequested && list.Children.Count > 0)
            {
                _replyDialog = dialog;
                await dialog.ShowAsync();
            }

            cancel.Cancel();
        }
        catch (Exception ex)
        {
            if (!_closed) ReportError(ex, null);
        }
        finally
        {
            _dialogOpen = false;
            _replyDialog = null;
        }

        var profile = _queuedProfile;
        _queuedProfile = null;
        if (profile != null && !_closed && _post?.Id == post.Id)
        {
            await OpenAuthorProfileAsync(profile.Id, profile.Name, profile.Game);
            return;
        }

        var target = _queuedReply;
        _queuedReply = null;
        if (target != null && !_closed && _post?.Id == post.Id) await ShowCommentComposerAsync(target);
    }

    private async Task ShowImageAsync(Uri uri)
    {
        if (_dialogOpen || _closed) return;
        _dialogOpen = true;
        try
        {
            var image = new Image
                { Source = new BitmapImage(uri), Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, MaxWidth = 850 };
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot, Title = "Miyoushe_Image".GetLocalized(),
                CloseButtonText = "Miyoushe_Close".GetLocalized(),
                PrimaryButtonText = "Miyoushe_OpenImage".GetLocalized(),
                Content = new ScrollViewer
                {
                    Content = image, MaxHeight = 600, HorizontalScrollMode = ScrollMode.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            if (!_closed) ReportError(ex, null);
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}