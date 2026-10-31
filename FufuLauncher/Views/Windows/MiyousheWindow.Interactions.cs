using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.Miyoushe;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Views;

public sealed partial class MiyousheWindow
{
    private bool _interactionBusy;

    private sealed record CommentDraft(string Text, bool Uncertain = false);

    private readonly Dictionary<string, CommentDraft> _commentDrafts = [];
    private readonly Dictionary<string, (bool Liked, long Count)> _replyLikeOverrides = [];
    private ContentDialog? _replyDialog;
    private CommunityReply? _queuedReply;

    private static string LikeLabel(bool liked, long count) =>
        (liked ? "Miyoushe_Liked" : "Miyoushe_Like").GetLocalized() + " " + count;

    private static void UpdateReplyLikeButton(ToggleButton button, bool liked, long count)
    {
        button.IsChecked = liked;
        string label = LikeLabel(liked, count);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var icon = new MiyousheIcon { Kind = "Like" };
        icon.SetBinding(Control.ForegroundProperty,
            new Binding { Source = button, Path = new PropertyPath(nameof(Control.Foreground)) });
        var number = new TextBlock
            { Text = count.ToString(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        number.SetBinding(TextBlock.ForegroundProperty,
            new Binding { Source = button, Path = new PropertyPath(nameof(Control.Foreground)) });
        content.Children.Add(icon);
        content.Children.Add(number);
        button.Content = content;
    }

    private void UpdatePostInteractionState()
    {
        UpdateRefreshButtonState();
        foreach (var counters in FeedCounters(PostsList)) counters.IsInteractionEnabled = !_interactionBusy;
        if (_post == null || _closed) return;
        PostLikeButton.IsChecked = _post.IsLiked;
        PostLikeLabel.Text = LikeLabel(_post.IsLiked, _post.LikeCount ?? 0);
        AutomationProperties.SetName(PostLikeButton, PostLikeLabel.Text);
        PostLikeButton.IsEnabled = !_interactionBusy;
        MiyoushePostCounters.SetCounters(PostStats, _post.Counters);
    }

    private bool IsCurrentInteraction(MiyousheClient client, string postId, CancellationToken ct) =>
        !_closed && !ct.IsCancellationRequested && client == _client && _isPostOpen && _post?.Id == postId;

    private async void OnPostLike(object sender, RoutedEventArgs args)
    {
        if (_post == null) return;
        bool liked = !_post.IsLiked;
        UpdatePostInteractionState();
        await SetPostLikeAsync(_client, _post.Id, liked, _readerCancellation.Token);
    }

    private async Task SetPostLikeAsync(MiyousheClient client, string postId, bool liked, CancellationToken ct)
    {
        if (_interactionBusy || !IsCurrentInteraction(client, postId, ct)) return;
        _interactionBusy = true;
        UpdatePostInteractionState();
        try
        {
            await client.SetPostLikeAsync(postId, liked, ct);
            if (!IsCurrentInteraction(client, postId, ct)) return;
            var post = _post!;
            _post = post with
            {
                IsLiked = liked, HasLikeState = true,
                LikeCount = Math.Max(0, (post.LikeCount ?? 0) + (liked == post.IsLiked ? 0 : liked ? 1 : -1))
            };
            RememberPostInteraction(_post);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentInteraction(client, postId, ct))
                ReportError(ex, () => SetPostLikeAsync(client, postId, liked, ct));
        }
        finally
        {
            _interactionBusy = false;
            UpdatePostInteractionState();
        }
    }

    private CommunityReply ApplyReplyLikeState(CommunityReply reply) => reply with
    {
        IsLiked = _replyLikeOverrides.TryGetValue(reply.Id, out var state) ? state.Liked : reply.IsLiked,
        LikeCount = _replyLikeOverrides.TryGetValue(reply.Id, out state) ? state.Count : reply.LikeCount,
        Children = reply.Children.Select(ApplyReplyLikeState).ToArray()
    };

    private CommunityReply? FindReply(string id)
    {
        foreach (var reply in _replies)
        {
            if (reply.Id == id) return ApplyReplyLikeState(reply);
            if (reply.Children.FirstOrDefault(r => r.Id == id) is { } child) return ApplyReplyLikeState(child);
        }

        return null;
    }

    private async Task SetReplyLikeAsync(CommunityPost post, CommunityReply reply, ToggleButton? button = null,
        MiyousheClient? expectedClient = null, CancellationToken? token = null, bool? desired = null)
    {
        var client = expectedClient ?? _client;
        var ct = token ?? _readerCancellation.Token;
        reply = ApplyReplyLikeState(reply);
        bool liked = desired ?? !reply.IsLiked;
        if (button != null) button.IsChecked = reply.IsLiked;
        if (_interactionBusy || !IsCurrentInteraction(client, post.Id, ct)) return;
        _interactionBusy = true;
        UpdatePostInteractionState();
        if (button != null) button.IsEnabled = false;
        try
        {
            await client.SetReplyLikeAsync(post.Id, reply.Id, liked, ct);
            if (!IsCurrentInteraction(client, post.Id, ct)) return;
            long count = Math.Max(0, reply.LikeCount + (liked == reply.IsLiked ? 0 : liked ? 1 : -1));
            _replyLikeOverrides[reply.Id] = (liked, count);
            if (button != null) UpdateReplyLikeButton(button, liked, count);
            RebuildReplies();
            await UpdateInlineCommentsAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentInteraction(client, post.Id, ct))
                ReportError(ex, () => SetReplyLikeAsync(post, reply, button, client, ct, liked));
        }
        finally
        {
            _interactionBusy = false;
            UpdatePostInteractionState();
            if (!_closed && button != null) button.IsEnabled = true;
        }
    }

