/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Services;

public static class CloudCredentialIdentity
{
    
    public static string CreateProfileName(string accountUid) =>
        "CloudCredential_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountUid)).AsSpan(0, 16));

    
    
    public static string? GetAccountUid(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var accountUids = cookies
            .Where(cookie => cookie.Key is "account_id_v2" or "account_id" or "ltuid_v2" or "ltuid")
            .Select(cookie => cookie.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return accountUids.Count == 1 ? accountUids[0] : null;
    }
}
