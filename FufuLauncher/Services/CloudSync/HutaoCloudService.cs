using System.Globalization;
using System.Net;
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

    private async Task<(Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int>
            ByName)>
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

                var icon = elem.TryGetProperty("icon", out var iconProp)
                    ? iconProp.GetString() ?? string.Empty
                    : string.Empty;
                var rank = string.Empty;
                if (isAvatar && elem.TryGetProperty("avatar_level", out var avLv) && avLv.TryGetInt32(out var avRank))
                {
                    rank = avRank.ToString(CultureInfo.InvariantCulture);
                }
                else if (!isAvatar && elem.TryGetProperty("weapon_level", out var wpLv) &&
                         wpLv.TryGetInt32(out var wpRank))
                {
                    rank = wpRank.ToString(CultureInfo.InvariantCulture);
                }

                var elementSrc = string.Empty;
                if (isAvatar && elem.TryGetProperty("element_attr_id", out var elemProp) &&
                    elemProp.TryGetInt32(out var elemId))
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
}