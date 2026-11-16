/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 游戏扫码登录

    private async Task StartGameLoginFlowAsync(LoginSession session)
    {
        if (session.FlowStarted) return;

        session.FlowStarted = true;
        _isLoginCompleting = false;

        for (int refreshCount = 0;; refreshCount++)
        {
            if (!IsSessionActive(session)) return;

            if (refreshCount == 0)
                UpdateStatus("LoginQR_CreatingGameQr".GetLocalized(), true);

            var qrResult = await CreateGameQrCodeAsync(session);
            if (!qrResult.Success)
            {
                UpdateStatus(string.Format("LoginQR_CreateFailed_Format".GetLocalized(), qrResult.Message), false);
                return;
            }

            if (!IsSessionActive(session)) return;

            session.QrCreatedAt = DateTimeOffset.UtcNow;
            RenderQrCode(qrResult.Url);
            UpdateStatus("LoginQR_ScanWithMiyousheOrGame".GetLocalized(), false, true);
            SetQrHint(refreshCount == 0 ? "LoginQR_AutoRefreshHint" : "LoginQR_AutoRefreshed");

            var outcome = await PollGameLoginStatusAsync(session);
            if (outcome != QrPollOutcome.Refresh || !IsSessionActive(session)) return;

            if (refreshCount >= MaxQrAutoRefreshCount)
            {
                DebugLog.Info("LoginQrWindow.GameQrLogin", "二维码自动刷新次数已达上限");
                SetQrHint("LoginQR_AutoRefreshPaused");
                UpdateStatus("LoginQR_QrExpiredMaxRefresh".GetLocalized(), false);
                return;
            }

            DebugLog.Info("LoginQrWindow.GameQrLogin",
                $"二维码已过期，自动刷新 ({refreshCount + 1}/{MaxQrAutoRefreshCount})");
        }
    }

    private async Task<(bool Success, string Url, string Message)> CreateGameQrCodeAsync(LoginSession session)
    {
        string url = ApiEndpoints.Hk4eQrCodeFetchUrl;
        var requestBody = new JsonObject
        {
            ["app_id"] = int.Parse(session.GameAppId),
            ["device"] = session.GameDevice
        };
        string bodyStr = requestBody.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        AddGameHeaders(request, bodyStr, "");

        try
        {
            var response = await _httpClient.SendAsync(request);
            string responseStr = await response.Content.ReadAsStringAsync();
            var result = JsonNode.Parse(responseStr);

            if (result["retcode"]?.GetValue<int>() == 0)
            {
                string qrUrl = result["data"]["url"]?.GetValue<string>();
                var uri = new Uri(qrUrl);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                session.Ticket = query["ticket"];
                return (true, qrUrl, "Success");
            }

            return (false, null, result["message"]?.GetValue<string>());
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task<QrPollOutcome> PollGameLoginStatusAsync(LoginSession session)
    {
        const int MaxConsecutiveErrors = 5;
        const int TimeoutSeconds = 180;
        int consecutiveErrors = 0;
        bool scanned = false;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(session.Cts.Token, timeoutCts.Token);
        CancellationToken ct = linkedCts.Token;

        string url = ApiEndpoints.Hk4eQrCodeQueryUrl;
        int pollInterval = 3000;

        while (!ct.IsCancellationRequested)
        {
            if (!scanned && IsQrAutoRefreshDue(session))
            {
                DebugLog.Info("LoginQrWindow.GameQrLogin", "二维码已超过自动刷新周期，重新创建");
                return QrPollOutcome.Refresh;
            }

            var requestBody = new JsonObject
            {
                ["app_id"] = int.Parse(session.GameAppId),
                ["device"] = session.GameDevice,
                ["ticket"] = session.Ticket
            };
            string bodyStr = requestBody.ToJsonString(_jsonOptions);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            AddGameHeaders(request, bodyStr, "");

            try
            {
                var response = await _httpClient.SendAsync(request, ct);
                string responseStr = await response.Content.ReadAsStringAsync();
                var result = JsonNode.Parse(responseStr);

                int retcode = result["retcode"]?.GetValue<int>() ?? -1;

                if (retcode == 0)
                {
                    string stat = result["data"]["stat"]?.GetValue<string>();
                    if (stat == "Confirmed")
                    {
                        _isLoginCompleting = true;
                        UpdateStatus("LoginQR_GameScanSuccessExchanging".GetLocalized(), true);
                        string raw = result["data"]["payload"]?["raw"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(raw))
                        {
                            UpdateStatus("LoginQR_ScanMissingCredential".GetLocalized(), false);
                            return QrPollOutcome.Stopped;
                        }

                        var rawNode = JsonNode.Parse(raw);
                        string uid = rawNode["uid"]?.GetValue<string>();
                        string token = rawNode["token"]?.GetValue<string>();
                        return await GetSTokenByGameTokenAsync(uid, token, session)
                            ? QrPollOutcome.Confirmed
                            : QrPollOutcome.Stopped;
                    }

                    if (stat == "Scanned")
                    {
                        scanned = true;
                        UpdateStatus("LoginQR_ScannedConfirmPhone".GetLocalized(), true);
                    }

                    consecutiveErrors = 0;
                }
                else if (retcode == -106 || retcode == -3501)
                {
                    DebugLog.Info("LoginQrWindow.GameQrLogin", $"二维码已失效或过期: {retcode}");
                    return _isLoginCompleting ? QrPollOutcome.Stopped : QrPollOutcome.Refresh;
                }
                else
                {
                    consecutiveErrors++;
                    DebugLog.Info("LoginQrWindow.GameQrLogin", $"Game QR polling returned error code: {retcode}");
                }
            }
            catch (OperationCanceledException)
            {
                return timeoutCts.IsCancellationRequested ? QrPollOutcome.Refresh : QrPollOutcome.Stopped;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                DebugLog.Info("LoginQrWindow.GameQrLogin", $"Game polling exception: {ex.Message}");
            }

            if (consecutiveErrors >= MaxConsecutiveErrors)
            {
                UpdateStatus("LoginQR_TooManyNetworkErrors".GetLocalized(), false);
                return QrPollOutcome.Stopped;
            }

            try
            {
                await Task.Delay(pollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return timeoutCts.IsCancellationRequested ? QrPollOutcome.Refresh : QrPollOutcome.Stopped;
            }
        }

        return timeoutCts.IsCancellationRequested ? QrPollOutcome.Refresh : QrPollOutcome.Stopped;
    }

    private async Task<bool> GetSTokenByGameTokenAsync(string accountId, string gameToken, LoginSession session)
    {
        string url = ApiEndpoints.GetTokenByGameTokenUrl;
        var requestBody = new JsonObject
        {
            ["account_id"] = int.Parse(accountId),
            ["game_token"] = gameToken
        };
        string bodyStr = requestBody.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.71.1");
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-sys_version", "12");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _gameDeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", "Xiaomi MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", "bll8iq97cem8");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "4");
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/4.9.3");
        request.Headers.TryAddWithoutValidation("DS", GenerateGameDS2(bodyStr, ""));

        try
        {
            var response = await _httpClient.SendAsync(request);
            string responseStr = await response.Content.ReadAsStringAsync();
            var result = JsonNode.Parse(responseStr);

            if (result["retcode"]?.GetValue<int>() == 0)
            {
                string stoken = result["data"]["token"]?["token"]?.GetValue<string>();
                string mid = result["data"]["user_info"]?["mid"]?.GetValue<string>();

                if (!string.IsNullOrEmpty(stoken) && !string.IsNullOrEmpty(mid))
                {
                    var cookies = await ExchangeV2TokensAsync(stoken, mid, accountId, token: session.Cts.Token);
                    if (cookies == null || !IsSessionActive(session)) return false;

                    OnLoginSuccess(cookies, "cn");
                    return true;
                }
            }

            UpdateStatus(string.Format("LoginQR_StokenExchangeFailed_Format".GetLocalized(), result["message"]?.GetValue<string>()), false);
        }
        catch (Exception ex)
        {
            UpdateStatus(string.Format("LoginQR_StokenExchangeException_Format".GetLocalized(), ex.Message), false);
        }

        return false;
    }

    private void AddGameHeaders(HttpRequestMessage request, string body, string query)
    {
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.71.1");
        request.Headers.TryAddWithoutValidation("x-rpc-aigis", "");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-sys_version", "12");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _gameDeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", "Xiaomi MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "3");
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/4.9.3");
    }

    private string GenerateGameDS2(string body, string query)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = new Random().Next(100001, 200000).ToString();
        string b = string.IsNullOrEmpty(body) ? "" : body;
        string q = string.IsNullOrEmpty(query) ? "" : query;
        string signStr = $"salt={SaltGame}&t={t}&r={r}&b={b}&q={q}";
        string sign = CreateMD5(signStr);
        return $"{t},{r},{sign}";
    }

    #endregion
}
