/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.MiHoYo.Passport;

internal static class PassportHttpUtil
{
    public static string? GetSingleHeader(HttpResponseMessage response, string headerName)
    {
        return response.Headers.TryGetValues(headerName, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
    }

    public static async Task<PassportResponse<TData>> DeserializeAsync<TData>(HttpResponseMessage response,
        CancellationToken token)
    {
        string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<PassportResponse<TData>>(json)
                   ?? CreateFailure<TData>("Passport_ParseResponseFailed".GetLocalized());
        }
        catch (JsonException ex)
        {
            DebugLog.Info("PassportHttpUtil", $"[Passport] Failed to parse response: {ex.Message}");
            return CreateFailure<TData>(string.Format("Passport_ParseResponseFailedDetail_Format".GetLocalized(), ex.Message));
        }
    }

    public static async Task<PassportResponse> DeserializeAsync(HttpResponseMessage response, CancellationToken token)
    {
        string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<PassportResponse>(json) ?? CreateFailure("Passport_ParseResponseFailed".GetLocalized());
        }
        catch (JsonException ex)
        {
            DebugLog.Info("PassportHttpUtil", $"[Passport] Failed to parse response: {ex.Message}");
            return CreateFailure(string.Format("Passport_ParseResponseFailedDetail_Format".GetLocalized(), ex.Message));
        }
    }

    private static PassportResponse<TData> CreateFailure<TData>(string message) => new() { Message = message };

    private static PassportResponse CreateFailure(string message) => new() { Message = message };
}