/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class CloudSyncService
{
    public async Task<CloudApiResult<CloudQuotaData>> GetQuotaAsync()
    {
        var result =
            await SendAuthenticatedJsonAsync<CloudQuotaData>(HttpMethod.Get, ApiEndpoints.CloudSyncDataQuotaPath);
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudActivationResultData>> ActivateCodeAsync(string code)
    {
        var result = await SendAuthenticatedJsonAsync<CloudActivationResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataActivatePath, new
            {
                code = code.Trim()
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudArchiveListData>> ListArchivesAsync(string? typeFilter = null)
    {
        var path = string.IsNullOrWhiteSpace(typeFilter) ||
                   string.Equals(typeFilter, "all", StringComparison.OrdinalIgnoreCase)
            ? ApiEndpoints.CloudSyncDataListPath
            : ApiEndpoints.GetCloudSyncDataListPath(typeFilter);

        var result = await SendAuthenticatedJsonAsync<CloudArchiveListData>(HttpMethod.Get, path);
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudUploadResultData>> UploadArchiveAsync(string dataType, string slot,
        string jsonContent, bool overwrite = true)
    {
        var normalizedType = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedType is not ("uiaf" or "uigf" or "playtime" or "challenge"))
        {
            return new CloudApiResult<CloudUploadResultData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "INVALID_DATA_TYPE",
                Message = "CloudSync_ErrUnsupportedDataType".GetLocalized()
            };
        }

        var trimmedSlot = (slot ?? string.Empty).Trim();
        if (!IsValidSlotName(trimmedSlot))
        {
            return new CloudApiResult<CloudUploadResultData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "INVALID_DATA_SLOT",
                Message = FormatFriendlyError("INVALID_DATA_SLOT", null, 400)
            };
        }

        var raw = await SendAuthenticatedRawAsync(token =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.CloudSyncDataUploadPath);
            req.Headers.Add("X-LToken", token);
            req.Headers.Add("X-Data-Type", normalizedType);
            req.Headers.Add("X-Data-Slot", trimmedSlot);
            req.Headers.Add("X-Overwrite", overwrite ? "true" : "false");
            req.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            return req;
        }, retryOnTransientFailure: overwrite);

        if (raw == null)
        {
            return UnauthorizedResult<CloudUploadResultData>();
        }

        if (raw.IsNetworkError)
        {
            return NetworkErrorResult<CloudUploadResultData>(raw.NetworkErrorMessage);
        }

        var parsed = ParseEnvelope<CloudUploadResultData>(raw.StatusCode, raw.Body, raw.RetryAfter);
        MarkLockedIf423(parsed.HttpStatusCode, parsed.Code);
        return parsed;
    }

    public async Task<CloudPullResult> PullArchiveAsync(string dataType, string slot)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = dataType.Trim().ToLowerInvariant(),
            slot = slot.Trim()
        });

        var raw = await SendAuthenticatedRawAsync(token =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.CloudSyncDataPullPath);
            req.Headers.Add("X-LToken", token);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            return req;
        }, retryOnTransientFailure: true);

        if (raw == null)
        {
            return new CloudPullResult
            {
                Ok = false,
                HttpStatusCode = 403,
                Code = "FORBIDDEN",
                Message = "CloudSync_ErrAuthRequired".GetLocalized()
            };
        }

        if (raw.IsNetworkError)
        {
            return new CloudPullResult
            {
                Ok = false,
                HttpStatusCode = 0,
                Code = "NETWORK_ERROR",
                Message = raw.NetworkErrorMessage ?? string.Empty
            };
        }

        if (raw.IsSuccessStatusCode)
        {
            if (!LooksLikeJsonValueStart(raw.Body, objectOnly: false))
            {
                DebugLog.Info("CloudSyncService",
                    $"[CloudSync] Upstream returned non-JSON archive payload (HTTP {raw.StatusCode}): {SummarizeNonJsonBody(raw.Body)}");
                return new CloudPullResult
                {
                    Ok = false,
                    HttpStatusCode = raw.StatusCode,
                    Code = "INVALID_RESPONSE",
                    Message = "CloudSync_ErrServiceUnavailable".GetLocalized()
                };
            }

            return new CloudPullResult
            {
                Ok = true,
                HttpStatusCode = raw.StatusCode,
                Code = "PULL_SUCCESS",
                DataType = raw.DataType ?? dataType,
                Slot = raw.Slot ?? slot,
                Sha256 = raw.Sha256 ?? string.Empty,
                CompressedBytes = raw.CompressedBytes ?? 0,
                RawBytes = raw.RawBytes ?? Encoding.UTF8.GetByteCount(raw.Body),
                JsonContent = raw.Body
            };
        }

        var errorEnvelope = ParseEnvelope<object>(raw.StatusCode, raw.Body, raw.RetryAfter);
        MarkLockedIf423(errorEnvelope.HttpStatusCode, errorEnvelope.Code);
        return new CloudPullResult
        {
            Ok = false,
            HttpStatusCode = errorEnvelope.HttpStatusCode,
            Code = errorEnvelope.Code,
            Message = errorEnvelope.Message
        };
    }

    public async Task<CloudApiResult<CloudRenameResultData>> RenameArchiveAsync(string dataType, string oldSlot,
        string newSlot, bool overwrite = false)
    {
        var trimmedNew = (newSlot ?? string.Empty).Trim();
        if (!IsValidSlotName(trimmedNew))
        {
            return new CloudApiResult<CloudRenameResultData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "INVALID_DATA_SLOT",
                Message = FormatFriendlyError("INVALID_DATA_SLOT", null, 400)
            };
        }

        var result = await SendAuthenticatedJsonAsync<CloudRenameResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataRenamePath, new
            {
                type = dataType.Trim().ToLowerInvariant(),
                old_slot = oldSlot.Trim(),
                new_slot = trimmedNew,
                overwrite
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudDeleteResultData>> DeleteArchiveAsync(string dataType, string slot)
    {
        var result = await SendAuthenticatedJsonAsync<CloudDeleteResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataDeletePath, new
            {
                type = dataType.Trim().ToLowerInvariant(),
                slot = slot.Trim()
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudDeleteAllResultData>> DeleteAllDataAsync()
    {
        var result = await SendAuthenticatedJsonAsync<CloudDeleteAllResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataDeleteAllPath, new
            {
                confirm = true
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }
}