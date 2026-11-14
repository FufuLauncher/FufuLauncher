using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Data.Entities;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.CycleChallenge.Dto;
using FufuLauncher.Models.UIGF;
using FufuLauncher.Services.CycleChallenge;

namespace FufuLauncher.Services;

public sealed partial class HutaoCloudService : IHutaoCloudService
{
    private const string HutaoPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA5W2SEyZSlP2zBI1Sn8Gd\n" +
        "TwbZoXlUGNKyoVrY8SVYu9GMefdGZCrUQNkCG/Np8pWPmSSEFGd5oeug/oIMtCZQ\n" +
        "NOn0drlR+pul/XZ1KQhKmj/arWjN1XNok2qXF7uxhqD0JyNT/Fxy6QvzqIpBsM9S\n" +
        "7ajm8/BOGlPG1SInDPaqTdTRTT30AuN+IhWEEFwT3Ctv1SmDupHs2Oan5qM7Y3uw\n" +
        "b6K1rbnk5YokiV2FzHajGUymmSKXqtG1USZzwPqImpYb4Z0M/StPFWdsKqexBqMM\n" +
        "mkXckI5O98GdlszEmQ0Ejv5Fx9fR2rXRwM76S4iZTfabYpiMbb4bM42mHMauupj6\n" +
        "9QIDAQAB\n" +
        "-----END PUBLIC KEY-----";

    private const string SessionFileName = "hutao_cloud_session.dat";
    private const string DeviceIdFileName = "hutao_device_id.txt";

    private static readonly Uri BaseUri = new(ApiEndpoints.HutaoApiBaseUrl + "/", UriKind.Absolute);
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("FufuLauncher.HutaoCloud.Session.v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly string[] ExactWishTimeFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff"
    };

