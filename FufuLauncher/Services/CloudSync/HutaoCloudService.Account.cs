using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class HutaoCloudService
{
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

    public async Task<HutaoOperationResult<HutaoUserInfo>> RegisterAsync(string email, string password,
        string verifyCode)
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

    public async Task<HutaoOperationResult<HutaoUserInfo>> ResetPasswordAsync(string email, string newPassword,
        string verifyCode)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(newPassword) ||
            string.IsNullOrWhiteSpace(verifyCode))
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
        var res = await SendAuthenticatedAsync<HutaoUserInfo>(HttpMethod.Get, ApiEndpoints.HutaoPassportUserInfoPath,
            null);
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
            .Where(d => !string.IsNullOrWhiteSpace(d.EffectiveDeviceId) ||
                        !string.IsNullOrWhiteSpace(d.EffectiveDeviceName))
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
}