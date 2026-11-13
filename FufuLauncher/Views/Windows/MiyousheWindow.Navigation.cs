using FufuLauncher.Models.Miyoushe;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FufuLauncher.Views;

public sealed partial class MiyousheWindow
{
    private void InitializeBackNavigation()
    {
        var browserBack = new KeyboardAccelerator { Key = (VirtualKey)0xA6 };
        browserBack.Invoked += OnBackAccelerator;
        RootGrid.KeyboardAccelerators.Add(browserBack);
    }

    private sealed record PageState(
        FeedRequest Feed,
        string Title,
        CommunityPost? Post,
        CommunityPost[] Posts,
        string Cursor,
        Visibility More,
        string Description,
        double ScrollOffset,
        bool Loading);

    private PageState CapturePage() => new(_feed, FeedTitle.Text, _isPostOpen ? _post : null,
        _posts.ToArray(), _feedCursor, MorePostsButton.Visibility, FeedDescription.Text,
        FindFeedScroll()?.VerticalOffset ?? 0, _feedLoading);

    private ScrollViewer? FindFeedScroll() => FindScroll(PostsList);

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } child)
                return child;
        return null;
    }

    private void SetPageMode(bool post)
    {
        _isPostOpen = post;
        FeedPane.Visibility = post ? Visibility.Collapsed : Visibility.Visible;
        ReaderPane.Visibility = post ? Visibility.Visible : Visibility.Collapsed;
        ForumTabs.Visibility = !post && _feed.Kind == CommunityFeed.Forum ? Visibility.Visible : Visibility.Collapsed;
        SortTabs.Visibility = !post && SortTabs.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!post)
        {
            _readerTarget = null;
            _readerLoading = false;
            ReaderProgress.Visibility = Visibility.Collapsed;
        }

        FeedTitle.Visibility = post || _feed.Kind != CommunityFeed.Forum ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigationSelection();
        UpdateRefreshButtonState();
    }

    private void SetTabs(Panel panel, IEnumerable<(int Id, string Label)> choices, int selected, string group,
        RoutedEventHandler onChecked)
    {
        panel.Children.Clear();
        foreach (var (id, label) in choices)
        {
            var tab = new RadioButton
            {
                Content = label, Tag = id, GroupName = group,
                Style = (Style)RootGrid.Resources["CapsuleTabStyle"], IsChecked = id == selected
            };
            tab.Checked += onChecked;
            panel.Children.Add(tab);
        }
    }

    private void UpdateNavigationSelection()
    {
        foreach (var button in NavigationButtons.Children.OfType<RadioButton>())
            button.IsChecked = _shopOpen
                ? button == ShopNavigation
                : button.Tag is string kind && kind == _feed.Kind.ToString() &&
                  (_feed.Kind != CommunityFeed.User || _feed.Target == _client.AccountUid);
        FeedBackButton.Visibility = _pageHistory.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnFeedBack(object sender, RoutedEventArgs args) => await GoBackAsync();

    private async void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_dialogOpen || _goingBack || (!_shopOpen && _pageHistory.Count == 0)) return;
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        if (sender.Key == VirtualKey.Back)
        {
            for (var element = focused; element != null; element = VisualTreeHelper.GetParent(element))
                if (element is TextBox or RichEditBox or PasswordBox)
                    return;
        }

        args.Handled = true;
        if (_shopOpen)
        {
            if (!ShopPane.TryClosePlans()) CloseShop();
        }
        else await GoBackAsync();
    }

    private async Task GoBackAsync()
    {
        if (_dialogOpen || _goingBack || _closed || !_pageHistory.TryPop(out var previous)) return;
        _goingBack = true;
        try
        {
            _feedCancellation.Cancel();
            _feedLoading = false;
            FeedProgress.Visibility = Visibility.Collapsed;
            await NavigateAsync(previous.Feed.Kind, previous.Feed.Target, previous.Title,
                previous.Feed.GameId, previous.Feed.Sort, previous.Feed.ForumId, remember: false,
                load: previous.Loading);
            if (!previous.Loading)
            {
                _posts.Clear();
                foreach (var post in previous.Posts) _posts.Add(ApplyPostInteractionState(post));
                _feedCursor = previous.Cursor;
                MorePostsButton.Visibility = previous.More;
                FeedDescription.Text = previous.Description;
                FeedDescription.Visibility =
                    previous.Description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                EmptyFeed.Visibility = _posts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (previous.Post is { } selected) await OpenPostAsync(selected.Id, selected.GameId, remember: false);
            else
            {
                PostsList.Focus(FocusState.Programmatic);
                PostsList.UpdateLayout();
                FindFeedScroll()?.ChangeView(null, previous.ScrollOffset, null, true);
            }
        }
        finally
        {
            _goingBack = false;
            UpdateRefreshButtonState();
        }
    }
}