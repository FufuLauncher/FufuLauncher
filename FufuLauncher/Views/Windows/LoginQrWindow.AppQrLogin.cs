/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 米游社APP扫码登录

    private async Task StartAppLoginFlowAsync(LoginSession session)
    {
        if (session.FlowStarted) return;

        session.FlowStarted = true;
        _isLoginCompleting = false;

        for (int refreshCount = 0;; refreshCount++)
        {
            if (!IsSessionActive(session)) return;

            if (refreshCount == 0)
                UpdateStatus("正在创建APP登录二维码...", true);

            var qrResult = await CreateAppQrCodeAsync(session);
            if (!qrResult.Success)
            {
                UpdateStatus($"创建失败: {qrResult.Message}", false);
                return;
            }

            if (!IsSessionActive(session)) return;

            session.QrCreatedAt = DateTimeOffset.UtcNow;
            RenderQrCode(qrResult.Url);
            UpdateStatus("请使用米游社APP扫描二维码", false, true);
            SetQrHint(refreshCount == 0 ? "LoginQR_AutoRefreshHint" : "LoginQR_AutoRefreshed");

            var outcome = await PollAppLoginStatusAsync(session);
            if (outcome != QrPollOutcome.Refresh || !IsSessionActive(session)) return;

            if (refreshCount >= MaxQrAutoRefreshCount)
            {
                DebugLog.Info("LoginQrWindow.AppQrLogin", "二维码自动刷新次数已达上限");
                SetQrHint("LoginQR_AutoRefreshPaused");
                UpdateStatus("二维码已过期且自动刷新次数已达上限，请点击刷新二维码", false);
                return;
            }

            DebugLog.Info("LoginQrWindow.AppQrLogin",
                $"二维码已过期，自动刷新 ({refreshCount + 1}/{MaxQrAutoRefreshCount})");
        }
    }

    private async Task<(bool Success, string Url, string Message)> CreateAppQrCodeAsync(LoginSession session)
    {
        string url = ApiEndpoints.PassportAppCreateQrLoginUrl;
        var body = new JsonObject();
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        AddCommonHeaders(request, bodyStr, "", ClientTypes.GameApp, AppIds.Passport, HeaderVersions.PcWebCn);

        try
        {
            var response = await _httpClient.SendAsync(request);
            string responseStr = await response.Content.ReadAsStringAsync();
            var result = JsonNode.Parse(responseStr);

            if (result["retcode"]?.GetValue<int>() == 0)
            {
                string qrUrl = result["data"]["url"]?.GetValue<string>();
                session.Ticket = result["data"]["ticket"]?.GetValue<string>();
                return (true, qrUrl, "Success");
            }

            return (false, null, result["message"]?.GetValue<string>());
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task<QrPollOutcome> PollAppLoginStatusAsync(LoginSession session)
    {
        const int MaxConsecutiveErrors = 5;
        const int TimeoutSeconds = 180;
        int consecutiveErrors = 0;
        bool scanned = false;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(session.Cts.Token, timeoutCts.Token);
        CancellationToken ct = linkedCts.Token;

        string url = ApiEndpoints.PassportAppQueryQrLoginStatusUrl;
        int pollInterval = 3000;
        JsonNode confirmedData = null;

        while (!ct.IsCancellationRequested)
        {
            if (!scanned && IsQrAutoRefreshDue(session))
            {
                DebugLog.Info("LoginQrWindow.AppQrLogin", "二维码已超过自动刷新周期，重新创建");
                return QrPollOutcome.Refresh;
            }

            var body = new JsonObject { ["ticket"] = session.Ticket };
            string bodyStr = body.ToJsonString(_jsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            AddCommonHeaders(request, bodyStr, "", ClientTypes.GameApp, AppIds.Passport, HeaderVersions.PcWebCn);

            try
            {
                using var response = await _httpClient.SendAsync(request, ct);
                string responseStr = await response.Content.ReadAsStringAsync();
                var result = JsonNode.Parse(responseStr);

                int retcode = result?["retcode"]?.GetValue<int>() ?? -1;

                if (retcode == -3501 || retcode == -106)
                {
                    DebugLog.Info("LoginQrWindow.AppQrLogin", $"二维码已失效或过期: {retcode}");
                    return _isLoginCompleting ? QrPollOutcome.Stopped : QrPollOutcome.Refresh;
                }

                if (retcode == 0)
                {
                    string status = result["data"]["status"]?.GetValue<string>();
                    if (status?.ToLower() == "confirmed")
                    {
                        _isLoginCompleting = true;
                        UpdateStatus("APP扫码成功，正在换取...", true);
                        confirmedData = result["data"];
                        break;
                    }

                    if (status?.ToLower() == "scanned")
                    {
                        scanned = true;
                        UpdateStatus("已扫码，请在手机端确认登录...", true);
                    }

                    consecutiveErrors = 0;
                }
                else
                {
                    consecutiveErrors++;
                    DebugLog.Info("LoginQrWindow.AppQrLogin", $"Unexpected poll return code: {retcode}");
                }
            }
            catch (OperationCanceledException)
            {
                return timeoutCts.IsCancellationRequested ? QrPollOutcome.Refresh : QrPollOutcome.Stopped;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                DebugLog.Info("LoginQrWindow.AppQrLogin", $"Polling exception: {ex.Message}");
            }

            if (consecutiveErrors >= MaxConsecutiveErrors)
            {
                UpdateStatus("网络异常次数过多，请检查网络后重试", false);
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

        if (confirmedData != null)
        {
            await ProcessAndExchangeV2TokensAsync(confirmedData);
            return QrPollOutcome.Confirmed;
        }

        return timeoutCts.IsCancellationRequested ? QrPollOutcome.Refresh : QrPollOutcome.Stopped;
    }

    #endregion
}
