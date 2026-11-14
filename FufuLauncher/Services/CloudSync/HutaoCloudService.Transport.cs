using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class HutaoCloudService
{
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
                    DebugLog.Info("HutaoCloudService",
                        $"[Hutao] {method} {path} HTTP={(int)resp.StatusCode} empty body");
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
                        ? (msgEl.ValueKind == JsonValueKind.String ? msgEl.GetString() :
                            msgEl.ValueKind == JsonValueKind.Null ? null : msgEl.GetRawText())
                        : null;
                    var l10nKey = TryGetPropertyIgnoreCase(doc.RootElement, "l10nKey", out var l10nEl) &&
                                  l10nEl.ValueKind == JsonValueKind.String
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
            var session =
                JsonSerializer.Deserialize<HutaoPersistedSession>(Encoding.UTF8.GetString(decrypted), JsonOptions);
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

    private static string TruncateString(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
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
            combined.Contains("60", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("秒", StringComparison.OrdinalIgnoreCase))
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
                else if (prop.Value.ValueKind == JsonValueKind.String &&
                         !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                {
                    return $"{prop.Name}: {prop.Value.GetString()}";
                }
            }
        }

        return null;
    }

    private static HutaoOperationResult<T> Fail<T>(string message)
        => new()
        {
            Ok = false,
            Message = message
        };
}