using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

public sealed class CommunityApiException(
    string message,
    int code = -1,
    bool verification = false,
    bool loginExpired = false,
    string? aigis = null) : Exception(message)
{
    public int Code
    {
        get;
    } = code;

    public bool NeedsVerification
    {
        get;
    } = verification;

    public bool LoginExpired
    {
        get;
    } = loginExpired;

    public string? Aigis
    {
        get;
    } = aigis;
}

public sealed partial class MiyousheClient : IDisposable
{
    private const string BaseUrl = ApiEndpoints.MiyousheBbsApiBase + "/";
    private const string Version = "2.115.0";
    private const string K2Salt = "09d39d16528ca0e4900a39ecc07d5062";
    private const string X4Salt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string WebSalt = "r3KppdID2yT6ht6P7MxzQykauJj0Cmtg";
    private const string WebVersion = "2.102.0";
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequest;
    private readonly HttpClient _http;
    private readonly AccountContext? _context;
    private CommunityApiException? _blocked;
    private DateTimeOffset _cooldown;
    private Profile _blockedProfile;
    private string? _challenge;
    private string? _aigis;
    private string _currentRequestKey = "", _verificationRequestKey = "";
    public bool IsAuthenticated => _context != null && BuildCookies(false).Length > 0;
    public bool NeedsVerification => _blocked?.NeedsVerification == true;

    public string AccountUid => _context == null ? ""
        : _context.Stuid.Length > 0 ? _context.Stuid
        : FirstCookie("account_id_v2", "ltuid_v2", "account_id", "ltuid", "stuid");

    private enum Profile
    {
        Web,
        WebSign,
        X4,
        X4Sign,
        K2Sign,
        K2
    }

    public MiyousheClient(AccountContext? context = null, HttpMessageHandler? handler = null)
    {
        if (context != null && (context.ServerType != ServerType.Cn ||
                                string.IsNullOrEmpty(context.Device.BbsDeviceId) ||
                                string.IsNullOrEmpty(context.Device.DeviceFp)))
            throw new InvalidOperationException("米游社账号的设备身份不完整");
        _context = context;
        _http = new(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All
        }) { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task<(IReadOnlyList<CommunityGame> Games, IReadOnlyList<CommunityForum> Forums)> GetNavigationAsync(
        CancellationToken ct)
    {
        var games = await SendAsync(ApiEndpoints.MiyousheGetGameListPath, [], Profile.Web, ct);
        var forums = await SendAsync(ApiEndpoints.MiyousheGetAllGamesForumsPath, [], Profile.Web, ct);
        return (
            games.Get("list").Items()
                .Select(g => new CommunityGame((int)g.Number("id"), g.Text("name"), g.Text("en_name"))).ToArray(),
            forums.Get("list").Items().SelectMany(g => g.Get("forums").Items()).Where(f => f.Number("visible") == 1)
                .Select(f => new CommunityForum((int)f.Number("id"), (int)f.Number("game_id"), f.Text("name")))
                .ToArray());
    }

    public async Task<CommunityPage<CommunityPost>> GetFeedAsync(FeedRequest feed, string cursor, CancellationToken ct)
    {
        var q = new Dictionary<string, string> { ["gids"] = feed.GameId.ToString(), ["size"] = "20" };
        string endpoint;
        Profile profile = Profile.Web;
        switch (feed.Kind)
        {
            case CommunityFeed.Forum:
                endpoint = feed.Sort == 3
                    ? ApiEndpoints.MiyousheGetHotForumPostListPath
                    : ApiEndpoints.MiyousheGetRecentForumPostListPath;
                q = new()
                {
                    ["forum_id"] = feed.ForumId.ToString(), ["gids"] = feed.GameId.ToString(), ["is_good"] = "false",
                    ["page_size"] = "20", ["last_id"] = cursor
                };
                if (feed.Sort != 3) q["sort_type"] = feed.Sort.ToString();
                profile = _context == null ? Profile.Web : Profile.X4;
                break;
            case CommunityFeed.News:
                endpoint = ApiEndpoints.MiyousheGetNewsListPath;
                q = new()
                {
                    ["gids"] = feed.GameId.ToString(), ["type"] = feed.Sort.ToString(), ["page_size"] = "20",
                    ["last_id"] = cursor.Length == 0 ? "0" : cursor
                };
                break;
            case CommunityFeed.Search:
                endpoint = ApiEndpoints.MiyousheSearchPostsPath;
                q["keyword"] = feed.Target;
                q["last_id"] = cursor;
                q["order_type"] = feed.Sort.ToString();
                break;
            case CommunityFeed.Following:
                RequireLogin("ltoken", "ltoken_v2");
                endpoint = ApiEndpoints.MiyousheTimelineListPath;
                q["gids"] = "0";
                if (cursor.Length > 0) q["offset"] = cursor;
                profile = Profile.X4Sign;
                break;
            case CommunityFeed.Favorites:
                RequireLogin("cookie_token", "cookie_token_v2");
                endpoint = ApiEndpoints.MiyousheUserFavouritePostPath;
                q = new() { ["uid"] = AccountUid, ["size"] = "20", ["offset"] = cursor };
                profile = Profile.X4;
                break;
            case CommunityFeed.User:
                endpoint = ApiEndpoints.MiyousheUserPostPath;
                q["uid"] = feed.Target;
                if (cursor.Length > 0) q["offset"] = cursor;
                break;
            case CommunityFeed.Topic:
                endpoint = ApiEndpoints.MiyousheGetTopicPostListPath;
                q = new()
                {
                    ["gids"] = feed.GameId.ToString(), ["game_id"] = feed.GameId.ToString(), ["topic_id"] = feed.Target,
                    ["list_type"] = feed.Sort.ToString(), ["last_id"] = cursor, ["page_size"] = "20"
                };
                break;
            case CommunityFeed.Collection:
                endpoint = ApiEndpoints.MiyousheGetPostFullInCollectionPath;
                q = new() { ["collection_id"] = feed.Target };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(feed));
        }

        var data = await SendAsync(endpoint, q, profile, ct, ltokenOnly: feed.Kind == CommunityFeed.Following);
        var list = data.Get("list");
        if (list.ValueKind != JsonValueKind.Array) list = data.Get("posts");
        var posts = list.Items().Select(p => CommunityPost.Parse(p, feed.GameId)).Where(p => p.Id.Length > 0).ToArray();
        string next = data.Text("next_offset");
        if (next.Length == 0) next = data.Text("last_id");
        return new(posts, next,
            feed.Kind == CommunityFeed.Collection || data.Flag("is_last") || posts.Length == 0 || next.Length == 0 ||
            next == cursor);
    }

