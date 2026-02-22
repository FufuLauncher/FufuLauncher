using System.Diagnostics;
using Microsoft.Win32;

namespace FufuLauncher.Helpers
{
    public static class GamePathFinder
    {
        public static string? FindGamePath()
        {
            Debug.WriteLine("========== [Debug] FindGamePath 开始 ==========");
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\HYP\1_1\hk4e_cn"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("GameInstallPath")?.ToString();
                        Debug.WriteLine($"[Debug] 成功读取注册表 GameInstallPath，值为: '{value}'");
                        
                        if (!string.IsNullOrEmpty(value))
                        {
                            var exePath = Path.Combine(value, "YuanShen.exe");
                            if (File.Exists(exePath))
                            {
                                Debug.WriteLine($"[Debug] 验证通过: 找到文件 {exePath}");
                                return Path.GetDirectoryName(exePath);
                            }

                            var subDirPath = Path.Combine(value, "Genshin Impact Game", "YuanShen.exe");
                            if (File.Exists(subDirPath))
                            {
                                Debug.WriteLine($"[Debug] 验证通过: 找到文件 {subDirPath}");
                                return Path.GetDirectoryName(subDirPath);
                            }
                            
                            Debug.WriteLine("[Debug] 注册表路径存在，但该路径下没找到 YuanShen.exe！");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[Debug] 注册表项 hk4e_cn 不存在或为 null。");
                    }
                }

                Debug.WriteLine("[Debug] 准备检查常见默认路径...");
                string[] commonPaths = {
                    @"C:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                    @"D:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                    @"E:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                    @"C:\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                    @"D:\Genshin Impact\Genshin Impact Game\YuanShen.exe",
                    @"E:\Genshin Impact\Genshin Impact Game\YuanShen.exe"
                };

                foreach (var exePath in commonPaths)
                {
                    if (File.Exists(exePath))
                    {
                        Debug.WriteLine($"[Debug] 在常见路径中找到游戏: {exePath}");
                        return Path.GetDirectoryName(exePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Debug] FindGamePath 发生异常: {ex.Message}");
            }

            Debug.WriteLine("[Debug] FindGamePath 最终未找到任何路径，返回 null。");
            return null;
        }
    }
}