/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Microsoft.UI.Xaml;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class DailyNoteWindow
{
    private async void DailyNoteWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_isWebViewInitialized) return;
        _isWebViewInitialized = true;
        await InitializeAndFetchAsync();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _ = InitializeAndFetchAsync();
    }

    private async Task InitializeAndFetchAsync()
    {
        _isRefreshing = true;
        try
        {
            StatusText = "DailyNote_Connecting".GetLocalized();
            MaterialGroups.Clear();
            Activities.Clear();

            await ScraperWebView.EnsureCoreWebView2Async();

            ScraperWebView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            ScraperWebView.CoreWebView2.Settings.IsScriptEnabled = true;

            await ScraperWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                "{\"width\": 1920, \"height\": 1080, \"deviceScaleFactor\": 1, \"mobile\": false}");

            bool materialsSuccess = false;
            int retryCountMat = 0;
            IsLoadingMaterials = true;

            while (!materialsSuccess)
            {
                if (retryCountMat > 0)
                {
                    StatusText = "DailyNote_Connecting".GetLocalized();
                    await Task.Delay(300);
                }

                materialsSuccess = await FetchDailyMaterialsAsync();

                if (!materialsSuccess)
                {
                    retryCountMat++;
                }
            }

            IsLoadingMaterials = false;

            bool activitiesSuccess = false;
            int retryCountAct = 0;
            IsLoadingActivities = true;

            while (!activitiesSuccess)
            {
                if (retryCountAct > 0)
                {
                    StatusText = "DailyNote_Connecting".GetLocalized();
                    await Task.Delay(300);
                }

                activitiesSuccess = await FetchActivitiesAsync();

                if (!activitiesSuccess)
                {
                    retryCountAct++;
                }
            }

            IsLoadingActivities = false;
            StatusText = "DailyNote_Updated".GetLocalized();
        }
        catch (Exception ex)
        {
            StatusText = "DailyNote_InitError".GetLocalized();
            DebugLog.Info("DailyNoteWindow.Scraper", $"[DailyNote] Init Error: {ex}");
            IsLoadingMaterials = false;
            IsLoadingActivities = false;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task<bool> WaitForElementAsync(string selector, int timeoutMs = 10000)
    {
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            try
            {
                string script = $"document.querySelectorAll('{selector}').length";
                string result = await ScraperWebView.ExecuteScriptAsync(script);
                if (int.TryParse(result, out int count) && count > 0) return true;
            }
            catch
            {
                /* ignored */
            }

            await Task.Delay(200);
            elapsed += 200;
        }

        DebugLog.Info("DailyNoteWindow.Scraper", $"[DailyNote] Timeout waiting for: {selector}");
        return false;
    }
}