    private void RebuildReplies()
    {
        if (_post == null) return;
        RepliesPanel.Children.Clear();
        foreach (var reply in _replies) RepliesPanel.Children.Add(BuildReply(ApplyReplyLikeState(reply), _post, false));
    }

    private async void OnPublishComment(object sender, RoutedEventArgs args) => await ShowCommentComposerAsync();

    private async Task ReplyToAsync(CommunityPost post, CommunityReply reply)
    {
        if (!_isPostOpen || _post?.Id != post.Id) return;
        if (_replyDialog != null)
        {
            _queuedReply = reply;
            _replyDialog.Hide();
        }
        else await ShowCommentComposerAsync(reply);
    }

    private async Task ShowCommentComposerAsync(CommunityReply? reply = null)
    {
        if (_dialogOpen || _interactionBusy || _closed || _post == null) return;
        var post = _post;
        var client = _client;
        var ct = _readerCancellation.Token;
        try
        {
            client.RequireInteractionAccount();
        }
        catch (Exception ex)
        {
            ReportError(ex, null);
            return;
        }

        string key = ((AccountSelector.SelectedItem as Choice)?.Id ?? client.AccountUid) + "/" + post.Id + "/" +
                     reply?.Id;
        var draft = _commentDrafts.GetValueOrDefault(key) ?? new CommentDraft("");
        bool uncertain = draft.Uncertain, posted = false, sending = false;
        var editor = new TextBox
        {
            Text = draft.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MaxLength = 1000, MinHeight = 150, MaxHeight = 300, PlaceholderText = "Miyoushe_CommentHint".GetLocalized()
        };
        var count = new TextBlock
        {
            Text = editor.Text.Length + "/1000", FontSize = 12, Opacity = .6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var feedback = new InfoBar
        {
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppGlobalFont"],
            IsOpen = uncertain, IsClosable = false, Severity = InfoBarSeverity.Warning,
            Message = uncertain ? "Miyoushe_SubmissionUncertain".GetLocalized() : ""
        };
        var panel = new StackPanel { Spacing = 12, Width = 440 };
        panel.Children.Add(new TextBlock
            { Text = (AccountSelector.SelectedItem as Choice)?.Label ?? client.AccountUid, Opacity = .7 });
        if (reply != null)
            panel.Children.Add(new TextBlock
            {
                Text = string.Format("Miyoushe_ReplyTo".GetLocalized(), reply.Author), TextWrapping = TextWrapping.Wrap
            });
        panel.Children.Add(editor);
        panel.Children.Add(count);
        panel.Children.Add(feedback);
        var original = new HyperlinkButton { Content = "Miyoushe_Original".GetLocalized(), Padding = new Thickness(0) };
        var originalUri = OriginalUri;
        original.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(originalUri);
        panel.Children.Add(original);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = (reply == null ? "Miyoushe_PublishComment" : "Miyoushe_Reply").GetLocalized(),
            Content = panel,
            PrimaryButtonText = (uncertain ? "Miyoushe_Resubmit" : "Miyoushe_SendComment").GetLocalized(),
            CloseButtonText = "Miyoushe_Cancel".GetLocalized(), DefaultButton = ContentDialogButton.None
        };
        editor.TextChanged += (_, _) =>
        {
            count.Text = editor.Text.Length + "/1000";
            _commentDrafts[key] = new(editor.Text, uncertain);
            dialog.IsPrimaryButtonEnabled = !sending && !string.IsNullOrWhiteSpace(editor.Text);
        };
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(editor.Text);
        dialog.Closing += (_, args) =>
        {
            if (sending && !_closed) args.Cancel = true;
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (sending || !IsCurrentInteraction(client, post.Id, ct)) return;
            var deferral = args.GetDeferral();
            sending = _interactionBusy = true;
            dialog.IsPrimaryButtonEnabled = false;
            editor.IsReadOnly = true;
            UpdatePostInteractionState();
            string content = editor.Text;
            _commentDrafts[key] = new(content, uncertain);
            try
            {
                if (client.NeedsVerification) await client.VerifyAsync(MiyousheVerificationWindow.ShowAsync, ct);
                ct.ThrowIfCancellationRequested();
                await client.PublishReplyAsync(post, content, reply?.Id, ct);
                _commentDrafts.Remove(key);
                posted = true;
                args.Cancel = false;
                if (IsCurrentInteraction(client, post.Id, ct))
                    _post = _post! with { ReplyCount = (_post!.ReplyCount ?? 0) + 1 };
            }
            catch (CommunitySubmissionException)
            {
                uncertain = true;
                _commentDrafts[key] = new(content, true);
                if (!_closed)
                {
                    feedback.Message = "Miyoushe_SubmissionUncertain".GetLocalized();
                    feedback.IsOpen = true;
                }
            }
            catch (OperationCanceledException)
            {
                if (!ct.IsCancellationRequested && !_closed)
                {
                    feedback.Message = "Miyoushe_VerifyCancelled".GetLocalized();
                    feedback.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                if (!_closed && !ct.IsCancellationRequested)
                {
                    feedback.Message = ex is CommunityApiException api && api.NeedsVerification
                        ? "Miyoushe_CommentRiskHint".GetLocalized()
                        : ex is CommunityApiException login && login.LoginExpired
                            ? "Miyoushe_LoginHint".GetLocalized()
                            : ex.Message;
                    feedback.IsOpen = true;
                }
            }
            finally
            {
                sending = _interactionBusy = false;
                if (!_closed)
                {
                    editor.IsReadOnly = client.NeedsVerification;
                    dialog.PrimaryButtonText =
                        (client.NeedsVerification ? "Miyoushe_VerifyAndSend" :
                            uncertain ? "Miyoushe_Resubmit" : "Miyoushe_SendComment").GetLocalized();
                    dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(editor.Text);
                    UpdatePostInteractionState();
                }

                deferral.Complete();
            }
        };
        _dialogOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            if (!_closed) ReportError(ex, null);
        }
        finally
        {
            _dialogOpen = false;
        }

        if (posted && IsCurrentInteraction(client, post.Id, ct))
        {
            RememberPostInteraction(_post);
            UpdatePostInteractionState();
            _changingReplyOptions = true;
            _replyOrder = 2;
            ReplySortSelector.SelectedItem = ReplySortSelector.Items.OfType<SortChoice>().First(s => s.Value == 2);
            OnlyAuthor.IsChecked = false;
            _changingReplyOptions = false;
            ShowStatus("Miyoushe_CommentSent".GetLocalized(), InfoBarSeverity.Success);
            await LoadRepliesAsync(true);
        }
    }
}