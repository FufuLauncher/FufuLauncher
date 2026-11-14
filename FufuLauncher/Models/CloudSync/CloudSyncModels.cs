/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public sealed class FlexibleTimestampConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var intVal)
                ? intVal.ToString(CultureInfo.InvariantCulture)
                : ((long)reader.GetDouble()).ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} when parsing timestamp.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

public sealed class CloudApiEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok
    {
        get;
        set;
    }

    [JsonPropertyName("code")]
    public string Code
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("message")]
    public string Message
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement? Data
    {
        get;
        set;
    }

    [JsonPropertyName("details")]
    public JsonElement? Details
    {
        get;
        set;
    }
}

public sealed class CloudApiResult<T>
{
    public bool Ok
    {
        get;
        init;
    }

    public int HttpStatusCode
    {
        get;
        init;
    }

    public TimeSpan? RetryAfter
    {
        get;
        init;
    }

    public string Code
    {
        get;
        init;
    } = string.Empty;

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

    public CloudChallengeInfo? Challenge
    {
        get;
        init;
    }

    public bool RequiresCaptcha =>
        Challenge != null && string.Equals(Code, "CAPTCHA_REQUIRED", StringComparison.Ordinal);

    public bool IsPending => string.Equals(Code, "CHALLENGE_PENDING", StringComparison.Ordinal);

    public bool IsAccountLocked =>
        HttpStatusCode == 423 || string.Equals(Code, "ACCOUNT_LOCKED", StringComparison.Ordinal);
}

public sealed class CloudChallengeInfo
{
    [JsonPropertyName("challenge_id")]
    public string ChallengeId
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("challenge_secret")]
    public string ChallengeSecret
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("captcha_url")]
    public string CaptchaUrl
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("poll_url")]
    public string PollUrl
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn
    {
        get;
        set;
    }
}

public sealed class CloudLoginSessionData
{
    [JsonPropertyName("stoken")]
    public string Stoken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("stoken_expires_in")]
    public long StokenExpiresIn
    {
        get;
        set;
    }

    [JsonPropertyName("ltoken")]
    public string Ltoken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ltoken_expires_in")]
    public long LtokenExpiresIn
    {
        get;
        set;
    }

    [JsonPropertyName("account_status")]
    public string AccountStatus
    {
        get;
        set;
    } = "active";

    [JsonPropertyName("locked")]
    public bool Locked
    {
        get;
        set;
    }

    [JsonPropertyName("locked_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? LockedAt
    {
        get;
        set;
    }
}

public sealed class CloudLtokenIssueData
{
    [JsonPropertyName("ltoken")]
    public string Ltoken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("expires_in")]
    public long ExpiresIn
    {
        get;
        set;
    }

    [JsonPropertyName("account_status")]
    public string AccountStatus
    {
        get;
        set;
    } = "active";

    [JsonPropertyName("locked")]
    public bool Locked
    {
        get;
        set;
    }

    [JsonPropertyName("locked_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? LockedAt
    {
        get;
        set;
    }
}

public sealed class CloudAccountStatusData
{
    [JsonPropertyName("account_status")]
    public string AccountStatus
    {
        get;
        set;
    } = "active";

    [JsonPropertyName("locked")]
    public bool Locked
    {
        get;
        set;
    }

    [JsonPropertyName("locked_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? LockedAt
    {
        get;
        set;
    }
}

public sealed class CloudOtpSentData
{
    [JsonPropertyName("expires_in")]
    public int ExpiresIn
    {
        get;
        set;
    }
}

public sealed class CloudPersistedSession
{
    public string Email
    {
        get;
        set;
    } = string.Empty;

    public string Stoken
    {
        get;
        set;
    } = string.Empty;

    public long StokenExpiresAtUnix
    {
        get;
        set;
    }
}
