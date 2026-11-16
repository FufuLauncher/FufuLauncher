/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class DailyNoteWindow
{
    private async Task<bool> FetchActivitiesAsync()
    {
        StatusText = "DailyNote_FetchingActivities".GetLocalized();
        string url = ApiEndpoints.BaikeActivitiesUrl;

        try
        {
            Activities.Clear();

            var tcs = new TaskCompletionSource<bool>();
            void NavHandler(WebView2 s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(true);
            ScraperWebView.NavigationCompleted += NavHandler;
            ScraperWebView.Source = new Uri(url);

            var navTask = tcs.Task;
            var delayTask = Task.Delay(20000);
            var completedTask = await Task.WhenAny(navTask, delayTask);

            ScraperWebView.NavigationCompleted -= NavHandler;

            if (completedTask == delayTask) return false;

            if (!await WaitForElementAsync(".hit-card-list"))
            {
                DebugLog.Info("DailyNoteWindow.Activities", "[Activities] Wait Element Timeout");
                return false;
            }

            string script = @"
            (function() {
                var list = [];
                try {
                    var items = document.querySelectorAll('.hit-card-list li');
                    items.forEach(item => {
                       var title = item.querySelector('h5');
                       var subtitle = item.querySelector('p');
                       
                       var imgDiv = item.querySelector('.item__left');
                       var imgUrl = '';
                       if(imgDiv) {
                           imgUrl = imgDiv.getAttribute('data-src');
                           if(!imgUrl && imgDiv.style.backgroundImage) {
                               imgUrl = imgDiv.style.backgroundImage.slice(4, -1).replace(/['""]/g, '');
                           }
                       }
                       if(!imgUrl) {
                           var imgTag = item.querySelector('img');
                           if(imgTag) imgUrl = imgTag.getAttribute('data-src') || imgTag.src;
                       }

                       var countdown = item.querySelector('.count-down');
                       
                       if(title) {
                           list.push({
                               title: title.textContent.trim(),
                               subtitle: subtitle ? subtitle.textContent.trim() : '',
                               img: imgUrl || '',
                               time: countdown ? countdown.textContent.trim() : ''
                           });
                       }
                    });
                } catch(e) { return JSON.stringify({error: e.message}); }
                return JSON.stringify(list);
            })();";

            var json = await ScraperWebView.ExecuteScriptAsync(script);
            if (json != "null")
            {
                var unescapedJson = JsonSerializer.Deserialize<string>(json);
                var items = JsonSerializer.Deserialize<List<RawActivityItem>>(unescapedJson);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.title))
                        {
                            var actItem = new ActivityItem
                            {
                                Title = item.title,
                                Subtitle = item.subtitle,
                                Countdown = item.time
                            };
                            if (!string.IsNullOrEmpty(item.img))
                                actItem.Image = new BitmapImage(new Uri(item.img));
                            Activities.Add(actItem);
                        }
                    }
                }
            }

            return Activities.Count > 0;
        }
        catch (Exception ex)
        {
            DebugLog.Info("DailyNoteWindow.Activities", $"[Activity Fetch] {ex}");
            return false;
        }
    }
}