    public async Task<CommunityPost> GetPostAsync(string id, int gameId, CancellationToken ct)
    {
        ValidateId(id);
        var data = await SendAsync(ApiEndpoints.MiyousheGetPostFullPath, new() { ["post_id"] = id, ["read"] = "1" },
            _context == null ? Profile.Web : Profile.K2Sign, ct);
        var post = CommunityPost.Parse(data.Get("post"), gameId);
        if (post.Id.Length == 0) throw new CommunityApiException("帖子内容不可用");
        return post;
    }

    public async Task<CommunityPage<CommunityReply>> GetRepliesAsync(CommunityPost post, string cursor, int order,
        bool onlyAuthor,
        CancellationToken ct, string? floor = null)
    {
        var q = new Dictionary<string, string>
            { ["post_id"] = post.Id, ["gids"] = post.GameId.ToString(), ["size"] = "20", ["last_id"] = cursor };
        if (floor != null) q["floor_id"] = floor;
        else
        {
            q["is_hot"] = order == 0 && !onlyAuthor ? "true" : "false";
            if (order > 0 || onlyAuthor) q["order_type"] = (onlyAuthor ? 1 : order).ToString();
            if (onlyAuthor) q["only_master"] = "true";
        }

        var data = await SendAsync(
            floor == null ? ApiEndpoints.MiyousheGetPostRepliesPath : ApiEndpoints.MiyousheGetSubRepliesPath, q,
            _context == null ? Profile.Web : Profile.X4Sign, ct);
        var replies = data.Get("list").Items().Select(CommunityReply.Parse).ToArray();
        var next = data.Text("last_id");
        return new(replies, next, data.Flag("is_last") || replies.Length == 0 || next.Length == 0 || next == cursor);
    }

    public async Task<string> GetDescriptionAsync(FeedRequest feed, CancellationToken ct)
    {
        string endpoint = feed.Kind switch
        {
            CommunityFeed.User => ApiEndpoints.MiyousheGetUserFullInfoPath,
            CommunityFeed.Topic => ApiEndpoints.MiyousheGetTopicFullInfoPath,
            CommunityFeed.Collection => ApiEndpoints.MiyousheGetCollectionDetailPath, _ => ""
        };
        if (endpoint.Length == 0) return "";
        var q = new Dictionary<string, string>
            { ["gids"] = feed.GameId.ToString(), [feed.Kind == CommunityFeed.User ? "uid" : "id"] = feed.Target };
        var data = await SendAsync(endpoint, q,
            feed.Kind == CommunityFeed.Topic || _context == null ? Profile.Web : Profile.X4Sign, ct);
        var info = feed.Kind switch
        {
            CommunityFeed.User => data.Get("user_info"), CommunityFeed.Topic => data.Get("topic"),
            _ => data.Get("collection_info")
        };
        if (info.ValueKind != JsonValueKind.Object) info = data;
        return string.Join("\n",
            new[]
            {
                info.Text("nickname"), info.Text("name"), info.Text("title"), info.Text("introduce"), info.Text("desc"),
                info.Text("description")
            }.Where(s => s.Length > 0));
    }

