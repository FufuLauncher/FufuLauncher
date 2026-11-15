/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class DailyNoteWindow
{
    private async Task<bool> FetchDailyMaterialsAsync()
    {
        StatusText = "DailyNote_FetchingMaterials".GetLocalized();
        string url = ApiEndpoints.BaikeDailyMaterialsUrl;

        try
        {
            MaterialGroups.Clear();

            var tcs = new TaskCompletionSource<bool>();
            void NavHandler(WebView2 s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(true);

            ScraperWebView.NavigationCompleted += NavHandler;
            ScraperWebView.Source = new Uri(url);

            var navTask = tcs.Task;
            var delayTask = Task.Delay(20000);
            var completedTask = await Task.WhenAny(navTask, delayTask);

            ScraperWebView.NavigationCompleted -= NavHandler;

            if (completedTask == delayTask)
            {
                DebugLog.Info("DailyNoteWindow.Materials", "[DailyNote] Navigation Timeout");
                return false;
            }

            if (!await WaitForElementAsync(".channel__calendar"))
            {
                DebugLog.Info("DailyNoteWindow.Materials", "[DailyNote] Wait Element Timeout");
                return false;
            }

            string script = @"
            (function() {
                var result = { date: '', birthday: null, groups: [] };
                try {
                    var dateEl = document.querySelector('.calendar__today-date');
                    if(dateEl) result.date = dateEl.innerText.trim().replace(/[\r\n]/g, ' ');
                    var bdayEl = document.querySelector('.calendar__today-event-birthday');
                    if(bdayEl) {
                       var name = bdayEl.querySelector('.calendar__today-name');
                       var img = bdayEl.querySelector('img');
                       result.birthday = { 
                           name: name ? name.textContent.replace('今天是','').replace('的生日哦~','').trim() : '', 
                           img: img ? img.src : '' 
                       };
                    }
                    var rows = document.querySelectorAll('.cal-pc__row');
                    rows.forEach(row => {
                        var colName = row.querySelector('.cal-pc__col');
                        if(!colName || colName.classList.contains('cal-pc__slider') || colName.classList.contains('cal-pc__month')) return;
                        var catName = colName.textContent.trim();
                        if(catName.length < 2 && '一二三四五六日'.includes(catName)) return;
                        var group = { category: catName, items: [] };
                        var items = row.querySelectorAll('.cal-pc__item');
                        items.forEach(item => {
                            var nameEl = item.querySelector('.cal-pc__name');
                            var imgEl = item.querySelector('.cal-pc__img');
                            var imgUrl = imgEl ? (imgEl.getAttribute('data-src') || imgEl.src) : '';
                            var popover = item.querySelector('.cal-material-popover');
                            var domainName = '';
                            var matImages = [];
                            if(popover) {
                                var domainEl = popover.querySelector('.way__content--title');
                                if(domainEl) domainName = domainEl.textContent.trim();
                                popover.querySelectorAll('.cal-material-popover__item img').forEach(m => {
                                    matImages.push(m.getAttribute('data-src') || m.src);
                                });
                            }
                            if(nameEl) {
                                group.items.push({
                                    name: nameEl.textContent.trim(),
                                    icon: imgUrl,
                                    domain: domainName,
                                    materials: matImages
                                });
                            }
                        });
                        if(group.items.length > 0) result.groups.push(group);
                    });
                } catch(e) { return JSON.stringify({error: e.message}); }
                return JSON.stringify(result);
            })();";
            var json = await ScraperWebView.ExecuteScriptAsync(script);
            if (json != "null")
            {
                var unescapedJson = JsonSerializer.Deserialize<string>(json);
                if (unescapedJson != null && unescapedJson.Contains("\"error\":"))
                {
                    DebugLog.Info("DailyNoteWindow.Materials", $"[Script Error] {unescapedJson}");
                    return false;
                }

                using var doc = JsonDocument.Parse(unescapedJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("date", out var d)) TodayDate = d.GetString();

                if (root.TryGetProperty("birthday", out var bday) && bday.ValueKind != JsonValueKind.Null)
                {
                    BirthdayRoleName = bday.GetProperty("name").GetString();
                    var bUrl = bday.GetProperty("img").GetString();
                    if (!string.IsNullOrEmpty(bUrl)) BirthdayRoleIcon = new BitmapImage(new Uri(bUrl));
                }

                if (root.TryGetProperty("groups", out var groups))
                {
                    foreach (var g in groups.EnumerateArray())
                    {
                        var groupObj = new MaterialGroup { CategoryName = g.GetProperty("category").GetString() };
                        foreach (var item in g.GetProperty("items").EnumerateArray())
                        {
                            var rawItem = JsonSerializer.Deserialize<RawMaterialItem>(item.GetRawText());
                            var mItem = new MaterialItem
                            {
                                Name = rawItem?.name,
                                DomainName = rawItem?.domain
                            };
                            if (!string.IsNullOrEmpty(rawItem?.icon))
                                mItem.IconImage = new BitmapImage(new Uri(rawItem.icon));
                            foreach (var matUrl in rawItem?.materials!)
                            {
                                if (!string.IsNullOrEmpty(matUrl))
                                    mItem.Materials.Add(new BitmapImage(new Uri(matUrl)));
                            }

                            groupObj.Items.Add(mItem);
                        }

                        MaterialGroups.Add(groupObj);
                    }
                }
            }

            return MaterialGroups.Count > 0;
        }
        catch (Exception ex)
        {
            DebugLog.Info("DailyNoteWindow.Materials", $"[Material Fetch] {ex}");
            return false;
        }
    }

    private void UpdateBirthdayVisibility()
    {
        BirthdayInfoVisibility = string.IsNullOrEmpty(BirthdayRoleName) ? Visibility.Collapsed : Visibility.Visible;
    }
}