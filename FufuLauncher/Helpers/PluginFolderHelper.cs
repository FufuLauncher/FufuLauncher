/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;

namespace FufuLauncher.Helpers
{
    public static class PluginFolderHelper
    {
        public static void CheckAndCreatePluginsFolder()
        {
            try
            {
                var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins");
                
                if (!Directory.Exists(pluginsPath))
                {
                    Directory.CreateDirectory(pluginsPath);
                    Debug.WriteLine("已自动创建 Plugins 文件夹");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建 Plugins 文件夹失败: {ex.Message}");
            }
        }
    }
}
