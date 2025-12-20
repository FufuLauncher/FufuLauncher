using System.Text.Json.Serialization;

namespace FufuLauncher.Models
{
    public class GameAccount
    {
        public Guid InnerId { get; set; } = Guid.NewGuid();

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SchemeType Type
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;
        public string MihoyoSDK { get; set; } = string.Empty;
        public string? Mid
        {
            get; set;
        }
        public string? MacAddress
        {
            get; set;
        }
        public bool IsExpired
        {
            get; set;
        }

        [JsonPropertyName("lastUsed")]
        public DateTime? LastUsed
        {
            get; set;
        }

        public void UpdateName(string name)
        {
            Name = name;
        }

        public static GameAccount Create(SchemeType type, string name, string mihoyoSdk, string? mid, string? macAddress)
        {
            return new GameAccount
            {
                Type = type,
                Name = name,
                MihoyoSDK = mihoyoSdk,
                Mid = mid,
                MacAddress = macAddress,
                IsExpired = false,
                LastUsed = DateTime.Now
            };
        }
    }

    public enum SchemeType
    {
        ChineseOfficial,
        Oversea,
        ChineseBilibili
    }
}