    public async Task VerifyAsync(Func<JsonElement, string?, CancellationToken, Task<JsonElement?>> showCaptcha,
        CancellationToken ct)
    {
        var blocked = _blocked ?? throw new InvalidOperationException("没有待处理的验证");
        if (!blocked.NeedsVerification) throw blocked;
        if (blocked.Aigis is { Length: > 0 } raw)
        {
            using var doc = JsonDocument.Parse(raw);
            var session = doc.RootElement.Text("session_id");
            var data = doc.RootElement.Get("data");
            if (data.ValueKind == JsonValueKind.String)
            {
                using var nested = JsonDocument.Parse(data.GetString()!);
                data = nested.RootElement.Clone();
            }

            if (session.Length == 0 || data.Text("gt").Length == 0)
                throw new CommunityApiException("验证参数不完整，请在米游社官方页面完成验证");
            var result = await showCaptcha(data, session, ct) ?? throw new OperationCanceledException(ct);
            _aigis = session + ";" + Convert.ToBase64String(Encoding.UTF8.GetBytes(result.GetRawText()));
        }
        else
        {
            var profile = _blockedProfile switch
            {
                Profile.WebSign => Profile.WebSign,
                Profile.K2 or Profile.K2Sign => Profile.K2Sign,
                _ => Profile.X4Sign
            };
            var data = await SendAsync(ApiEndpoints.MiyousheCreateVerificationPath, new() { ["is_high"] = "true" },
                profile, ct,
                verificationRequest: true);
            if (data.Text("gt").Length == 0) throw new CommunityApiException("服务未提供可用的验证码，请在米游社官方页面完成验证");
            var result = await showCaptcha(data, null, ct) ?? throw new OperationCanceledException(ct);
            var verified = await SendAsync(ApiEndpoints.MiyousheVerifyVerificationPath, [], profile switch
                {
                    Profile.WebSign => Profile.WebSign, Profile.K2Sign => Profile.K2, _ => Profile.X4
                },
                ct, result.GetRawText(), verificationRequest: true);
            _challenge = verified.Text("challenge");
            if (_challenge.Length == 0) throw new CommunityApiException("验证未成功，请重试");
        }

        _blocked = null;
    }

