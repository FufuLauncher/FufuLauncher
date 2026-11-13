/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;
using FufuLauncher.Models;
using Windows.System;
using FufuLauncher.Helpers;
using FufuLauncher.Services.Help;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class HelpPage : Page
{
    public HelpViewModel ViewModel
    {
        get;
    } = new();

    private readonly Dictionary<TreeViewNode, DocItem> _nodeToDocItemMap = new();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchFlyoutDebounce;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _renderDebounce;

    private Task? _webInitialization;

    public HelpPage()
    {
        this.InitializeComponent();
        this.Loaded += HelpPage_Loaded;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ActualThemeChanged += (_, _) => RequestRender();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HelpViewModel.MarkdownContent) or nameof(HelpViewModel.MarkdownUriPrefix)
            or nameof(HelpViewModel.CurrentTitle) or nameof(HelpViewModel.CurrentCategory)
            or nameof(HelpViewModel.CurrentAuthor) or nameof(HelpViewModel.CurrentDate))
            RequestRender();
    }

    private void RequestRender()
    {
        if (_renderDebounce is null)
        {
            _ = RenderAsync();
            return;
        }

        _renderDebounce.Stop();
        _renderDebounce.Start();
    }

    private async void RenderDebounce_Tick()
    {
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        var core = await EnsureCoreWebViewAsync();
        if (core is null)
            return;

        var dark = ActualTheme == ElementTheme.Dark;
        var meta = new HelpDocumentMeta(ViewModel.CurrentTitle, ViewModel.CurrentCategory, ViewModel.CurrentAuthor,
            ViewModel.CurrentDate);
        var html = HelpDocumentRenderer.Render(ViewModel.MarkdownContent, meta, ViewModel.MarkdownUriPrefix, dark,
            AccentColor);

        core.NavigateToString(html);
    }

    private string AccentColor
    {
        get
        {
            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) &&
                value is Windows.UI.Color color)
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            return ActualTheme == ElementTheme.Dark ? "#4CC2FF" : "#005FB8";
        }
    }

    private async Task<CoreWebView2?> EnsureCoreWebViewAsync()
    {
        try
        {
            _webInitialization ??= InitializeWebViewAsync();
            await _webInitialization;
            return HelpWebView.CoreWebView2;
        }
        catch
        {
            _webInitialization = null;
            return null;
        }
    }

    private async Task InitializeWebViewAsync()
    {
        await HelpWebView.EnsureCoreWebView2Async();

        var core = HelpWebView.CoreWebView2;
        var settings = core.Settings;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        HelpWebView.DefaultBackgroundColor = ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 28, 28, 30)
            : Windows.UI.Color.FromArgb(255, 251, 251, 251);

        if (HelpDocumentRenderer.HasCustomFont)
            core.SetVirtualHostNameToFolderMapping(ApiEndpoints.HelpFontVirtualHost, HelpDocumentRenderer.CustomFontDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

        core.NavigationStarting += (sender, e) =>
        {
            if (HelpDocumentRenderer.TryExternalUri(e.Uri, out var uri))
            {
                e.Cancel = true;
                _ = Launcher.LaunchUriAsync(uri);
            }
        };

        core.NewWindowRequested += (sender, e) =>
        {
            e.Handled = true;
            if (HelpDocumentRenderer.TryExternalUri(e.Uri, out var uri))
                _ = Launcher.LaunchUriAsync(uri);
        };

        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
    }

    private async void HelpPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        _searchFlyoutDebounce ??= CreateDebounceTimer(320, SearchFlyoutDebounce_Tick);
        _renderDebounce ??= CreateDebounceTimer(180, RenderDebounce_Tick);

        _ = RenderAsync();
        await ViewModel.InitializeAsync();
        BuildTree(string.Empty);
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? CreateDebounceTimer(int intervalMs, Action onTick)
    {
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
            return null;

        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => onTick();
        return timer;
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

    private void SearchFlyoutDebounce_Tick()
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
            ? string.Format("HelpPage_SearchResults".GetLocalized(), n)
            : "HelpPage_NoResults".GetLocalized();

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

    private void TranslateToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ToggleTranslationCommand.CanExecute(null))
            ViewModel.ToggleTranslationCommand.Execute(null);
    }
}