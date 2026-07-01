/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.Generic;
using System.Threading.Tasks;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Helpers
{
    public static class GameExeManager
    {
        public const string CustomExeNameKey = "CustomGameExeName";

        public static async Task<List<string>> GetExeNamesAsync()
        {
            try
            {
                var settings = App.GetService<ILocalSettingsService>();
                if (settings != null)
                {
                    var custom = await settings.ReadSettingAsync(CustomExeNameKey) as string;
                    if (!string.IsNullOrWhiteSpace(custom))
                    {
                        return new List<string> { custom.Trim() };
                    }
                }
            }
            catch { }

            return new List<string> { "YuanShen.exe", "GenshinImpact.exe" };
        }
    }
}
