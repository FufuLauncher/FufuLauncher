using System.Text.Json.Serialization;

namespace FufuLauncher.Models
{
    public class HoyoverseBackgroundResponse
    {
        [JsonPropertyName("retcode")]
        public int Retcode
        {
            get; set;
        }

        [JsonPropertyName("message")]
        public string Message
        {
            get; set;
        }

        [JsonPropertyName("data")]
        public BackgroundData Data
        {
            get; set;
        }
    }

    public class BackgroundData
    {
        [JsonPropertyName("game_info_list")]
        public GameInfoItem[] GameInfoList
        {
            get; set;
        }
    }

    public class GameInfoItem
    {
        [JsonPropertyName("game")]
        public GameInfo Game
        {
            get; set;
        }

        [JsonPropertyName("backgrounds")]
        public BackgroundItem[] Backgrounds
        {
            get; set;
        }
    }

    public class GameInfo
    {
        [JsonPropertyName("id")]
        public string Id
        {
            get; set;
        }

        [JsonPropertyName("biz")]
        public string Biz
        {
            get; set;
        }
    }

    public class BackgroundItem
    {
        [JsonPropertyName("background")]
        public BackgroundInfo Background
        {
            get; set;
        }

        [JsonPropertyName("video")]
        public VideoInfo Video
        {
            get; set;
        }

        [JsonPropertyName("type")]
        public string Type
        {
            get; set;
        }
    }

    public class BackgroundInfo
    {
        [JsonPropertyName("url")]
        public string Url
        {
            get; set;
        }
    }

    public class VideoInfo
    {
        [JsonPropertyName("url")]
        public string Url
        {
            get; set;
        }

        [JsonPropertyName("size")]
        public long Size
        {
            get; set;
        }
    }
}