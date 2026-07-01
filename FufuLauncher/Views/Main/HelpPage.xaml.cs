/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;
using FufuLauncher.Models;
using Windows.System;
using CommunityToolkit.WinUI.UI.Controls;

namespace FufuLauncher.Views;

public sealed partial class HelpPage : Page
{
    public HelpViewModel ViewModel { get; } = new();

    private readonly Dictionary<TreeViewNode, DocItem> _nodeToDocItemMap = new();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchFlyoutDebounce;

    public HelpPage()
    {
        this.InitializeComponent();
        this.Loaded += HelpPage_Loaded;
    }
    
    private void HelpMarkdown_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not MarkdownTextBlock md)
            return;

        var w = e.NewSize.Width;
        if (double.IsInfinity(w) || double.IsNaN(w) || w <= 0)
            return;

        var cap = Math.Max(160.0, Math.Floor(w - 2));
        md.ImageMaxWidth = cap;
        md.ImageMaxHeight = Math.Min(2400, cap * 3);
    }

    private async void HelpPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        if (_searchFlyoutDebounce is null)
        {
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dq is not null)
            {
                _searchFlyoutDebounce = dq.CreateTimer();
                _searchFlyoutDebounce.Interval = TimeSpan.FromMilliseconds(320);
                _searchFlyoutDebounce.IsRepeating = false;
                _searchFlyoutDebounce.Tick += SearchFlyoutDebounce_Tick;
            }
        }

        await ViewModel.InitializeAsync();
        BuildTree(string.Empty);
    }

    private void BuildTree(string? filter)
    {
        var f = (filter ?? string.Empty).Trim();
        DirectoryTreeView.RootNodes.Clear();
        _nodeToDocItemMap.Clear();
        ViewModel.UpdateSearchHits(f);

        if (string.IsNullOrEmpty(f))
        {
            foreach (var cat in ViewModel.AllCategories)
            {
                var categoryNode = new TreeViewNode { Content = cat.CategoryName, IsExpanded = true };
                foreach (var item in cat.Items)
                {
                    var itemNode = new TreeViewNode { Content = item.Title };
                    _nodeToDocItemMap[itemNode] = item;
                    categoryNode.Children.Add(itemNode);
                }
                DirectoryTreeView.RootNodes.Add(categoryNode);
            }
            return;
        }

        foreach (var group in ViewModel.SearchHits.GroupBy(h => h.CategoryName))
        {
            var categoryNode = new TreeViewNode { Content = group.Key, IsExpanded = true };
            foreach (var hit in group)
            {
                var itemNode = new TreeViewNode { Content = hit.Item.Title };
                _nodeToDocItemMap[itemNode] = hit.Item;
                categoryNode.Children.Add(itemNode);
            }
            if (categoryNode.Children.Count > 0)
                DirectoryTreeView.RootNodes.Add(categoryNode);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var trimmed = (SearchBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _searchFlyoutDebounce?.Stop();
            HideSearchFlyout();
            BuildTree(string.Empty);
            return;
        }

        BuildTree(trimmed);
        _searchFlyoutDebounce?.Stop();
        _searchFlyoutDebounce?.Start();
    }

    private void SearchFlyoutDebounce_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var trimmed = (SearchBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            HideSearchFlyout();
            return;
        }

        ViewModel.UpdateSearchHits(trimmed);
        var n = ViewModel.SearchHits.Count;
        SearchFlyoutSummary.Text = n > 0
            ? $"找到 {n} 个结果，点击条目打开"
            : "未找到匹配的文档";

        global::Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(SearchBox);
    }

    private void HideSearchFlyout()
    {
        if (global::Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(SearchBox) is Flyout flyout)
            flyout.Hide();
    }

    private async void SearchHitsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DocSearchHit hit)
            return;
        await ViewModel.LoadDocumentAsync(hit.Item);
        HideSearchFlyout();
    }

    private async void DirectoryTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node)
        {
            if (_nodeToDocItemMap.TryGetValue(node, out var item))
            {
                await ViewModel.LoadDocumentAsync(item);
            }
            else
            {
                node.IsExpanded = !node.IsExpanded;
            }
        }
    }

    private async void HelpMarkdown_LinkClicked(object sender, LinkClickedEventArgs e)
    {
        var link = e.Link?.Trim();
        if (string.IsNullOrEmpty(link))
            return;

        Uri? target = null;
        if (link.StartsWith("//", StringComparison.Ordinal))
        {
            if (Uri.TryCreate("https:" + link, UriKind.Absolute, out var protocolRelative))
                target = protocolRelative;
        }
        else if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            target = absolute;
        }
        else if (!string.IsNullOrEmpty(ViewModel.MarkdownUriPrefix) &&
                 Uri.TryCreate(new Uri(ViewModel.MarkdownUriPrefix, UriKind.Absolute), link, out var relative))
        {
            target = relative;
        }

        if (target is null)
            return;

        var scheme = target.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https" or "mailto"))
            return;

        _ = await Launcher.LaunchUriAsync(target);
    }
}
