/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Contracts.Services;
using FufuLauncher.Views;
using Microsoft.UI.Dispatching;

namespace FufuLauncher.Services;

public class StarPromotionService
{
    private readonly ILocalSettingsService _settingsService;
    private readonly DispatcherQueue _dispatcherQueue;

    public StarPromotionService(ILocalSettingsService settingsService, DispatcherQueue dispatcherQueue)
    {
        _settingsService = settingsService;
        _dispatcherQueue = dispatcherQueue;
    }

    public async Task CheckAndShowPromptAsync()
    {
        var isDismissedObj = await _settingsService.ReadSettingAsync("StarPromptDismissed");
        if (isDismissedObj != null && Convert.ToBoolean(isDismissedObj))
        {
            return;
        }

        var firstLaunchObj = await _settingsService.ReadSettingAsync("FirstLaunchDate");
        if (firstLaunchObj == null)
        {
            await _settingsService.SaveSettingAsync("FirstLaunchDate", DateTime.Now.ToString("O"));
            await _settingsService.SaveSettingAsync("NextStarPromptDate", DateTime.Now.AddDays(7).ToString("O"));
            return;
        }

        var nextPromptObj = await _settingsService.ReadSettingAsync("NextStarPromptDate");
        if (nextPromptObj != null && DateTime.TryParse(nextPromptObj.ToString(), out DateTime nextPromptDate))
        {
            if (DateTime.Now >= nextPromptDate)
            {
                _dispatcherQueue.TryEnqueue(() => 
                {
                    var window = new StarPromptWindow(_settingsService);
                    window.Activate();
                });
            }
        }
        else if (firstLaunchObj != null && DateTime.TryParse(firstLaunchObj.ToString(), out DateTime firstLaunchDate))
        {
            if ((DateTime.Now - firstLaunchDate).TotalDays >= 7)
            {
                _dispatcherQueue.TryEnqueue(() => 
                {
                    var window = new StarPromptWindow(_settingsService);
                    window.Activate();
                });
            }
        }
    }
}
