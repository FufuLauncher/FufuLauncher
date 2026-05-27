using System.Text.Json;

namespace FufuLauncher.Views
{
    public class BrowserConfig
    {
        public string HomePage { get; set; } = "https://www.bing.com";
        public double ZoomFactor { get; set; } = 1.0;
        public string FastForwardKey { get; set; } = "ArrowRight"; 
        public string RewindKey { get; set; } = "ArrowLeft";

        [System.Text.Json.Serialization.JsonIgnore]
        public static string ConfigPath => Path.Combine(Helpers.AppPaths.DataDir, "browser_config.json");

        public static BrowserConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<BrowserConfig>(json) ?? new BrowserConfig();
                }
            }
            catch
            {
                // ignored
            }

            return new BrowserConfig();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // ignored
            }
        }
    }
}