/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Services.MiHoYo;

namespace FufuLauncher.Services;

public static class CloudCredentialIdentity
{
    public static string CreateProfileName(string accountUid) =>
        "CloudCredential_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountUid)).AsSpan(0, 16));


    public static string? GetAccountUid(IEnumerable<KeyValuePair<string, string>> cookies) =>
        AccountCookieIdentity.GetUid(cookies);
}
