using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public sealed class HutaoApiResponse<T>
{
    [JsonPropertyName("retcode")]
    public int RetCode
    {
        get;
        set;
    }

    [JsonPropertyName("message")]
    public string? Message
    {
        get;
        set;
    }

    [JsonPropertyName("data")]
    public T? Data
    {
        get;
        set;
    }

    [JsonPropertyName("l10nKey")]
    public string? L10nKey
    {
        get;
        set;
    }

    [JsonIgnore] public bool IsSuccess => RetCode == 0;
}

public sealed class HutaoTokenSet
{
    [JsonPropertyName("AccessToken")]
    public string AccessToken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ExpiresIn")]
    public int ExpiresIn
    {
        get;
        set;
    }

    [JsonPropertyName("RefreshToken")]
    public string RefreshToken
    {
        get;
        set;
    } = string.Empty;
}

public sealed class HutaoUserInfo
{
    [JsonPropertyName("NormalizedUserName")]
    public string? NormalizedUserName
    {
        get;
        set;
    }

    [JsonPropertyName("UserName")]
    public string? UserName
    {
        get;
        set;
    }

    [JsonPropertyName("CdnExpireAt")]
    public DateTimeOffset? CdnExpireAt
    {
        get;
        set;
    }

    [JsonPropertyName("GachaLogExpireAt")]
    public DateTimeOffset? GachaLogExpireAt
    {
        get;
        set;
    }

    [JsonPropertyName("IsLicensedDeveloper")]
    public bool IsLicensedDeveloper
    {
        get;
        set;
    }

    [JsonPropertyName("IsMaintainer")]
    public bool IsMaintainer
    {
        get;
        set;
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields
    {
        get;
        set;
    }

    [JsonIgnore]
    public bool HasValidGachaPlan =>
        IsMaintainer || IsLicensedDeveloper ||
        (GachaLogExpireAt.HasValue && GachaLogExpireAt.Value > DateTimeOffset.UtcNow);

    [JsonIgnore]
    public bool HasValidCdnPlan =>
        IsMaintainer || IsLicensedDeveloper || (CdnExpireAt.HasValue && CdnExpireAt.Value > DateTimeOffset.UtcNow);
}

public sealed class HutaoLoggedInDevice
{
    [JsonPropertyName("DeviceId")]
    public string? DeviceId
    {
        get;
        set;
    }

    [JsonPropertyName("Id")]
    public string? Id
    {
        get;
        set;
    }

    [JsonPropertyName("DeviceName")]
    public string? DeviceName
    {
        get;
        set;
    }

    [JsonPropertyName("Name")]
    public string? Name
    {
        get;
        set;
    }

    [JsonPropertyName("OperatingSystem")]
    public string? OperatingSystem
    {
        get;
        set;
    }

    [JsonPropertyName("OS")]
    public string? Os
    {
        get;
        set;
    }

    [JsonPropertyName("Platform")]
    public string? Platform
    {
        get;
        set;
    }

    [JsonPropertyName("LastLoginTime")]
    public string? LastLoginTime
    {
        get;
        set;
    }

    [JsonPropertyName("LoginTime")]
    public string? LoginTime
    {
        get;
        set;
    }

    [JsonPropertyName("CreatedAt")]
    public string? CreatedAt
    {
        get;
        set;
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields
    {
        get;
        set;
    }

    [JsonIgnore]
    public string EffectiveDeviceId =>
        FirstNonEmpty(DeviceId, Id, ReadExtraString("device_id"), ReadExtraString("id"));

    [JsonIgnore]
    public string EffectiveDeviceName =>
        FirstNonEmpty(DeviceName, Name, ReadExtraString("device_name"), ReadExtraString("name"));

    [JsonIgnore]
    public string EffectiveOs =>
        FirstNonEmpty(OperatingSystem, Os, Platform, ReadExtraString("os"), ReadExtraString("operating_system"),
            ReadExtraString("system"));

    [JsonIgnore]
    public string EffectiveLoginTime =>
        FirstNonEmpty(LastLoginTime, LoginTime, CreatedAt, ReadExtraString("last_login_time"),
            ReadExtraString("login_time"), ReadExtraString("created_at"));

    private string? ReadExtraString(string key)
    {
        if (ExtraFields == null) return null;
        foreach (var kv in ExtraFields)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : kv.Value.ToString();
            }
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        }

        return string.Empty;
    }
}

public sealed class HutaoOperationResult<T>
{
    public bool Ok
    {
        get;
        init;
    }

    public string Message
    {
        get;
        init;
    } = string.Empty;

    public T? Data
    {
        get;
        init;
    }

    public bool AuthInvalidated
    {
        get;
        init;
    }
}

public sealed class HutaoDeviceListItem
{
    public string DeviceId
    {
        get;
        init;
    } = string.Empty;

    public string TitleText
    {
        get;
        init;
    } = string.Empty;

    public string SubtitleText
    {
        get;
        init;
    } = string.Empty;

    public string BadgeText
    {
        get;
        init;
    } = string.Empty;

    public bool IsCurrentDevice
    {
        get;
        init;
    }
}

public sealed class HutaoAccountExtraFieldItem
{
    public string Label
    {
        get;
        init;
    } = string.Empty;

    public string Value
    {
        get;
        init;
    } = string.Empty;
}

public sealed class AccountBoundRoleOption
{
    public bool IsAllRolesOption
    {
        get;
        init;
    }

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public string Nickname
    {
        get;
        init;
    } = string.Empty;

    public string ServerName
    {
        get;
        init;
    } = string.Empty;

    public int Level
    {
        get;
        init;
    }

    public bool IsActiveRole
    {
        get;
        init;
    }

    public string DisplayText
    {
        get;
        init;
    } = string.Empty;

    public string ShortDisplayText
    {
        get;
        init;
    } = string.Empty;

    public override string ToString() => DisplayText;
}

public sealed class HutaoPersistedSession
{
    public string Email
    {
        get;
        set;
    } = string.Empty;

    public string AccessToken
    {
        get;
        set;
    } = string.Empty;

    public long AccessTokenExpiresAtUnix
    {
        get;
        set;
    }

    public string RefreshToken
    {
        get;
        set;
    } = string.Empty;
}

public sealed class HutaoJsonPayloadHelper
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}