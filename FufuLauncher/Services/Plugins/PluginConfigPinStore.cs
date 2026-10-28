using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services;

public sealed class PluginConfigPinStore(ILocalSettingsService settings)
{
    private static readonly SemaphoreSlim AccessGate = new(1, 1);

    public async Task<HashSet<string>> ReadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        await AccessGate.WaitAsync(cancellationToken);
        try
        {
            var sections = await ReadSectionsAsync(GetStorageKey(configPath));
            cancellationToken.ThrowIfCancellationRequested();
            return sections;
        }
        finally
        {
            AccessGate.Release();
        }
    }

    public async Task<bool> SetPinnedAsync(string configPath, string section, bool pinned)
    {
        await AccessGate.WaitAsync();
        try
        {
            var key = GetStorageKey(configPath);
            var sections = await ReadSectionsAsync(key);
            if (pinned)
            {
                sections.Add(section);
            }
            else
            {
                sections.Remove(section);
            }

            return await settings.TrySaveSettingAsync(key,
                JsonSerializer.Serialize(sections.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
        }
        finally
        {
            AccessGate.Release();
        }
    }

    private async Task<HashSet<string>> ReadSectionsAsync(string key)
    {
        var stored = await settings.ReadSettingAsync(key);
        string[]? sections = stored == null ? [] : JsonSerializer.Deserialize<string[]>(stored.ToString()!);
        return new HashSet<string>((sections ?? []).Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string GetStorageKey(string configPath)
    {
        var identity = Path.GetFullPath(configPath).ToUpperInvariant();
        return "PluginConfigPinnedItems:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
