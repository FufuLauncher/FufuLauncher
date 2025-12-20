using System.Text.Json.Serialization;

namespace FufuLauncher.Models
{
    public class HoyoverseContentResponse
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
        public ContentData Data
        {
            get; set;
        }
    }

    public class ContentData
    {
        [JsonPropertyName("content")]
        public ContentInfo Content
        {
            get; set;
        }
    }

    public class ContentInfo
    {
        [JsonPropertyName("game")]
        public GameInfo Game
        {
            get; set;
        }

        [JsonPropertyName("language")]
        public string Language
        {
            get; set;
        }

        [JsonPropertyName("banners")]
        public BannerItem[] Banners
        {
            get; set;
        }

        [JsonPropertyName("posts")]
        public PostItem[] Posts
        {
            get; set;
        }

        [JsonPropertyName("social_media_list")]
        public SocialMediaItem[] SocialMediaList
        {
            get; set;
        }
    }

    public class BannerItem
    {
        [JsonPropertyName("id")]
        public string Id
        {
            get; set;
        }

        [JsonPropertyName("image")]
        public ImageInfo Image
        {
            get; set;
        }

        [JsonPropertyName("i18n_identifier")]
        public string I18nIdentifier
        {
            get; set;
        }
    }

    public class PostItem
    {
        [JsonPropertyName("id")]
        public string Id
        {
            get; set;
        }

        [JsonPropertyName("type")]
        public string Type
        {
            get; set;
        }

        [JsonPropertyName("title")]
        public string Title
        {
            get; set;
        }

        [JsonPropertyName("link")]
        public string Link
        {
            get; set;
        }

        [JsonPropertyName("date")]
        public string Date
        {
            get; set;
        }

        [JsonPropertyName("i18n_identifier")]
        public string I18nIdentifier
        {
            get; set;
        }
    }

    public class SocialMediaItem
    {
        [JsonPropertyName("id")]
        public string Id
        {
            get; set;
        }

        [JsonPropertyName("icon")]
        public ImageInfo Icon
        {
            get; set;
        }

        [JsonPropertyName("qr_image")]
        public ImageInfo QrImage
        {
            get; set;
        }

        [JsonPropertyName("qr_desc")]
        public string QrDesc
        {
            get; set;
        }

        [JsonPropertyName("links")]
        public SocialLink[] Links
        {
            get; set;
        }

        [JsonPropertyName("enable_red_dot")]
        public bool EnableRedDot
        {
            get; set;
        }

        [JsonPropertyName("red_dot_content")]
        public string RedDotContent
        {
            get; set;
        }
    }

    public class SocialLink
    {
        [JsonPropertyName("title")]
        public string Title
        {
            get; set;
        }

        [JsonPropertyName("link")]
        public string Link
        {
            get; set;
        }

        [JsonPropertyName("login_state_in_link")]
        public bool LoginStateInLink
        {
            get; set;
        }
    }

    public class ImageInfo
    {
        [JsonPropertyName("url")]
        public string Url
        {
            get; set;
        }

        [JsonPropertyName("link")]
        public string Link
        {
            get; set;
        }

        [JsonPropertyName("login_state_in_link")]
        public bool LoginStateInLink
        {
            get; set;
        }

        [JsonPropertyName("md5")]
        public string Md5
        {
            get; set;
        }

        [JsonPropertyName("size")]
        public long Size
        {
            get; set;
        }

        [JsonPropertyName("hover_url")]
        public string HoverUrl
        {
            get; set;
        }
    }
}