    private static readonly string[] ProblemDetailKeys =
    {
        "detail",
        "message",
        "title"
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> SupportedUigfLangs = new(StringComparer.Ordinal)
    {
        "de-de", "en-us", "es-es", "fr-fr", "id-id", "it-it", "ja-jp", "ko-kr",
        "pt-pt", "ru-ru", "th-th", "tr-tr", "vi-vn", "zh-cn", "zh-tw"
    };

    private static readonly HashSet<int> ValidGachaTypes = new()
    {
        100, 200, 301, 302, 400, 500
    };

    private static readonly int[] QueryTypes = { 100, 200, 301, 302, 500 };

    [GeneratedRegex("^[0-9]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidIdRegex();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidTimeRegex();

    private readonly ICloudSyncService _cloudSync;
    private readonly MetadataRepository _metadataRepo;
    private readonly GameRecordApiClient _gameRecordApiClient;
    private readonly HttpClient _hutaoHttpClient;
    private readonly HttpClient _catalogHttpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private readonly string _sessionFilePath;
    private readonly string _deviceIdFilePath;
    private readonly string _deviceId;

    private string _email = string.Empty;
    private string _accessToken = string.Empty;
    private long _accessTokenExpiresAtUnix;
    private string _refreshToken = string.Empty;
    private HutaoUserInfo? _currentUserInfo;
    private DateTimeOffset? _verifyCodeCooldownUntil;
    private bool _calculatorCatalogLoaded;

    public event EventHandler? SessionChanged;

    public HutaoCloudService(
        ICloudSyncService cloudSync,
        MetadataRepository metadataRepo,
        GameRecordApiClient gameRecordApiClient)
    {
        _cloudSync = cloudSync;
        _metadataRepo = metadataRepo;
        _gameRecordApiClient = gameRecordApiClient;
        _sessionFilePath = Path.Combine(AppPaths.SettingsDir, SessionFileName);
        _deviceIdFilePath = Path.Combine(AppPaths.SettingsDir, DeviceIdFileName);
        _deviceId = LoadOrCreateDeviceId();

        var appVersion = !string.IsNullOrWhiteSpace(AppVersionHelper.NumericVersion)
            ? AppVersionHelper.NumericVersion
            : "1.0.0";

        _hutaoHttpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(45)
        };
        _hutaoHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-hutao-device-id", _deviceId);
        _hutaoHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"FufuLauncher/{appVersion}");

        _catalogHttpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _catalogHttpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _catalogHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", ApiEndpoints.WebstaticRefererUrl);
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(_refreshToken) && !string.IsNullOrEmpty(_accessToken);

    public string CurrentEmail => _email;

    public HutaoUserInfo? CurrentUserInfo => _currentUserInfo;

    public string CurrentDeviceId => _deviceId;

    public DateTimeOffset? VerifyCodeCooldownUntil => _verifyCodeCooldownUntil;

    public async Task InitializeAsync()
    {
        LoadSessionFromDisk();
        if (!IsLoggedIn) return;

        var ok = await EnsureAuthenticatedAsync();
        if (ok)
        {
            await RefreshUserInfoAsync();
        }
    }

    public async Task<bool> EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken)) return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!string.IsNullOrEmpty(_accessToken) && _accessTokenExpiresAtUnix > now + 60)
        {
            return true;
        }

        await _authLock.WaitAsync();
        try
        {
            now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrEmpty(_accessToken) && _accessTokenExpiresAtUnix > now + 60)
            {
                return true;
            }

            if (string.IsNullOrEmpty(_refreshToken)) return false;

            var payload = new
            {
                RefreshToken = EncryptRsa(_refreshToken)
            };

            var res = await SendRawRequestAsync<HutaoTokenSet>(
                HttpMethod.Post,
                ApiEndpoints.HutaoPassportRefreshTokenPath,
                payload,
                requiresAuth: false);

            if (res.RetCode == 0 && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.AccessToken))
            {
                ApplyTokenSet(_email, res.Data);
                return true;
            }

            if (res.RetCode == 514002 || res.HttpStatus == HttpStatusCode.Unauthorized)
            {
                ClearSession();
            }

            return false;
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<HutaoOperationResult<object>> SendVerifyCodeAsync(string email, bool isResetPassword)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Fail<object>("HutaoSync_ErrEnterEmail".GetLocalized());
        }

        if (_verifyCodeCooldownUntil.HasValue && _verifyCodeCooldownUntil.Value > DateTimeOffset.UtcNow)
        {
            return Fail<object>("HutaoSync_ErrRateLimited".GetLocalized());
        }

        var trimmedEmail = email.Trim();
        var payload = new
        {
            UserName = EncryptRsa(trimmedEmail),
            IsResetPassword = isResetPassword
        };

        var res = await SendRawRequestAsync<object>(
            HttpMethod.Post,
            ApiEndpoints.HutaoPassportVerifyPath,
            payload,
            requiresAuth: false);

        if (res.RetCode == 0)
        {
            _verifyCodeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(60);
            return new HutaoOperationResult<object>
            {
                Ok = true,
                Message = "HutaoSync_MsgVerifySent".GetLocalized()
            };
        }

        return Fail<object>(FormatFriendlyError(res.RetCode, res.Message, res.L10nKey, res.HttpStatus));
    }

    public async Task<HutaoOperationResult<HutaoUserInfo>> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return Fail<HutaoUserInfo>("HutaoSync_ErrEnterAccountPassword".GetLocalized());
        }

        var trimmedEmail = email.Trim();
        var payload = new
        {
            UserName = EncryptRsa(trimmedEmail),
            Password = EncryptRsa(password)
        };

        var res = await SendRawRequestAsync<HutaoTokenSet>(
            HttpMethod.Post,
            ApiEndpoints.HutaoPassportLoginPath,
            payload,
            requiresAuth: false);

        if (res.RetCode != 0 || res.Data == null || string.IsNullOrWhiteSpace(res.Data.AccessToken))
        {
            return Fail<HutaoUserInfo>(FormatFriendlyError(res.RetCode, res.Message, res.L10nKey, res.HttpStatus));
        }

        ApplyTokenSet(trimmedEmail, res.Data);
        var infoRes = await RefreshUserInfoAsync();
        return new HutaoOperationResult<HutaoUserInfo>
        {
            Ok = true,
            Message = "HutaoSync_MsgLoginSuccess".GetLocalized(),
            Data = infoRes.Data ?? _currentUserInfo
        };
    }

    public async Task<HutaoOperationResult<HutaoUserInfo>> RegisterAsync(string email, string password, string verifyCode)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(verifyCode))
        {
            return Fail<HutaoUserInfo>("HutaoSync_ErrEnterCompleteAuth".GetLocalized());
        }

        var trimmedEmail = email.Trim();
        var payload = new
        {
            UserName = EncryptRsa(trimmedEmail),
            Password = EncryptRsa(password),
            VerifyCode = EncryptRsa(verifyCode.Trim())
        };

        var res = await SendRawRequestAsync<HutaoTokenSet>(
            HttpMethod.Post,
            ApiEndpoints.HutaoPassportRegisterPath,
            payload,
            requiresAuth: false);

        if (res.RetCode != 0 || res.Data == null || string.IsNullOrWhiteSpace(res.Data.AccessToken))
        {
            return Fail<HutaoUserInfo>(FormatFriendlyError(res.RetCode, res.Message, res.L10nKey, res.HttpStatus));
        }

        ApplyTokenSet(trimmedEmail, res.Data);
        var infoRes = await RefreshUserInfoAsync();
        return new HutaoOperationResult<HutaoUserInfo>
        {
            Ok = true,
            Message = "HutaoSync_MsgRegisterSuccess".GetLocalized(),
            Data = infoRes.Data ?? _currentUserInfo
        };
    }

    public async Task<HutaoOperationResult<HutaoUserInfo>> ResetPasswordAsync(string email, string newPassword, string verifyCode)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(newPassword) || string.IsNullOrWhiteSpace(verifyCode))
        {
            return Fail<HutaoUserInfo>("HutaoSync_ErrEnterCompleteAuth".GetLocalized());
        }

        var trimmedEmail = email.Trim();
        var payload = new
        {
            UserName = EncryptRsa(trimmedEmail),
            Password = EncryptRsa(newPassword),
            VerifyCode = EncryptRsa(verifyCode.Trim()),
            IsResetPassword = true
        };

        var res = await SendRawRequestAsync<HutaoTokenSet>(
            HttpMethod.Post,
            ApiEndpoints.HutaoPassportResetPasswordPath,
            payload,
            requiresAuth: false);

        if (res.RetCode != 0 || res.Data == null || string.IsNullOrWhiteSpace(res.Data.AccessToken))
        {
            return Fail<HutaoUserInfo>(FormatFriendlyError(res.RetCode, res.Message, res.L10nKey, res.HttpStatus));
        }

        ApplyTokenSet(trimmedEmail, res.Data);
        var infoRes = await RefreshUserInfoAsync();
        return new HutaoOperationResult<HutaoUserInfo>
        {
            Ok = true,
            Message = "HutaoSync_MsgResetSuccess".GetLocalized(),
            Data = infoRes.Data ?? _currentUserInfo
        };
    }

    public async Task<HutaoOperationResult<HutaoUserInfo>> RefreshUserInfoAsync()
    {
        var res = await SendAuthenticatedAsync<HutaoUserInfo>(HttpMethod.Get, ApiEndpoints.HutaoPassportUserInfoPath, null);
        if (!res.Ok)
        {
            return res;
        }

        if (res.Data != null)
        {
            _currentUserInfo = res.Data;
            if (string.IsNullOrWhiteSpace(_email) && !string.IsNullOrWhiteSpace(res.Data.UserName))
            {
                _email = res.Data.UserName;
            }

            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        return res;
    }

    public async Task<HutaoOperationResult<List<HutaoLoggedInDevice>>> GetLoggedInDevicesAsync()
    {
        var res = await SendAuthenticatedAsync<JsonElement>(
            HttpMethod.Get,
            ApiEndpoints.HutaoPassportLoggedInDevicesPath,
            null);

        if (!res.Ok)
        {
            return new HutaoOperationResult<List<HutaoLoggedInDevice>>
            {
                Ok = false,
                Message = res.Message,
                AuthInvalidated = res.AuthInvalidated
            };
        }

        var devices = ParseLoggedInDevices(res.Data)
            .Where(d => !string.IsNullOrWhiteSpace(d.EffectiveDeviceId) || !string.IsNullOrWhiteSpace(d.EffectiveDeviceName))
            .OrderByDescending(d => string.Equals(d.EffectiveDeviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new HutaoOperationResult<List<HutaoLoggedInDevice>>
        {
            Ok = true,
            Data = devices
        };
    }

    public async Task<HutaoOperationResult<object>> RevokeDeviceAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Fail<object>("HutaoSync_ErrDeviceInvalid".GetLocalized());
        }

        var trimmed = deviceId.Trim();
        var res = await SendAuthenticatedAsync<object>(
            HttpMethod.Post,
            ApiEndpoints.HutaoPassportRevokeTokenPath,
            new
            {
                DeviceId = trimmed
            });

        if (!res.Ok && !res.AuthInvalidated && trimmed.Length <= 190)
        {
            res = await SendAuthenticatedAsync<object>(
                HttpMethod.Post,
                ApiEndpoints.HutaoPassportRevokeTokenPath,
                new
                {
                    DeviceId = EncryptRsa(trimmed)
                });
        }

        if (res.Ok && string.Equals(trimmed, _deviceId, StringComparison.OrdinalIgnoreCase))
        {
            ClearSession();
        }

        return res;
    }

    public async Task<HutaoOperationResult<object>> RevokeAllDevicesAsync()
    {
        var res = await SendAuthenticatedAsync<object>(
            HttpMethod.Post,
            ApiEndpoints.HutaoPassportRevokeAllTokensPath,
            new
            {
            });

        if (res.Ok)
        {
            ClearSession();
        }

        return res;
    }

    public async Task<HutaoOperationResult<string>> RedeemCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Fail<string>("HutaoSync_ErrRedeemEmpty".GetLocalized());
        }

        var trimmed = code.Trim();
        var res = await SendAuthenticatedAsync<JsonElement>(
            HttpMethod.Post,
            ApiEndpoints.HutaoRedeemUsePath,
            new
            {
                code = trimmed
            });

        if (!res.Ok)
        {
            return new HutaoOperationResult<string>
            {
                Ok = false,
                Message = res.Message,
                AuthInvalidated = res.AuthInvalidated
            };
        }

        await RefreshUserInfoAsync();
        var detail = ExtractRedeemDetail(res.Data);
        var msg = string.IsNullOrWhiteSpace(detail)
            ? "HutaoSync_MsgRedeemSuccess".GetLocalized()
            : string.Format("HutaoSync_MsgRedeemDetailFormat".GetLocalized(), detail);

        return new HutaoOperationResult<string>
        {
            Ok = true,
            Message = msg,
            Data = msg
        };
    }

    private static List<HutaoLoggedInDevice> ParseLoggedInDevices(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<HutaoLoggedInDevice>>(element.GetRawText(), JsonOptions)
                ?? new List<HutaoLoggedInDevice>();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array &&
                    (prop.Name.Equals("Devices", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("Items", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("List", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("Data", StringComparison.OrdinalIgnoreCase)))
                {
                    return JsonSerializer.Deserialize<List<HutaoLoggedInDevice>>(prop.Value.GetRawText(), JsonOptions)
                        ?? new List<HutaoLoggedInDevice>();
                }
            }
        }

        return new List<HutaoLoggedInDevice>();
    }

    private static string ExtractRedeemDetail(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?.Trim() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.ToString();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if ((prop.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("Days", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("Duration", StringComparison.OrdinalIgnoreCase)) &&
                    prop.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                {
                    var val = prop.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
        }

        return string.Empty;
    }

    public async Task LogoutAsync(bool revokeRemote = true)
    {
        if (revokeRemote && IsLoggedIn)
        {
            try
            {
                var raw = await SendRawRequestAsync<object>(
                    HttpMethod.Post,
                    ApiEndpoints.HutaoPassportRevokeTokenPath,
                    new
                    {
                        DeviceId = _deviceId
                    },
                    requiresAuth: true);

                if (raw.RetCode != 0 && !string.IsNullOrWhiteSpace(_deviceId) && _deviceId.Length <= 190)
                {
                    await SendRawRequestAsync<object>(
                        HttpMethod.Post,
                        ApiEndpoints.HutaoPassportRevokeTokenPath,
                        new
                        {
                            DeviceId = EncryptRsa(_deviceId)
                        },
                        requiresAuth: true);
                }
            }
            catch
            {
            }
        }

        ClearSession();
    }

    public async Task<HutaoOperationResult<List<HutaoGachaEntry>>> GetGachaEntriesAsync()
    {
        var res = await SendAuthenticatedAsync<List<HutaoGachaEntry>>(
            HttpMethod.Get,
            ApiEndpoints.HutaoGachaEntriesPath,
            null);

        if (!res.Ok)
        {
            return res;
        }

        return new HutaoOperationResult<List<HutaoGachaEntry>>
        {
            Ok = true,
            Data = res.Data ?? new List<HutaoGachaEntry>()
        };
    }

    public async Task<HutaoOperationResult<int>> DeleteGachaUidAsync(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Fail<int>("HutaoSync_ErrSelectUidFirst".GetLocalized());
        }

        var path = $"{ApiEndpoints.HutaoGachaDeletePath}?Uid={Uri.EscapeDataString(uid.Trim())}";
        return await SendAuthenticatedAsync<int>(HttpMethod.Get, path, null);
    }

    public async Task<HutaoOperationResult<HutaoBridgeTransferSummary>> PullGachaToFufuCloudAsync(
        IReadOnlyList<string> uids,
        string targetSlot,
        bool mergeExistingFufuArchive,
        bool overwriteExisting = true,
        CancellationToken cancellationToken = default)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<HutaoBridgeTransferSummary>();
        if (bridgeCheck != null) return bridgeCheck;

        var cleanUids = (uids ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (cleanUids.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrSelectUidFirst".GetLocalized());
        }

        if (!_cloudSync.IsValidSlotName(targetSlot))
        {
            return Fail<HutaoBridgeTransferSummary>("CloudSync_ErrInvalidSlot".GetLocalized());
        }

        var normalizedSlot = targetSlot.Trim();

        var gachaPlanCheck = await EnsureGachaServiceAvailableAsync<HutaoBridgeTransferSummary>();
        if (gachaPlanCheck != null) return gachaPlanCheck;

        var catalog = await ResolveItemCatalogAsync(cancellationToken);

        var uidEntries = new Dictionary<string, UIGFEntry>(StringComparer.Ordinal);
        var downloadedCount = 0;

        foreach (var uid in cleanUids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endIdsRes = await SendAuthenticatedAsync<Dictionary<string, long>>(
                HttpMethod.Get,
                $"{ApiEndpoints.HutaoGachaEndIdsPath}?Uid={Uri.EscapeDataString(uid)}",
                null,
                cancellationToken);

            if (!endIdsRes.Ok)
            {
                return new HutaoOperationResult<HutaoBridgeTransferSummary>
                {
                    Ok = false,
                    Message = endIdsRes.Message,
                    AuthInvalidated = endIdsRes.AuthInvalidated
                };
            }

            var activeQueryTypes = QueryTypes.AsEnumerable();
            if (endIdsRes.Data is { Count: > 0 })
            {
                activeQueryTypes = endIdsRes.Data
                    .Where(kv => kv.Value > 0 && int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qt) && QueryTypes.Contains(qt))
                    .Select(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
                    .Distinct()
                    .ToList();
            }

            var tz = ResolveDefaultTimezone(uid);
            var itemMap = new Dictionary<string, UIGFItem>(StringComparer.Ordinal);

            foreach (var queryType in activeQueryTypes)
            {
                long? cursor = null;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var path = cursor.HasValue
                        ? $"{ApiEndpoints.HutaoGachaLimitedRetrievePath}?uid={Uri.EscapeDataString(uid)}&configType={queryType}&endId={cursor.Value}&count=200"
                        : $"{ApiEndpoints.HutaoGachaLimitedRetrievePath}?uid={Uri.EscapeDataString(uid)}&configType={queryType}&count=200";
                    var pageRes = await SendAuthenticatedAsync<List<HutaoGachaItem>>(HttpMethod.Get, path, null, cancellationToken);
                    if (!pageRes.Ok)
                    {
                        return new HutaoOperationResult<HutaoBridgeTransferSummary>
                        {
                            Ok = false,
                            Message = pageRes.Message,
                            AuthInvalidated = pageRes.AuthInvalidated
                        };
                    }

                    var batch = pageRes.Data ?? new List<HutaoGachaItem>();
                    if (batch.Count == 0) break;

                    var minId = long.MaxValue;
                    foreach (var gachaItem in batch)
                    {
                        if (gachaItem.Id <= 0) continue;
                        if (gachaItem.Id < minId) minId = gachaItem.Id;

                        var converted = ConvertHutaoItemToUigf(gachaItem, tz, catalog);
                        if (converted == null) continue;

                        itemMap[converted.Id] = converted;
                    }

                    if (batch.Count < 200 || minId == long.MaxValue || (cursor.HasValue && minId >= cursor.Value))
                    {
                        break;
                    }

                    cursor = minId;
                }
            }

            if (itemMap.Count > 0)
            {
                downloadedCount += itemMap.Count;
                uidEntries[uid] = new UIGFEntry
                {
                    Uid = uid,
                    Timezone = tz,
                    Lang = "zh-cn",
                    List = itemMap.Values
                        .OrderByDescending(x => long.TryParse(x.Id, out var idVal) ? idVal : 0L)
                        .ToList()
                };
            }
        }

        if (uidEntries.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrNoHutaoGachaData".GetLocalized());
        }

        if (mergeExistingFufuArchive)
        {
            var listRes = await _cloudSync.ListArchivesAsync("uigf");
            if (listRes.Ok && listRes.Data?.Archives.Any(a =>
                    string.Equals(a.DataType, "uigf", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Slot, normalizedSlot, StringComparison.OrdinalIgnoreCase)) == true)
            {
                var existingPull = await _cloudSync.PullArchiveAsync("uigf", normalizedSlot);
                if (existingPull.Ok && !string.IsNullOrWhiteSpace(existingPull.JsonContent))
                {
                    MergeExistingUigfIntoEntries(existingPull.JsonContent, uidEntries, catalog);
                }
            }
        }

        var strictJson = BuildStrictUigfV4Json(uidEntries);
        if (string.IsNullOrWhiteSpace(strictJson))
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrNoHutaoGachaData".GetLocalized());
        }

        var uploadRes = await _cloudSync.UploadArchiveAsync(
            "uigf",
            normalizedSlot,
            strictJson,
            overwrite: overwriteExisting || mergeExistingFufuArchive);
        if (!uploadRes.Ok)
        {
            return Fail<HutaoBridgeTransferSummary>(
                _cloudSync.FormatFriendlyError(uploadRes.Code, uploadRes.Message, uploadRes.HttpStatusCode));
        }

        var totalRecords = uidEntries.Values.Sum(e => e.List.Count);
        return new HutaoOperationResult<HutaoBridgeTransferSummary>
        {
            Ok = true,
            Message = string.Format(
                "HutaoSync_MsgPullGachaSuccess".GetLocalized(),
                cleanUids.Count,
                downloadedCount,
                normalizedSlot,
                totalRecords),
            Data = new HutaoBridgeTransferSummary
            {
                TargetSlot = normalizedSlot,
                UidCount = uidEntries.Count,
                TransferredRecords = downloadedCount,
                TotalMergedRecords = totalRecords
            }
        };
    }

    public async Task<HutaoOperationResult<List<FufuCloudUigfUidItem>>> InspectFufuCloudUigfAsync(string slot)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<List<FufuCloudUigfUidItem>>();
        if (bridgeCheck != null) return bridgeCheck;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return Fail<List<FufuCloudUigfUidItem>>("HutaoSync_ErrSelectFufuArchive".GetLocalized());
        }

        var pullRes = await _cloudSync.PullArchiveAsync("uigf", slot.Trim());
        if (!pullRes.Ok)
        {
            return Fail<List<FufuCloudUigfUidItem>>(
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
        }

        var entries = ParseUigfEntries(pullRes.JsonContent);
        if (entries.Count == 0)
        {
            return Fail<List<FufuCloudUigfUidItem>>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
        }

        var items = entries.Values
            .OrderBy(e => e.Uid, StringComparer.Ordinal)
            .Select(e => new FufuCloudUigfUidItem
            {
                Uid = e.Uid,
                RecordCount = e.List.Count,
                IsSelected = true
            })
            .ToList();

        return new HutaoOperationResult<List<FufuCloudUigfUidItem>>
        {
            Ok = true,
            Data = items
        };
    }

    public async Task<HutaoOperationResult<HutaoBridgeTransferSummary>> PushFufuCloudGachaToHutaoAsync(
        string slot,
        IReadOnlyList<string> selectedUids,
        CancellationToken cancellationToken = default)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<HutaoBridgeTransferSummary>();
        if (bridgeCheck != null) return bridgeCheck;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrSelectFufuArchive".GetLocalized());
        }

        var uidSet = (selectedUids ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (uidSet.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrSelectUidFirst".GetLocalized());
        }

        var gachaPlanCheck = await EnsureGachaServiceAvailableAsync<HutaoBridgeTransferSummary>();
        if (gachaPlanCheck != null) return gachaPlanCheck;

        var pullRes = await _cloudSync.PullArchiveAsync("uigf", slot.Trim());
        if (!pullRes.Ok)
        {
            return Fail<HutaoBridgeTransferSummary>(
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
        }

        var entries = ParseUigfEntries(pullRes.JsonContent);
        var targetEntries = entries.Values
            .Where(e => uidSet.Contains(e.Uid) && e.List.Count > 0)
            .ToList();

        if (targetEntries.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
        }

        var catalog = await ResolveItemCatalogAsync(cancellationToken);
        var totalUploaded = 0;
        var alreadySyncedCount = 0;
        var skippedInvalidCount = 0;

        foreach (var entry in targetEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endIdsRes = await SendAuthenticatedAsync<Dictionary<string, long>>(
                HttpMethod.Get,
                $"{ApiEndpoints.HutaoGachaEndIdsPath}?Uid={Uri.EscapeDataString(entry.Uid)}",
                null,
                cancellationToken);

            if (!endIdsRes.Ok)
            {
                DebugLog.Info("HutaoCloudService", $"[Hutao] PushGacha EndIds failed for UID {entry.Uid}: {endIdsRes.Message}");
                return new HutaoOperationResult<HutaoBridgeTransferSummary>
                {
                    Ok = false,
                    Message = endIdsRes.Message,
                    AuthInvalidated = endIdsRes.AuthInvalidated
                };
            }

            var endIds = endIdsRes.Data ?? new Dictionary<string, long>(StringComparer.Ordinal);
            var tz = entry.Timezone is >= -12 and <= 14 ? entry.Timezone : ResolveDefaultTimezone(entry.Uid);
            var seenIds = new HashSet<long>();
            var toUpload = new List<HutaoGachaItem>();

            foreach (var rec in entry.List)
            {
                if (!long.TryParse((rec.Id ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                {
                    skippedInvalidCount++;
                    continue;
                }

                if (!seenIds.Add(id))
                {
                    skippedInvalidCount++;
                    continue;
                }

                var gachaType = ParseGachaTypeInt(rec.GachaType, rec.UigfGachaType);
                if (gachaType == 0)
                {
                    skippedInvalidCount++;
                    continue;
                }

                var queryType = gachaType == 400 ? 301 : ParseGachaTypeInt(rec.UigfGachaType, gachaType.ToString(CultureInfo.InvariantCulture));
                if (queryType is not (100 or 200 or 301 or 302 or 500))
                {
                    queryType = gachaType == 400 ? 301 : 200;
                }

                var endId = endIds.GetValueOrDefault(queryType.ToString(CultureInfo.InvariantCulture), 0L);
                if (id <= endId)
                {
                    alreadySyncedCount++;
                    continue;
                }

                var itemId = ResolveItemId(rec, catalog);
                if (itemId <= 0)
                {
                    skippedInvalidCount++;
                    continue;
                }

                var formattedTime = FormatHutaoIsoTime(rec.Time, tz);
                if (formattedTime == null)
                {
                    skippedInvalidCount++;
                    continue;
                }

                toUpload.Add(new HutaoGachaItem
                {
                    GachaType = gachaType,
                    QueryType = queryType,
                    ItemId = itemId,
                    Time = formattedTime,
                    Id = id
                });
            }

            if (toUpload.Count == 0) continue;

            toUpload.Sort((a, b) => a.Id.CompareTo(b.Id));

            const int chunkSize = 5000;
            for (var i = 0; i < toUpload.Count; i += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = toUpload.Skip(i).Take(chunkSize).ToList();
                var uploadPayload = new
                {
                    Uid = entry.Uid,
                    Items = batch
                };

                var uploadRes = await SendAuthenticatedAsync<JsonElement?>(
                    HttpMethod.Post,
                    ApiEndpoints.HutaoGachaUploadPath,
                    uploadPayload,
                    cancellationToken);

                if (!uploadRes.Ok)
                {
                    DebugLog.Info("HutaoCloudService", $"[Hutao] PushGacha Upload failed for UID {entry.Uid} (batchCount={batch.Count}): {uploadRes.Message}");
                    return new HutaoOperationResult<HutaoBridgeTransferSummary>
                    {
                        Ok = false,
                        Message = uploadRes.Message,
                        AuthInvalidated = uploadRes.AuthInvalidated
                    };
                }

                totalUploaded += batch.Count;
            }
        }

        var totalSkipped = alreadySyncedCount + skippedInvalidCount;
        if (totalUploaded == 0)
        {
            if (alreadySyncedCount == 0 && skippedInvalidCount > 0)
            {
                return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
            }

            return new HutaoOperationResult<HutaoBridgeTransferSummary>
            {
                Ok = true,
                Message = "HutaoSync_MsgPushGachaUpToDate".GetLocalized(),
                Data = new HutaoBridgeTransferSummary
                {
                    TargetSlot = slot.Trim(),
                    UidCount = targetEntries.Count,
                    TransferredRecords = 0,
                    SkippedRecords = totalSkipped
                }
            };
        }

        return new HutaoOperationResult<HutaoBridgeTransferSummary>
        {
            Ok = true,
            Message = string.Format(
                "HutaoSync_MsgPushGachaSuccess".GetLocalized(),
                targetEntries.Count,
                totalUploaded),
            Data = new HutaoBridgeTransferSummary
            {
                TargetSlot = slot.Trim(),
                UidCount = targetEntries.Count,
                TransferredRecords = totalUploaded,
                SkippedRecords = totalSkipped
            }
        };
    }

    public async Task<HutaoOperationResult<List<FufuCloudChallengeItem>>> InspectFufuCloudChallengeAsync(string slot)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<List<FufuCloudChallengeItem>>();
        if (bridgeCheck != null) return bridgeCheck;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return Fail<List<FufuCloudChallengeItem>>("HutaoSync_ErrSelectFufuArchive".GetLocalized());
        }

        var pullRes = await _cloudSync.PullArchiveAsync("challenge", slot.Trim());
        if (!pullRes.Ok)
        {
            return Fail<List<FufuCloudChallengeItem>>(
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
        }

        CycleChallengeCloudDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<CycleChallengeCloudDocument>(pullRes.JsonContent, JsonOptions);
        }
        catch
        {
            return Fail<List<FufuCloudChallengeItem>>("CloudSync_ErrInvalidFormat".GetLocalized());
        }

        if (doc?.Records == null || doc.Records.Count == 0)
        {
            return Fail<List<FufuCloudChallengeItem>>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
        }

        var now = DateTimeOffset.UtcNow;
        var items = new List<FufuCloudChallengeItem>();

        foreach (var rec in doc.Records)
        {
            if (string.IsNullOrWhiteSpace(rec.Uid) || string.IsNullOrWhiteSpace(rec.Payload)) continue;
            if (rec.Mode != 0 && rec.Mode != 1) continue;

            if (rec.Mode == 0)
            {
                SpiralAbyssDto? abyss;
                try
                {
                    abyss = SpiralAbyssPayloadParser.Parse(rec.Payload);
                }
                catch
                {
                    continue;
                }

                if (abyss == null || !abyss.IsUnlock) continue;

                var canUpload = TryBuildSpiralAbyssData(abyss, rec.ScheduleId, out _);
                var isCurrent = IsPeriodCurrent(rec.StartUtc, rec.EndUtc, abyss.StartTime, abyss.EndTime, now);
                var period = BuildPeriodText(rec.StartUtc, rec.EndUtc, abyss.StartTime, abyss.EndTime, rec.ScheduleId);
                var floorText = string.IsNullOrWhiteSpace(abyss.MaxFloor) ? "--" : abyss.MaxFloor;
                var summary = string.Format("HutaoSync_AbyssSummaryFormat".GetLocalized(), floorText, abyss.TotalStar);

                items.Add(new FufuCloudChallengeItem
                {
                    Uid = rec.Uid.Trim(),
                    Mode = 0,
                    ScheduleId = !string.IsNullOrWhiteSpace(abyss.ScheduleId) ? abyss.ScheduleId! : rec.ScheduleId,
                    IsLikelyCurrent = isCurrent,
                    CanUpload = canUpload,
                    ModeLabel = "HutaoSync_ModeAbyss".GetLocalized(),
                    PeriodText = period,
                    SummaryText = summary,
                    StatusBadgeText = !canUpload
                        ? "HutaoSync_ChallengeStatusIncomplete".GetLocalized()
                        : isCurrent
                            ? "HutaoSync_ChallengeStatusCurrent".GetLocalized()
                            : "HutaoSync_ChallengeStatusPast".GetLocalized(),
                    IsSelected = canUpload && isCurrent,
                    Payload = rec.Payload
                });
            }
            else
            {
                RoleCombatEntryDto? entry;
                try
                {
                    var envelope = CycleChallengePayloadReader.Read(rec.Payload);
                    entry = envelope.Entry is JsonElement el
                        ? el.Deserialize<RoleCombatEntryDto>(CycleChallengePayloadReader.SerializerOptions)
                        : null;
                }
                catch
                {
                    continue;
                }

                if (entry == null) continue;

                var canUpload = TryBuildRoleCombatRequest(rec.Uid.Trim(), entry, rec.ScheduleId, out var req);
                var isCurrent = IsPeriodCurrent(
                    rec.StartUtc,
                    rec.EndUtc,
                    entry.Schedule?.StartTime ?? entry.StartTime,
                    entry.Schedule?.EndTime ?? entry.EndTime,
                    now);
                var period = BuildPeriodText(
                    rec.StartUtc,
                    rec.EndUtc,
                    entry.Schedule?.StartTime ?? entry.StartTime,
                    entry.Schedule?.EndTime ?? entry.EndTime,
                    rec.ScheduleId);
                var maxRound = entry.Stat?.MaxRoundId ?? entry.MaxRoundId ?? 0;
                var medalNum = entry.Stat?.MedalNum ?? entry.MedalNum ?? 0;
                var avatarCount = req?.BackupAvatars.Count ?? 0;
                var summary = string.Format("HutaoSync_TheaterSummaryFormat".GetLocalized(), maxRound, medalNum, avatarCount);

                items.Add(new FufuCloudChallengeItem
                {
                    Uid = rec.Uid.Trim(),
                    Mode = 1,
                    ScheduleId = !string.IsNullOrWhiteSpace(entry.Schedule?.ScheduleId)
                        ? entry.Schedule!.ScheduleId!
                        : (!string.IsNullOrWhiteSpace(entry.ScheduleId) ? entry.ScheduleId! : rec.ScheduleId),
                    IsLikelyCurrent = isCurrent,
                    CanUpload = canUpload,
                    ModeLabel = "HutaoSync_ModeTheater".GetLocalized(),
                    PeriodText = period,
                    SummaryText = summary,
                    StatusBadgeText = !canUpload
                        ? "HutaoSync_ChallengeStatusIncomplete".GetLocalized()
                        : isCurrent
                            ? "HutaoSync_ChallengeStatusCurrent".GetLocalized()
                            : "HutaoSync_ChallengeStatusPast".GetLocalized(),
                    IsSelected = canUpload && isCurrent,
                    Payload = rec.Payload
                });
            }
        }

        if (items.Count == 0)
        {
            return Fail<List<FufuCloudChallengeItem>>("HutaoSync_ErrNoSupportedChallengeRecords".GetLocalized());
        }

        var ordered = items
            .OrderByDescending(i => i.IsLikelyCurrent)
            .ThenByDescending(i => long.TryParse(i.ScheduleId, out var sid) ? sid : 0L)
            .ThenBy(i => i.Mode)
            .ThenBy(i => i.Uid, StringComparer.Ordinal)
            .ToList();

        return new HutaoOperationResult<List<FufuCloudChallengeItem>>
        {
            Ok = true,
            Data = ordered
        };
    }

    public async Task<HutaoOperationResult<HutaoChallengeUploadSummary>> PushFufuCloudChallengeToHutaoAsync(
        string slot,
        IReadOnlyList<FufuCloudChallengeItem> selectedItems,
        CancellationToken cancellationToken = default)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<HutaoChallengeUploadSummary>();
        if (bridgeCheck != null) return bridgeCheck;

        if (!IsLoggedIn || !await EnsureAuthenticatedAsync())
        {
            return new HutaoOperationResult<HutaoChallengeUploadSummary>
            {
                Ok = false,
                Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                AuthInvalidated = true
            };
        }

        var targets = (selectedItems ?? Array.Empty<FufuCloudChallengeItem>())
            .Where(x => x.IsSelected)
            .ToList();

        if (targets.Count == 0)
        {
            return Fail<HutaoChallengeUploadSummary>("HutaoSync_ErrSelectChallengeFirst".GetLocalized());
        }

        if (_currentUserInfo == null)
        {
            await RefreshUserInfoAsync();
        }

        var reservedUserName = !string.IsNullOrWhiteSpace(_currentUserInfo?.NormalizedUserName)
            ? _currentUserInfo!.NormalizedUserName!
            : (!string.IsNullOrWhiteSpace(_currentUserInfo?.UserName) ? _currentUserInfo!.UserName! : _email);

        var uploadedCount = 0;
        var extendedCount = 0;
        var skippedPastCount = 0;
        var skippedIncompleteCount = 0;
        var failedCount = 0;
        var failureDetails = new List<string>();
        var avatarCache = new Dictionary<string, (bool Ok, string ErrorMessage, List<HutaoSimpleAvatar> Avatars)>(StringComparer.Ordinal);

        foreach (var item in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.CanUpload)
            {
                skippedIncompleteCount++;
                continue;
            }

            if (item.Mode == 0)
            {
                var abyss = SpiralAbyssPayloadParser.Parse(item.Payload);
                if (abyss == null || !TryBuildSpiralAbyssData(abyss, item.ScheduleId, out var abyssData) || abyssData == null)
                {
                    skippedIncompleteCount++;
                    continue;
                }

                if (!avatarCache.TryGetValue(item.Uid, out var avatarResult))
                {
                    avatarResult = await _gameRecordApiClient.GetCharactersForUidAsync(item.Uid, cancellationToken);
                    avatarCache[item.Uid] = avatarResult;
                }

                if (!avatarResult.Ok || !HasRequiredHutaoAvatars(avatarResult.Avatars))
                {
                    failedCount++;
                    var avatarReason = !avatarResult.Ok && !string.IsNullOrWhiteSpace(avatarResult.ErrorMessage)
                        ? string.Format("HutaoSync_ErrChallengeFetchAvatarsFormat".GetLocalized(), item.Uid, avatarResult.ErrorMessage)
                        : string.Format("HutaoSync_ErrChallengeMissingAvatarsFormat".GetLocalized(), item.Uid);
                    if (!failureDetails.Contains(avatarReason, StringComparer.Ordinal))
                    {
                        failureDetails.Add(avatarReason);
                    }
                    continue;
                }

                var req = new HutaoRecordUploadRequest
                {
                    Uid = item.Uid,
                    Identity = "FufuLauncher",
                    SpiralAbyss = abyssData,
                    Avatars = avatarResult.Avatars,
                    ReservedUserName = reservedUserName
                };

                var rawRes = await SendRawRequestWithRetryAsync<JsonElement?>(
                    HttpMethod.Post,
                    $"{ApiEndpoints.HutaoRecordUploadPath}?returningRank=false",
                    req,
                    cancellationToken);

                if (rawRes.RetCode == 0)
                {
                    uploadedCount++;
                    var dataText = rawRes.Data?.ValueKind == JsonValueKind.String
                        ? rawRes.Data.Value.GetString() ?? string.Empty
                        : rawRes.Data?.ToString() ?? string.Empty;
                    if (ContainsExtendedRewardHint(rawRes.Message, rawRes.L10nKey, dataText))
                    {
                        extendedCount++;
                    }
                }
                else if (IsNotCurrentPeriodError(rawRes.Message, rawRes.L10nKey))
                {
                    skippedPastCount++;
                }
                else if (rawRes.RetCode == 514002 || rawRes.HttpStatus == HttpStatusCode.Unauthorized)
                {
                    ClearSession();
                    return new HutaoOperationResult<HutaoChallengeUploadSummary>
                    {
                        Ok = false,
                        Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                        AuthInvalidated = true
                    };
                }
                else
                {
                    failedCount++;
                    var reason = ExtractChallengeFailureReason(rawRes.RetCode, rawRes.Message, rawRes.L10nKey, rawRes.HttpStatus);
                    if (!string.IsNullOrWhiteSpace(reason) && !failureDetails.Contains(reason, StringComparer.Ordinal))
                    {
                        failureDetails.Add(reason);
                    }
                }
            }
            else if (item.Mode == 1)
            {
                var envelope = CycleChallengePayloadReader.Read(item.Payload);
                var entry = envelope.Entry is JsonElement el
                    ? el.Deserialize<RoleCombatEntryDto>(CycleChallengePayloadReader.SerializerOptions)
                    : null;

                if (entry == null || !TryBuildRoleCombatRequest(item.Uid, entry, item.ScheduleId, out var combatReq) || combatReq == null)
                {
                    skippedIncompleteCount++;
                    continue;
                }

                var rawRes = await SendRawRequestWithRetryAsync<JsonElement?>(
                    HttpMethod.Post,
                    ApiEndpoints.HutaoRoleCombatUploadPath,
                    combatReq,
                    cancellationToken);

                if (rawRes.RetCode == 0)
                {
                    uploadedCount++;
                }
                else if (IsNotCurrentPeriodError(rawRes.Message, rawRes.L10nKey))
                {
                    skippedPastCount++;
                }
                else if (rawRes.RetCode == 514002 || rawRes.HttpStatus == HttpStatusCode.Unauthorized)
                {
                    ClearSession();
                    return new HutaoOperationResult<HutaoChallengeUploadSummary>
                    {
                        Ok = false,
                        Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                        AuthInvalidated = true
                    };
                }
                else
                {
                    failedCount++;
                    var reason = ExtractChallengeFailureReason(rawRes.RetCode, rawRes.Message, rawRes.L10nKey, rawRes.HttpStatus);
                    if (!string.IsNullOrWhiteSpace(reason) && !failureDetails.Contains(reason, StringComparer.Ordinal))
                    {
                        failureDetails.Add(reason);
                    }
                }
            }
        }

        if (extendedCount > 0)
        {
            await RefreshUserInfoAsync();
        }

        var parts = new List<string>();
        if (uploadedCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeUploadedPart".GetLocalized(), uploadedCount));
        }
        if (extendedCount > 0)
        {
            parts.Add("HutaoSync_ChallengeExtendedPart".GetLocalized());
        }
        if (skippedPastCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeSkippedPastPart".GetLocalized(), skippedPastCount));
        }
        if (skippedIncompleteCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeSkippedIncompletePart".GetLocalized(), skippedIncompleteCount));
        }
        if (failedCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeFailedPart".GetLocalized(), failedCount));
        }

        var summaryText = parts.Count > 0
            ? string.Join("；", parts)
            : "HutaoSync_MsgChallengeNothingUploaded".GetLocalized();

        if (failedCount > 0 && failureDetails.Count > 0)
        {
            summaryText = $"{summaryText}（{string.Join("；", failureDetails.Take(2))}）";
        }

        var overallOk = uploadedCount > 0 || (failedCount == 0 && skippedPastCount > 0);
        return new HutaoOperationResult<HutaoChallengeUploadSummary>
        {
            Ok = overallOk,
            Message = summaryText,
            Data = new HutaoChallengeUploadSummary
            {
                UploadedCount = uploadedCount,
                GachaExtendedCount = extendedCount,
                SkippedPastCount = skippedPastCount,
                SkippedIncompleteCount = skippedIncompleteCount,
                FailedCount = failedCount,
                DetailMessage = summaryText
            }
        };
    }

    private async Task<HutaoOperationResult<T>?> EnsureFufuCloudReadyAsync<T>()
    {
        if (!_cloudSync.IsLoggedIn || !await _cloudSync.EnsureAuthenticatedAsync())
        {
            return Fail<T>("HutaoSync_ErrFufuLoginRequired".GetLocalized());
        }

        if (_cloudSync.IsLocked)
        {
            return Fail<T>("CloudSync_ErrLocked".GetLocalized());
        }

        return null;
    }

    private async Task<HutaoOperationResult<T>?> EnsureGachaServiceAvailableAsync<T>()
    {
        if (_currentUserInfo == null)
        {
            var infoRes = await RefreshUserInfoAsync();
            if (!infoRes.Ok)
            {
                return new HutaoOperationResult<T>
                {
                    Ok = false,
                    Message = infoRes.Message,
                    AuthInvalidated = infoRes.AuthInvalidated
                };
            }
        }

        if (_currentUserInfo == null || !_currentUserInfo.HasValidGachaPlan)
        {
            return Fail<T>("HutaoSync_ErrNoGachaService".GetLocalized());
        }

        return null;
    }

    private async Task<(Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName)>
        ResolveItemCatalogAsync(CancellationToken cancellationToken)
    {
        var entities = _metadataRepo.GetAllMetadata();
        if (!_calculatorCatalogLoaded)
        {
            var fetched = new List<MetadataEntity>();
            await TryFetchCalculatorCatalogAsync(
                ApiEndpoints.CalculateAvatarListUrl,
                new { page = 1, size = 1000, is_all = true },
                isAvatar: true,
                fetched,
                cancellationToken);
            await TryFetchCalculatorCatalogAsync(
                ApiEndpoints.CalculateWeaponListUrl,
                new { page = 1, size = 1000, weapon_levels = new[] { 1, 2, 3, 4, 5 } },
                isAvatar: false,
                fetched,
                cancellationToken);

            if (fetched.Count > 0)
            {
                try
                {
                    _metadataRepo.UpsertMetadata(fetched);
                    entities = _metadataRepo.GetAllMetadata();
                }
                catch
                {
                    entities = entities.Concat(fetched).ToList();
                }
            }

            _calculatorCatalogLoaded = true;
        }

        var byId = new Dictionary<int, (string Name, string ItemType, string RankType)>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            if (int.TryParse(entity.ItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                var itemType = string.Equals(entity.Type, "char", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(entity.Type, "角色", StringComparison.Ordinal) ||
                               id >= 10_000_000
                    ? "角色"
                    : "武器";
                var name = entity.Name ?? string.Empty;
                var rank = entity.Rank ?? string.Empty;

                if (!byId.ContainsKey(id) || (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(byId[id].Name)))
                {
                    byId[id] = (name, itemType, rank);
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    byName[name.Trim()] = id;
                }
            }
        }

        return (byId, byName);
    }

    private async Task TryFetchCalculatorCatalogAsync(
        string url,
        object payload,
        bool isAvatar,
        List<MetadataEntity> output,
        CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using var resp = await _catalogHttpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("list", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var elem in list.EnumerateArray())
            {
                var name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || name == "旅行者") continue;

                var itemId = elem.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal)
                    ? idVal.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                if (string.IsNullOrEmpty(itemId)) continue;

                var icon = elem.TryGetProperty("icon", out var iconProp) ? iconProp.GetString() ?? string.Empty : string.Empty;
                var rank = string.Empty;
                if (isAvatar && elem.TryGetProperty("avatar_level", out var avLv) && avLv.TryGetInt32(out var avRank))
                {
                    rank = avRank.ToString(CultureInfo.InvariantCulture);
                }
                else if (!isAvatar && elem.TryGetProperty("weapon_level", out var wpLv) && wpLv.TryGetInt32(out var wpRank))
                {
                    rank = wpRank.ToString(CultureInfo.InvariantCulture);
                }

                var elementSrc = string.Empty;
                if (isAvatar && elem.TryGetProperty("element_attr_id", out var elemProp) && elemProp.TryGetInt32(out var elemId))
                {
                    elementSrc = ElementMapping.GetElementIconUrl(elemId) ?? string.Empty;
                }

                output.Add(new MetadataEntity
                {
                    Name = name.Trim(),
                    ImgSrc = icon,
                    ElementSrc = elementSrc,
                    Type = isAvatar ? "char" : "weapon",
                    Rank = rank,
                    ItemId = itemId
                });
            }
        }
        catch
        {
        }
    }

    private async Task<HutaoOperationResult<T>> SendAuthenticatedAsync<T>(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new HutaoOperationResult<T>
            {
                Ok = false,
                Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                AuthInvalidated = true
            };
        }

        var raw = await SendRawRequestWithRetryAsync<T>(method, path, payload, cancellationToken);
        if (raw.RetCode == 0)
        {
            return new HutaoOperationResult<T>
            {
                Ok = true,
                Message = raw.Message ?? string.Empty,
                Data = raw.Data
            };
        }

        var authInvalid = raw.RetCode == 514002 || raw.HttpStatus == HttpStatusCode.Unauthorized;
        if (authInvalid)
        {
            ClearSession();
        }

        return new HutaoOperationResult<T>
        {
            Ok = false,
            Message = FormatFriendlyError(raw.RetCode, raw.Message, raw.L10nKey, raw.HttpStatus),
            AuthInvalidated = authInvalid
        };
    }

    private async Task<(int RetCode, string? Message, string? L10nKey, T? Data, HttpStatusCode HttpStatus)>
        SendRawRequestWithRetryAsync<T>(
            HttpMethod method,
            string path,
            object? payload,
            CancellationToken cancellationToken)
    {
        var raw = await SendRawRequestAsync<T>(method, path, payload, requiresAuth: true, cancellationToken);
        if (raw.RetCode == 514002 || raw.HttpStatus == HttpStatusCode.Unauthorized)
        {
            _accessTokenExpiresAtUnix = 0;
            if (await EnsureAuthenticatedAsync())
            {
                raw = await SendRawRequestAsync<T>(method, path, payload, requiresAuth: true, cancellationToken);
            }
        }

        return raw;
    }

    private async Task<(int RetCode, string? Message, string? L10nKey, T? Data, HttpStatusCode HttpStatus)>
        SendRawRequestAsync<T>(
            HttpMethod method,
            string path,
            object? payload,
            bool requiresAuth,
            CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(method, path.TrimStart('/'));
            if (requiresAuth && !string.IsNullOrEmpty(_accessToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, WriteJsonOptions);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var resp = await _hutaoHttpClient.SendAsync(req, cancellationToken);
            var text = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    DebugLog.Info("HutaoCloudService", $"[Hutao] {method} {path} HTTP={(int)resp.StatusCode} empty body");
                    return (-(int)resp.StatusCode, null, null, default, resp.StatusCode);
                }

                return (-1, null, null, default, resp.StatusCode);
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    TryGetPropertyIgnoreCase(doc.RootElement, "retcode", out var retEl) &&
                    TryReadInt(retEl, out var retCode))
                {
                    var message = TryGetPropertyIgnoreCase(doc.RootElement, "message", out var msgEl)
                        ? (msgEl.ValueKind == JsonValueKind.String ? msgEl.GetString() : msgEl.ValueKind == JsonValueKind.Null ? null : msgEl.GetRawText())
                        : null;
                    var l10nKey = TryGetPropertyIgnoreCase(doc.RootElement, "l10nKey", out var l10nEl) && l10nEl.ValueKind == JsonValueKind.String
                        ? l10nEl.GetString()
                        : null;

                    if (retCode != 0)
                    {
                        DebugLog.Info(
                            "HutaoCloudService",
                            $"[Hutao] {method} {path} HTTP={(int)resp.StatusCode} retcode={retCode} l10n={l10nKey} msg={TruncateString(message, 160)}");
                        return (retCode, message, l10nKey, default, resp.StatusCode);
                    }

                    T? parsedData = default;
                    if (TryGetPropertyIgnoreCase(doc.RootElement, "data", out var dataEl) &&
                        dataEl.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    {
                        try
                        {
                            parsedData = dataEl.Deserialize<T>(JsonOptions);
                        }
                        catch (JsonException ex)
                        {
                            DebugLog.Info(
                                "HutaoCloudService",
                                $"[Hutao] {method} {path} data deserialize failed ({typeof(T).Name}): {ex.Message}");
                            return (-1, TruncateString(ex.Message, 120), l10nKey, default, resp.StatusCode);
                        }
                    }

                    return (0, message, l10nKey, parsedData, resp.StatusCode);
                }

                if (!resp.IsSuccessStatusCode)
                {
                    var detail = ExtractProblemDetail(doc.RootElement);
                    DebugLog.Info(
                        "HutaoCloudService",
                        $"[Hutao] {method} {path} HTTP={(int)resp.StatusCode} detail={TruncateString(detail, 160)}");
                    return (-(int)resp.StatusCode, detail, null, default, resp.StatusCode);
                }

                var directData = JsonSerializer.Deserialize<T>(text, JsonOptions);
                return (0, null, null, directData, resp.StatusCode);
            }
            catch (JsonException ex)
            {
                DebugLog.Info(
                    "HutaoCloudService",
                    $"[Hutao] {method} {path} HTTP={(int)resp.StatusCode} JSON error: {ex.Message} body={TruncateString(text, 160)}");
                if (!resp.IsSuccessStatusCode)
                {
                    return (-(int)resp.StatusCode, TruncateString(text, 120), null, default, resp.StatusCode);
                }
            }

            return (-1, null, null, default, resp.StatusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (-3, "HutaoSync_ErrTimeout".GetLocalized(), null, default, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return (-2, "HutaoSync_ErrNetwork".GetLocalized(), null, default, 0);
        }
        catch
        {
            return (-1, null, null, default, 0);
        }
    }

    private void ApplyTokenSet(string email, HutaoTokenSet tokenSet)
    {
        _email = string.IsNullOrWhiteSpace(email) ? _email : email.Trim();
        _accessToken = tokenSet.AccessToken;
        _refreshToken = string.IsNullOrWhiteSpace(tokenSet.RefreshToken) ? _refreshToken : tokenSet.RefreshToken;
        _accessTokenExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, tokenSet.ExpiresIn);
        SaveSessionToDisk();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSession()
    {
        _email = string.Empty;
        _accessToken = string.Empty;
        _accessTokenExpiresAtUnix = 0;
        _refreshToken = string.Empty;
        _currentUserInfo = null;

        try
        {
            if (File.Exists(_sessionFilePath))
            {
                File.Delete(_sessionFilePath);
            }
        }
        catch
        {
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveSessionToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_sessionFilePath)!);
            var session = new HutaoPersistedSession
            {
                Email = _email,
                AccessToken = _accessToken,
                AccessTokenExpiresAtUnix = _accessTokenExpiresAtUnix,
                RefreshToken = _refreshToken
            };
            var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session));
            var encrypted = ProtectedData.Protect(raw, DpapiEntropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_sessionFilePath, encrypted);
        }
        catch
        {
        }
    }

    private void LoadSessionFromDisk()
    {
        try
        {
            if (!File.Exists(_sessionFilePath)) return;

            var encrypted = File.ReadAllBytes(_sessionFilePath);
            var decrypted = ProtectedData.Unprotect(encrypted, DpapiEntropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<HutaoPersistedSession>(Encoding.UTF8.GetString(decrypted), JsonOptions);
            if (session == null || string.IsNullOrWhiteSpace(session.RefreshToken)) return;

            _email = session.Email ?? string.Empty;
            _accessToken = session.AccessToken ?? string.Empty;
            _accessTokenExpiresAtUnix = session.AccessTokenExpiresAtUnix;
            _refreshToken = session.RefreshToken ?? string.Empty;
        }
        catch
        {
            ClearSession();
        }
    }

    private string LoadOrCreateDeviceId()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_deviceIdFilePath)!);
            if (File.Exists(_deviceIdFilePath))
            {
                var existing = File.ReadAllText(_deviceIdFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(existing) && existing.Length <= 64)
                {
                    return existing;
                }
            }

            var generated = Guid.NewGuid().ToString("D");
            File.WriteAllText(_deviceIdFilePath, generated);
            return generated;
        }
        catch
        {
            return Guid.NewGuid().ToString("D");
        }
    }

    private static string EncryptRsa(string plainText)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(HutaoPublicKeyPem);
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = rsa.Encrypt(bytes, RSAEncryptionPadding.OaepSHA1);
        return Convert.ToBase64String(encrypted);
    }

    private static UIGFItem? ConvertHutaoItemToUigf(
        HutaoGachaItem item,
        int timezoneOffset,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        var idStr = item.Id.ToString(CultureInfo.InvariantCulture);
        if (!ValidIdRegex().IsMatch(idStr)) return null;

        var gachaType = ValidGachaTypes.Contains(item.GachaType)
            ? item.GachaType.ToString(CultureInfo.InvariantCulture)
            : (ValidGachaTypes.Contains(item.QueryType) ? item.QueryType.ToString(CultureInfo.InvariantCulture) : "200");

        var queryTypeStr = item.QueryType is 100 or 200 or 301 or 302 or 500
            ? item.QueryType.ToString(CultureInfo.InvariantCulture)
            : (gachaType == "400" ? "301" : gachaType);

        var timeFormatted = FormatUigfLocalTime(item.Time, timezoneOffset);
        if (timeFormatted == null || !ValidTimeRegex().IsMatch(timeFormatted)) return null;

        catalog.ById.TryGetValue(item.ItemId, out var meta);
        var itemType = !string.IsNullOrEmpty(meta.ItemType)
            ? meta.ItemType
            : (item.ItemId >= 10_000_000 ? "角色" : "武器");

        return new UIGFItem
        {
            UigfGachaType = queryTypeStr,
            GachaType = gachaType,
            ItemId = item.ItemId > 0 ? item.ItemId.ToString(CultureInfo.InvariantCulture) : string.Empty,
            Count = "1",
            Time = timeFormatted,
            Name = meta.Name ?? string.Empty,
            ItemType = itemType,
            RankType = meta.RankType ?? string.Empty,
            Id = idStr
        };
    }

    private static Dictionary<string, UIGFEntry> ParseUigfEntries(string jsonContent)
    {
        var result = new Dictionary<string, UIGFEntry>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(jsonContent)) return result;

        UIGFJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UIGFJson>(jsonContent, JsonOptions);
        }
        catch
        {
            return result;
        }

        if (parsed == null) return result;

        if (parsed.Hk4e != null && parsed.Hk4e.Count > 0)
        {
            foreach (var entry in parsed.Hk4e)
            {
                var uid = (entry.Uid ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(uid) || entry.List == null || entry.List.Count == 0) continue;

                var normalizedLang = (entry.Lang ?? string.Empty).Trim().ToLowerInvariant();
                if (!SupportedUigfLangs.Contains(normalizedLang)) normalizedLang = "zh-cn";

                var tz = entry.Timezone is >= -12 and <= 14 ? entry.Timezone : ResolveDefaultTimezone(uid);

                if (!result.TryGetValue(uid, out var bucket))
                {
                    bucket = new UIGFEntry
                    {
                        Uid = uid,
                        Timezone = tz,
                        Lang = normalizedLang,
                        List = new List<UIGFItem>()
                    };
                    result[uid] = bucket;
                }

                bucket.List.AddRange(entry.List);
            }
        }
        else if (parsed.List != null && parsed.List.Count > 0)
        {
            var uid = (parsed.Info?.Uid ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var lang = (parsed.Info?.Lang ?? string.Empty).Trim().ToLowerInvariant();
                if (!SupportedUigfLangs.Contains(lang)) lang = "zh-cn";

                result[uid] = new UIGFEntry
                {
                    Uid = uid,
                    Timezone = ResolveDefaultTimezone(uid),
                    Lang = lang,
                    List = parsed.List
                };
            }
        }

        return result;
    }

    private static void MergeExistingUigfIntoEntries(
        string existingJson,
        Dictionary<string, UIGFEntry> targetEntries,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        var existing = ParseUigfEntries(existingJson);
        foreach (var (uid, oldEntry) in existing)
        {
            if (!targetEntries.TryGetValue(uid, out var target))
            {
                targetEntries[uid] = oldEntry;
                continue;
            }

            var mergedMap = new Dictionary<string, UIGFItem>(StringComparer.Ordinal);
            foreach (var rec in oldEntry.List)
            {
                var id = (rec.Id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(id)) continue;
                EnrichUigfItemFromCatalog(rec, catalog);
                mergedMap[id] = rec;
            }

            foreach (var rec in target.List)
            {
                var id = (rec.Id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(id)) continue;
                EnrichUigfItemFromCatalog(rec, catalog);
                if (!mergedMap.TryGetValue(id, out var prior) ||
                    (string.IsNullOrWhiteSpace(prior.Name) && !string.IsNullOrWhiteSpace(rec.Name)))
                {
                    mergedMap[id] = rec;
                }
            }

            target.List = mergedMap.Values
                .OrderByDescending(x => long.TryParse(x.Id, out var idVal) ? idVal : 0L)
                .ToList();
        }
    }

    private static void EnrichUigfItemFromCatalog(
        UIGFItem rec,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        if (int.TryParse(rec.ItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
            id > 0 &&
            catalog.ById.TryGetValue(id, out var meta))
        {
            if (string.IsNullOrWhiteSpace(rec.Name) && !string.IsNullOrEmpty(meta.Name)) rec.Name = meta.Name;
            if (string.IsNullOrWhiteSpace(rec.ItemType) && !string.IsNullOrEmpty(meta.ItemType)) rec.ItemType = meta.ItemType;
            if (string.IsNullOrWhiteSpace(rec.RankType) && !string.IsNullOrEmpty(meta.RankType)) rec.RankType = meta.RankType;
        }
        else if (string.IsNullOrWhiteSpace(rec.ItemId) &&
                 !string.IsNullOrWhiteSpace(rec.Name) &&
                 catalog.ByName.TryGetValue(rec.Name.Trim(), out var resolvedId))
        {
            rec.ItemId = resolvedId.ToString(CultureInfo.InvariantCulture);
            if (catalog.ById.TryGetValue(resolvedId, out var resolvedMeta))
            {
                if (string.IsNullOrWhiteSpace(rec.ItemType)) rec.ItemType = resolvedMeta.ItemType;
                if (string.IsNullOrWhiteSpace(rec.RankType)) rec.RankType = resolvedMeta.RankType;
            }
        }
    }

    private static string BuildStrictUigfV4Json(Dictionary<string, UIGFEntry> uidEntries)
    {
        var entries = new List<object>();
        var totalCount = 0;

        foreach (var (uid, entry) in uidEntries.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(uid) || uid.Length > 64) continue;

            var strictList = new List<object>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rec in entry.List)
            {
                if (totalCount >= 250_000) break;

                var id = (rec.Id ?? string.Empty).Trim();
                if (!ValidIdRegex().IsMatch(id) || !seenIds.Add(id)) continue;

                var time = NormalizeTimeForUigf(rec.Time);
                if (time == null || !ValidTimeRegex().IsMatch(time)) continue;

                var gachaTypeInt = ParseGachaTypeInt(rec.GachaType, rec.UigfGachaType);
                if (gachaTypeInt == 0) continue;

                var gachaType = gachaTypeInt.ToString(CultureInfo.InvariantCulture);
                var uigfType = gachaType == "400"
                    ? "301"
                    : ParseGachaTypeInt(rec.UigfGachaType, gachaType).ToString(CultureInfo.InvariantCulture);
                if (uigfType is not ("100" or "200" or "301" or "302" or "500"))
                {
                    uigfType = gachaType == "400" ? "301" : "200";
                }

                strictList.Add(new
                {
                    uigf_gacha_type = uigfType,
                    gacha_type = gachaType,
                    item_id = TruncateString(rec.ItemId, 64),
                    count = string.IsNullOrWhiteSpace(rec.Count) ? "1" : TruncateString(rec.Count, 16),
                    time,
                    name = TruncateString(rec.Name, 128),
                    item_type = TruncateString(rec.ItemType, 64),
                    rank_type = TruncateString(rec.RankType, 16),
                    id
                });

                totalCount++;
            }

            if (strictList.Count == 0) continue;

            var lang = (entry.Lang ?? string.Empty).Trim().ToLowerInvariant();
            if (!SupportedUigfLangs.Contains(lang)) lang = "zh-cn";

            entries.Add(new
            {
                uid,
                timezone = entry.Timezone is >= -12 and <= 14 ? entry.Timezone : ResolveDefaultTimezone(uid),
                lang,
                list = strictList
            });

            if (totalCount >= 250_000) break;
        }

        if (entries.Count == 0) return string.Empty;

        var appVersion = !string.IsNullOrWhiteSpace(AppVersionHelper.NumericVersion)
            ? AppVersionHelper.NumericVersion
            : "1.0.0";
        if (appVersion.Length > 64) appVersion = appVersion[..64];

        var root = new
        {
            info = new
            {
                export_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                export_app = "FufuLauncher",
                export_app_version = appVersion,
                version = "v4.0"
            },
            hk4e = entries
        };

        return JsonSerializer.Serialize(root, WriteJsonOptions);
    }

    private static bool TryBuildSpiralAbyssData(SpiralAbyssDto dto, string fallbackScheduleId, out HutaoSpiralAbyssData? result)
    {
        result = null;

        var schedStr = !string.IsNullOrWhiteSpace(dto.ScheduleId) ? dto.ScheduleId : fallbackScheduleId;
        if (!int.TryParse(schedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scheduleId) || scheduleId <= 0)
        {
            return false;
        }

        if (dto.TotalBattleTimes <= 0 || dto.TotalWinTimes <= 0)
        {
            return false;
        }

        var dmg = dto.DamageRank?.FirstOrDefault();
        var defeat = dto.DefeatRank?.FirstOrDefault();
        var energy = dto.EnergySkillRank?.FirstOrDefault();
        var normal = dto.NormalSkillRank?.FirstOrDefault();
        var take = dto.TakeDamageRank?.FirstOrDefault();

        if (dmg == null || defeat == null || energy == null || normal == null || take == null ||
            dmg.AvatarId <= 0 || defeat.AvatarId <= 0 || energy.AvatarId <= 0 || normal.AvatarId <= 0 || take.AvatarId <= 0)
        {
            return false;
        }

        var floors = (dto.Floors ?? new List<SpiralAbyssFloorDto>())
            .Where(f => (f.Levels?.Count ?? 0) > 0)
            .Select(f => new HutaoAbyssFloor
            {
                Index = f.Index,
                Star = f.Star,
                Levels = (f.Levels ?? new List<SpiralAbyssLevelDto>())
                    .Where(l => (l.Battles?.Count ?? 0) > 0)
                    .Select(l => new HutaoAbyssLevel
                    {
                        Index = l.Index,
                        Star = l.Star,
                        Battles = (l.Battles ?? new List<SpiralAbyssBattleDto>())
                            .Where(b => (b.Avatars?.Count ?? 0) > 0)
                            .Select(b => new HutaoAbyssBattle
                            {
                                Index = b.Index,
                                Avatars = b.Avatars!
                                    .Select(a => a.Id)
                                    .Where(id => id > 0)
                                    .ToList()
                            })
                            .Where(b => b.Avatars.Count > 0)
                            .ToList()
                    })
                    .Where(l => l.Battles.Count > 0)
                    .ToList()
            })
            .Where(f => f.Levels.Count > 0)
            .ToList();

        if (floors.Count == 0)
        {
            return false;
        }

        result = new HutaoSpiralAbyssData
        {
            ScheduleId = scheduleId,
            TotalBattleTimes = dto.TotalBattleTimes,
            TotalWinTimes = dto.TotalWinTimes,
            Damage = new HutaoAbyssItemRank { AvatarId = dmg.AvatarId, Value = dmg.Value },
            Defeat = new HutaoAbyssItemRank { AvatarId = defeat.AvatarId, Value = defeat.Value },
            EnergySkill = new HutaoAbyssItemRank { AvatarId = energy.AvatarId, Value = energy.Value },
            NormalSkill = new HutaoAbyssItemRank { AvatarId = normal.AvatarId, Value = normal.Value },
            TakeDamage = new HutaoAbyssItemRank { AvatarId = take.AvatarId, Value = take.Value },
            Floors = floors
        };

        return true;
    }

    private static bool TryBuildRoleCombatRequest(
        string uid,
        RoleCombatEntryDto entry,
        string fallbackScheduleId,
        out HutaoRoleCombatUploadRequest? request)
    {
        request = null;

        var schedStr = !string.IsNullOrWhiteSpace(entry.Schedule?.ScheduleId)
            ? entry.Schedule!.ScheduleId
            : (!string.IsNullOrWhiteSpace(entry.ScheduleId) ? entry.ScheduleId : fallbackScheduleId);

        if (!int.TryParse(schedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scheduleId) || scheduleId <= 0)
        {
            return false;
        }

        var avatars = (entry.Detail?.BackupAvatars ?? new List<RoleCombatAvatarDto>())
            .Select(a => a.AvatarId ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (avatars.Count == 0)
        {
            return false;
        }

        request = new HutaoRoleCombatUploadRequest
        {
            Version = 1,
            Uid = uid,
            Identity = "FufuLauncher",
            BackupAvatars = avatars,
            ScheduleId = scheduleId
        };

        return true;
    }

    private static int ResolveItemId(
        UIGFItem rec,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        if (int.TryParse((rec.ItemId ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var directId) && directId > 0)
        {
            return directId;
        }

        if (!string.IsNullOrWhiteSpace(rec.Name) &&
            catalog.ByName.TryGetValue(rec.Name.Trim(), out var resolved))
        {
            return resolved;
        }

        return 0;
    }

    private static int ParseGachaTypeInt(string? primary, string? fallback)
    {
        if (int.TryParse((primary ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) &&
            ValidGachaTypes.Contains(p))
        {
            return p;
        }

        if (int.TryParse((fallback ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) &&
            ValidGachaTypes.Contains(f))
        {
            return f;
        }

        return 0;
    }

    private static string? FormatUigfLocalTime(string? rawTime, int tzHours)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;

        if (DateTimeOffset.TryParse(rawTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.ToOffset(TimeSpan.FromHours(tzHours)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(rawTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string? NormalizeTimeForUigf(string? rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;
        var trimmed = rawTime.Trim();
        if (ValidTimeRegex().IsMatch(trimmed)) return trimmed;

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)
            ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? FormatHutaoIsoTime(string? rawTime, int tzHours)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;

        var trimmed = rawTime.Trim();
        var tzOffset = TimeSpan.FromHours(tzHours);

        if (DateTime.TryParseExact(
                trimmed,
                ExactWishTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exactDt))
        {
            var offsetDto = new DateTimeOffset(DateTime.SpecifyKind(exactDt, DateTimeKind.Unspecified), tzOffset);
            return offsetDto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDt))
        {
            var offsetDto = new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Unspecified), tzOffset);
            return offsetDto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static bool IsPeriodCurrent(
        string? startUtc,
        string? endUtc,
        string? rawStartUnix,
        string? rawEndUnix,
        DateTimeOffset now)
    {
        var end = ParseTimestampOrIso(endUtc) ?? ParseTimestampOrIso(rawEndUnix);
        var start = ParseTimestampOrIso(startUtc) ?? ParseTimestampOrIso(rawStartUnix);
        if (end.HasValue)
        {
            if (now > end.Value) return false;
            if (start.HasValue && now < start.Value.AddDays(-1)) return false;
            return true;
        }

        return false;
    }

    private static string BuildPeriodText(
        string? startUtc,
        string? endUtc,
        string? rawStartUnix,
        string? rawEndUnix,
        string fallbackSchedule)
    {
        var start = ParseTimestampOrIso(startUtc) ?? ParseTimestampOrIso(rawStartUnix);
        var end = ParseTimestampOrIso(endUtc) ?? ParseTimestampOrIso(rawEndUnix);

        if (start.HasValue && end.HasValue)
        {
            return $"{start.Value.ToLocalTime():yyyy-MM-dd} ~ {end.Value.ToLocalTime():yyyy-MM-dd}";
        }

        return string.IsNullOrWhiteSpace(fallbackSchedule) ? "-" : string.Format("HutaoSync_ScheduleFallbackFormat".GetLocalized(), fallbackSchedule);
    }

    private static DateTimeOffset? ParseTimestampOrIso(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
        {
            return unix < 10_000_000_000L
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.FromUnixTimeMilliseconds(unix);
        }

        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
    }

    private static int ResolveDefaultTimezone(string uid)
    {
        if (uid.StartsWith('6')) return -5;
        if (uid.StartsWith('7')) return 1;
        return 8;
    }

    private static string TruncateString(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }

    private static bool ContainsExtendedRewardHint(string? message, string? l10nKey, string? dataText)
    {
        var combined = $"{message} {l10nKey} {dataText}";
        return combined.Contains("赠送", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("延长", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("天", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Extend", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("GachaLog", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotCurrentPeriodError(string? message, string? l10nKey)
    {
        var combined = $"{message} {l10nKey}";
        return combined.Contains("非当前", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("NotCurrent", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("ScheduleId", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("期数", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFriendlyError(int retCode, string? message, string? l10nKey, HttpStatusCode httpStatus)
    {
        if (retCode == -2) return "HutaoSync_ErrNetwork".GetLocalized();
        if (retCode == -3) return "HutaoSync_ErrTimeout".GetLocalized();

        if (retCode == 514002 || httpStatus == HttpStatusCode.Unauthorized)
        {
            return "HutaoSync_ErrAuthExpired".GetLocalized();
        }

        if (httpStatus == HttpStatusCode.TooManyRequests)
        {
            return "HutaoSync_ErrRateLimited".GetLocalized();
        }

        var combined = $"{message} {l10nKey}";

        if (combined.Contains("频繁", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("TooMany", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Rate", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("60", StringComparison.OrdinalIgnoreCase) && combined.Contains("秒", StringComparison.OrdinalIgnoreCase))
        {
            return "HutaoSync_ErrRateLimited".GetLocalized();
        }

        if ((combined.Contains("Uid", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("上限", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("5", StringComparison.OrdinalIgnoreCase)) &&
            (combined.Contains("Exceed", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("Limit", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("最多", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("超出", StringComparison.OrdinalIgnoreCase)))
        {
            return "HutaoSync_ErrUidLimit".GetLocalized();
        }

        if ((combined.Contains("Gacha", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("祈愿", StringComparison.OrdinalIgnoreCase)) &&
            (combined.Contains("过期", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("未开通", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("Expire", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("Entitlement", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("Subscription", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("NotActivated", StringComparison.OrdinalIgnoreCase)))
        {
            return "HutaoSync_ErrNoGachaService".GetLocalized();
        }

        if (combined.Contains("验证码", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("VerifyCode", StringComparison.OrdinalIgnoreCase))
        {
            return "HutaoSync_ErrInvalidVerifyCode".GetLocalized();
        }

        if (combined.Contains("已存在", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("已注册", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Already", StringComparison.OrdinalIgnoreCase))
        {
            return "HutaoSync_ErrAccountExists".GetLocalized();
        }

        if (combined.Contains("密码", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("UserName", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("邮箱", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("不存在", StringComparison.OrdinalIgnoreCase))
        {
            return "HutaoSync_ErrInvalidCredentials".GetLocalized();
        }

        if (combined.Contains("兑换", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Redeem", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("已使用", StringComparison.OrdinalIgnoreCase))
        {
            return "HutaoSync_ErrRedeemInvalid".GetLocalized();
        }

        if (IsNotCurrentPeriodError(message, l10nKey))
        {
            return "HutaoSync_ErrNotCurrentPeriod".GetLocalized();
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            return TruncateString(message, 120);
        }

        if (!string.IsNullOrWhiteSpace(l10nKey))
        {
            return TruncateString(l10nKey, 80);
        }

        if ((int)httpStatus >= 400)
        {
            return string.Format("HutaoSync_ErrServerStatusFormat".GetLocalized(), (int)httpStatus);
        }

        return "HutaoSync_ErrGeneric".GetLocalized();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadInt(JsonElement el, out int result)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out result))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String &&
            int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static string? ExtractProblemDetail(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in ProblemDetailKeys)
        {
            if (TryGetPropertyIgnoreCase(root, key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }
            }
        }

        if (TryGetPropertyIgnoreCase(root, "errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in errorsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        {
                            return $"{prop.Name}: {item.GetString()}";
                        }
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                {
                    return $"{prop.Name}: {prop.Value.GetString()}";
                }
            }
        }

        return null;
    }

    public async Task<HutaoOperationResult<HutaoStatisticsBundle>> GetHutaoStatisticsBundleAsync(
        bool lastPeriod,
        string? uid,
        CancellationToken cancellationToken = default)
    {
        var suffix = lastPeriod ? "?last=true" : "?last=false";

        var overviewTask = SendRawRequestAsync<HutaoAbyssOverview>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsOverviewPath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var utilTask = SendRawRequestAsync<List<HutaoAbyssAvatarRateFloor>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsUtilizationRatePath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var attendTask = SendRawRequestAsync<List<HutaoAbyssAvatarRateFloor>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsAttendanceRatePath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var holdTask = SendRawRequestAsync<List<HutaoAbyssAvatarHolding>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsHoldingRatePath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var teamTask = SendRawRequestAsync<List<HutaoAbyssTeamFloor>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsTeamCombinationPath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var theaterTask = SendRawRequestAsync<HutaoRoleCombatStatistics>(
            HttpMethod.Get,
            ApiEndpoints.HutaoRoleCombatStatisticsPath,
            null,
            requiresAuth: false,
            cancellationToken);

        var catalogTask = ResolveItemCatalogAsync(cancellationToken);

        var trimmedUid = (uid ?? string.Empty).Trim();
        Task<(int RetCode, string? Message, string? L10nKey, HutaoRecordRankData? Data, HttpStatusCode HttpStatus)>? rankTask =
            !string.IsNullOrEmpty(trimmedUid)
                ? SendRawRequestAsync<HutaoRecordRankData>(
                    HttpMethod.Get,
                    $"{ApiEndpoints.HutaoRecordRankPath}?Uid={Uri.EscapeDataString(trimmedUid)}",
                    null,
                    requiresAuth: false,
                    cancellationToken)
                : null;

        await Task.WhenAll(
            overviewTask,
            utilTask,
            attendTask,
            holdTask,
            teamTask,
            theaterTask,
            catalogTask);

        if (rankTask != null)
        {
            await rankTask;
        }

        var overviewRes = overviewTask.Result;
        var utilRes = utilTask.Result;
        var attendRes = attendTask.Result;
        var holdRes = holdTask.Result;
        var teamRes = teamTask.Result;
        var theaterRes = theaterTask.Result;
        var catalog = catalogTask.Result;
        var rankRes = rankTask?.Result;

        if (overviewRes.RetCode != 0 &&
            utilRes.RetCode != 0 &&
            attendRes.RetCode != 0 &&
            holdRes.RetCode != 0 &&
            teamRes.RetCode != 0 &&
            theaterRes.RetCode != 0)
        {
            return Fail<HutaoStatisticsBundle>(
                FormatFriendlyError(overviewRes.RetCode, overviewRes.Message, overviewRes.L10nKey, overviewRes.HttpStatus));
        }

        var byId = catalog.ById;
        var utilByFloor = BuildFloorRateRows(utilRes.Data, byId);
        var attendByFloor = BuildFloorRateRows(attendRes.Data, byId);
        var holdingRates = BuildHoldingRows(holdRes.Data, byId);
        var teamsByFloor = BuildTeamRows(teamRes.Data, byId);
        var roleCombatRates = BuildRoleCombatRows(theaterRes.Data, byId);

        var uidRankData = rankRes.HasValue && rankRes.Value.RetCode == 0 ? rankRes.Value.Data : null;
        var dmgAvatarName = uidRankData?.Damage is { Item: > 0 }
            ? ResolveAvatarName(uidRankData.Damage.Item, byId)
            : string.Empty;
        var takeAvatarName = uidRankData?.TakeDamage is { Item: > 0 }
            ? ResolveAvatarName(uidRankData.TakeDamage.Item, byId)
            : string.Empty;

        return new HutaoOperationResult<HutaoStatisticsBundle>
        {
            Ok = true,
            Data = new HutaoStatisticsBundle
            {
                IsLastPeriod = lastPeriod,
                QueriedUid = trimmedUid,
                Overview = overviewRes.RetCode == 0 ? overviewRes.Data : null,
                RoleCombat = theaterRes.RetCode == 0 ? theaterRes.Data : null,
                UidRank = uidRankData,
                UtilizationByFloor = utilByFloor,
                AttendanceByFloor = attendByFloor,
                HoldingRates = holdingRates,
                TeamsByFloor = teamsByFloor,
                RoleCombatRates = roleCombatRates,
                UidDamageAvatarName = dmgAvatarName,
                UidTakeDamageAvatarName = takeAvatarName
            }
        };
    }

    private static bool HasRequiredHutaoAvatars(IReadOnlyList<HutaoSimpleAvatar>? avatars)
    {
        if (avatars == null || avatars.Count <= 8) return false;
        var ids = avatars.Select(a => a.AvatarId).ToHashSet();
        var hasTraveler = ids.Contains(10000005) || ids.Contains(10000007);
        return hasTraveler && ids.Contains(10000021) && ids.Contains(10000015) && ids.Contains(10000006);
    }

    private static string ExtractChallengeFailureReason(int retCode, string? message, string? l10nKey, HttpStatusCode httpStatus)
    {
        if (retCode == 500020 ||
            (!string.IsNullOrWhiteSpace(message) && message.Contains("无效", StringComparison.OrdinalIgnoreCase)))
        {
            return "HutaoSync_ErrChallengeInvalidData".GetLocalized();
        }

        var friendly = FormatFriendlyError(retCode, message, l10nKey, httpStatus);
        var generic = "HutaoSync_ErrGeneric".GetLocalized();
        if (!string.Equals(friendly, generic, StringComparison.Ordinal))
        {
            return friendly;
        }

        return !string.IsNullOrWhiteSpace(message) ? TruncateString(message, 48) : generic;
    }

    private static string ResolveAvatarName(
        int avatarId,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        if (avatarId == 10000005) return "HutaoSync_TravelerMaleName".GetLocalized();
        if (avatarId == 10000007) return "HutaoSync_TravelerFemaleName".GetLocalized();

        if (byId.TryGetValue(avatarId, out var info) && !string.IsNullOrWhiteSpace(info.Name))
        {
            return info.Name;
        }

        return $"#{avatarId}";
    }

    private static Dictionary<int, List<HutaoRankDisplayItem>> BuildFloorRateRows(
        List<HutaoAbyssAvatarRateFloor>? floors,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        var map = new Dictionary<int, List<HutaoRankDisplayItem>>();
        if (floors == null) return map;

        foreach (var floor in floors)
        {
            var sorted = (floor.Ranks ?? new List<HutaoItemRateIntDouble>())
                .Where(r => r.Item > 0)
                .OrderByDescending(r => r.Rate)
                .ToList();

            var list = new List<HutaoRankDisplayItem>(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                var entry = sorted[i];
                list.Add(new HutaoRankDisplayItem
                {
                    RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                    PrimaryText = ResolveAvatarName(entry.Item, byId),
                    SecondaryText = string.Format("HutaoSync_StatsFloorFormat".GetLocalized(), floor.Floor),
                    MetricText = $"{entry.Rate * 100:0.0}%"
                });
            }

            map[floor.Floor] = list;
        }

        return map;
    }

    private static List<HutaoRankDisplayItem> BuildHoldingRows(
        List<HutaoAbyssAvatarHolding>? holdings,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        if (holdings == null) return new List<HutaoRankDisplayItem>();

        var sorted = holdings
            .Where(h => h.AvatarId > 0)
            .OrderByDescending(h => h.HoldingRate)
            .ToList();

        var list = new List<HutaoRankDisplayItem>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            var constParts = (item.Constellations ?? new List<HutaoItemRateIntDouble>())
                .OrderBy(c => c.Item)
                .Select(c => string.Format("HutaoSync_StatsConstellationFormat".GetLocalized(), c.Item, $"{c.Rate * 100:0.0}%"))
                .ToList();

            list.Add(new HutaoRankDisplayItem
            {
                RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                PrimaryText = ResolveAvatarName(item.AvatarId, byId),
                SecondaryText = constParts.Count > 0 ? string.Join(" · ", constParts) : string.Empty,
                MetricText = $"{item.HoldingRate * 100:0.0}%"
            });
        }

        return list;
    }

    private static Dictionary<int, List<HutaoRankDisplayItem>> BuildTeamRows(
        List<HutaoAbyssTeamFloor>? floors,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        var map = new Dictionary<int, List<HutaoRankDisplayItem>>();
        if (floors == null) return map;

        var upperLabel = "HutaoSync_StatsTeamUpperLabel".GetLocalized();
        var lowerLabel = "HutaoSync_StatsTeamLowerLabel".GetLocalized();

        foreach (var floor in floors)
        {
            var combined = new List<(string SideLabel, string ItemIds, int Rate)>();
            if (floor.Up != null)
            {
                combined.AddRange(floor.Up.Select(t => (upperLabel, t.Item, t.Rate)));
            }
            if (floor.Down != null)
            {
                combined.AddRange(floor.Down.Select(t => (lowerLabel, t.Item, t.Rate)));
            }

            combined.Sort((a, b) => b.Rate.CompareTo(a.Rate));

            var list = new List<HutaoRankDisplayItem>(combined.Count);
            for (var i = 0; i < combined.Count; i++)
            {
                var entry = combined[i];
                var memberNames = (entry.ItemIds ?? string.Empty)
                    .Split(new[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(rawId => int.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                        ? ResolveAvatarName(id, byId)
                        : rawId)
                    .ToList();

                list.Add(new HutaoRankDisplayItem
                {
                    RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                    PrimaryText = memberNames.Count > 0 ? string.Join(" / ", memberNames) : entry.ItemIds,
                    SecondaryText = $"{entry.SideLabel} · {string.Format("HutaoSync_StatsFloorFormat".GetLocalized(), floor.Floor)}",
                    MetricText = string.Format("HutaoSync_StatsTeamCountFormat".GetLocalized(), entry.Rate)
                });
            }

            map[floor.Floor] = list;
        }

        return map;
    }

    private static List<HutaoRankDisplayItem> BuildRoleCombatRows(
        HutaoRoleCombatStatistics? stats,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        if (stats?.BackupAvatarRates == null) return new List<HutaoRankDisplayItem>();

        var sorted = stats.BackupAvatarRates
            .Where(r => r.Item > 0)
            .OrderByDescending(r => r.Rate)
            .ToList();

        var list = new List<HutaoRankDisplayItem>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var entry = sorted[i];
            list.Add(new HutaoRankDisplayItem
            {
                RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                PrimaryText = ResolveAvatarName(entry.Item, byId),
                SecondaryText = string.Format("HutaoSync_StatsRoleCombatRecordCountFormat".GetLocalized(), stats.RecordTotal),
                MetricText = $"{entry.Rate * 100:0.0}%"
            });
        }

        return list;
    }

    private static HutaoOperationResult<T> Fail<T>(string message)
        => new()
        {
            Ok = false,
            Message = message
        };
}