    private async Task<JsonElement> SendAsync(string endpoint, Dictionary<string, string> query, Profile profile,
        CancellationToken ct,
        string? body = null, bool verificationRequest = false, bool ltokenOnly = false, bool submission = false)
    {
        if (!verificationRequest && _blocked != null) throw _blocked;
        if (_cooldown > DateTimeOffset.UtcNow)
            throw new CommunityApiException(
                $"请求频率受限，请等待 {Math.Ceiling((_cooldown - DateTimeOffset.UtcNow).TotalSeconds)} 秒再重试", 429);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        ct = deadline.Token;
        await RequestGate.WaitAsync(ct);
        bool sent = false;
        try
        {
            var delay = TimeSpan.FromMilliseconds(650) - (DateTimeOffset.UtcNow - _lastRequest);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            ct.ThrowIfCancellationRequested();
            if (!verificationRequest && _blocked != null) throw _blocked;
            _lastRequest = DateTimeOffset.UtcNow;
            string requestKey = endpoint + "|" + string.Join("&",
                query.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)) + "|" + body;
            if (!verificationRequest) _currentRequestKey = requestKey;
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post,
                BaseUrl + endpoint + (query.Count == 0
                    ? ""
                    : "?" + string.Join("&", query.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)))));
            request.Headers.Referrer = new(ApiEndpoints.BbsMihoyoBase + "/");
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FufuLauncher/1.7");
            if (profile != Profile.Web)
            {
                if (profile == Profile.WebSign)
                {
                    request.Headers.Referrer = new(ApiEndpoints.MiyousheWebBase + "/");
                    request.Headers.TryAddWithoutValidation("Origin", ApiEndpoints.MiyousheWebBase);
                    request.Headers.TryAddWithoutValidation("x-rpc-app_version", WebVersion);
                    request.Headers.TryAddWithoutValidation("x-rpc-client_type", "4");
                    request.Headers.TryAddWithoutValidation("DS", CreateDs(WebSalt, true, query, body ?? ""));
                }
                else
                {
                    request.Headers.Remove("User-Agent");
                    request.Headers.TryAddWithoutValidation("User-Agent", _context == null
                        ? "Mozilla/5.0 (Linux; Android 12) Mobile miHoYoBBS/" + Version
                        : System.Text.RegularExpressions.Regex.Replace(_context.UserAgent.Mobile, @"miHoYoBBS/[\d.]+",
                            "miHoYoBBS/" + Version));
                    request.Headers.TryAddWithoutValidation("x-rpc-app_version", Version);
                    request.Headers.TryAddWithoutValidation("x-rpc-client_type",
                        profile is Profile.K2 or Profile.K2Sign ? "2" : "5");
                    request.Headers.TryAddWithoutValidation("x-requested-with", "com.mihoyo.hyperion");
                    request.Headers.TryAddWithoutValidation("DS",
                        CreateDs(profile is Profile.K2 or Profile.K2Sign ? K2Salt : X4Salt,
                            profile is Profile.K2Sign or Profile.X4Sign, query, body ?? ""));
                }

                if (_context != null)
                {
                    request.Headers.TryAddWithoutValidation("x-rpc-device_id", _context.Device.BbsDeviceId);
                    request.Headers.TryAddWithoutValidation("x-rpc-device_fp", _context.Device.DeviceFp);
                    request.Headers.TryAddWithoutValidation("Cookie", BuildCookies(ltokenOnly));
                }
            }

            if (!verificationRequest && requestKey == _verificationRequestKey)
            {
                if (_challenge != null) request.Headers.TryAddWithoutValidation("x-rpc-challenge", _challenge);
                if (_aigis != null) request.Headers.TryAddWithoutValidation("x-rpc-aigis", _aigis);
                _challenge = _aigis = null;
            }

            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            sent = true;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            string? aigis = response.Headers.TryGetValues("x-rpc-aigis", out var values)
                ? values.FirstOrDefault()
                : null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ??
                           (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(30);
                _cooldown = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 300));
                throw new CommunityApiException("请求频率受限，请稍后手动重试", 429);
            }

            if (aigis != null || response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.PreconditionFailed)
                throw Block(new("米游社要求完成安全验证", (int)response.StatusCode, true, aigis: aigis), profile,
                    verificationRequest);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new InvalidDataException("响应内容过大");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("响应内容过大");
                buffer.Write(chunk, 0, read);
            }

            using var doc = JsonDocument.Parse(buffer.ToArray());
            var root = doc.RootElement;
            var retcode = root.Get("retcode");
            if (retcode.ValueKind != JsonValueKind.Number || !retcode.TryGetInt32(out int code))
                throw new InvalidDataException("米游社返回了无法识别的响应");
            if (code != 0)
                throw Block(new(root.Text("message"), code, code is 1028 or 1034, code is -100 or 10001 or -101),
                    profile, verificationRequest);
            return root.Get("data").Clone();
        }
        catch (Exception ex) when (submission && sent && ex is HttpRequestException or OperationCanceledException
                                       or JsonException or IOException or InvalidDataException
                                       or ObjectDisposedException)
        {
            throw new CommunitySubmissionException(ex);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private CommunityApiException Block(CommunityApiException error, Profile profile, bool verificationRequest)
    {
        if (!verificationRequest && error.NeedsVerification)
        {
            _blocked = error;
            _blockedProfile = profile;
            _verificationRequestKey = _currentRequestKey;
            _challenge = _aigis = null;
        }

        return error;
    }

    internal static string CreateDs(string salt, bool ds1, IReadOnlyDictionary<string, string> query, string body,
        long? timestamp = null, string? nonce = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var r = nonce ?? (ds1
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()
            : RandomNumberGenerator.GetInt32(100000, 200001).ToString());
        string input = $"salt={salt}&t={t}&r={r}";
        if (!ds1)
            input += "&b=" + body + "&q=" + string.Join("&",
                query.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        return $"{t},{r},{Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()}";
    }

    private string FirstCookie(params string[] keys) => keys.Select(k => _context?.Cookies.GetValueOrDefault(k))
        .FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

    private void RequireLogin(params string[] keys)
    {
        bool ltoken = keys.Any(k => k is "ltoken" or "ltoken_v2") && LTokenCookies.Length > 0;
        bool cookieToken = keys.Any(k => k is "cookie_token" or "cookie_token_v2") && CookieTokenCookies.Length > 0;
        if (_context == null || (!ltoken && !cookieToken))
            throw new CommunityApiException("请先在启动器账号管理中登录米游社国服账号，再选择该账号", -100, loginExpired: true);
    }

    public static void ValidateId(string id)
    {
        if (id.Length is < 1 or > 20 || !id.All(char.IsAsciiDigit)) throw new ArgumentException("无效的米游社 ID");
    }

    public void Dispose() => _http.Dispose();
}