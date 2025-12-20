using Microsoft.Win32;

namespace FufuLauncher.Helpers
{
    public static class GamePathFinder
    {
        public static string? FindGamePath()
        {
            try
            {

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\HYP\1_1\hk4e_cn"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("GameInstallPath")?.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {

                            var exePath = Path.Combine(value, "YuanShen.exe");
                            if (File.Exists(exePath))
                                return Path.GetDirectoryName(exePath);

                            var subDirPath = Path.Combine(value, "Genshin Impact Game", "YuanShen.exe");
                            if (File.Exists(subDirPath))
                                return Path.GetDirectoryName(subDirPath);
                        }
                    }
                }

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
                        return Path.GetDirectoryName(exePath);
                }
            }
            catch { }

            return null;
        }
    }
}