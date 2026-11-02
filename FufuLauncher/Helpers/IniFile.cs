/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using FufuLauncher.Services;

namespace FufuLauncher.Helpers;

public class IniFile
{
    private readonly string _path;

    public IniFile(string path)
    {
        _path = path;
    }

    public Dictionary<string, Dictionary<string, string>> ReadAll(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path)) return result;

        var currentSection = string.Empty;
        var lines = File.ReadLines(_path, Encoding.UTF8);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";"))
                continue;

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (!result.ContainsKey(currentSection))
                {
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex > 0 && !string.IsNullOrEmpty(currentSection))
            {
                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                result[currentSection][key] = value;
            }
        }

        return result;
    }

    public void WriteValue(string section, string key, string value)
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException($"{_path}");
        }

        var lines = new List<string>(File.ReadAllLines(_path, Encoding.UTF8));
        UpdateLinesForKeyValue(lines, section, key, value);
        SaveToFile(lines);
    }

    public void UpdateMultiple(Dictionary<string, Dictionary<string, string>> updates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException($"Configuration file was not found: {_path}");
        }

        var pending = updates.ToDictionary(section => section.Key,
            section => new Dictionary<string, string>(section.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        Dictionary<string, string>? currentUpdates = null;

        void AppendMissingValues()
        {
            if (currentUpdates == null)
            {
                return;
            }

            foreach (var value in currentUpdates)
            {
                output.Add($"{value.Key} = {value.Value}");
            }

            currentUpdates.Clear();
        }

        foreach (var line in File.ReadLines(_path, Encoding.UTF8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                AppendMissingValues();
                pending.TryGetValue(trimmed[1..^1].Trim(), out currentUpdates);
                output.Add(line);
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (currentUpdates != null && separator >= 0 &&
                currentUpdates.Remove(trimmed[..separator].Trim(), out var value))
            {
                output.Add($"{trimmed[..separator].Trim()} = {value}");
            }
            else
            {
                output.Add(line);
            }
        }

        AppendMissingValues();
        cancellationToken.ThrowIfCancellationRequested();
        SaveToFile(output);
    }

    private void UpdateLinesForKeyValue(List<string> lines, string section, string key, string value)
    {
        var inTargetSection = false;
        var keyFound = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                var currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (inTargetSection)
                {
                    lines.Insert(i, $"{key} = {value}");
                    keyFound = true;
                    break;
                }

                inTargetSection = currentSection.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inTargetSection)
            {
                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex > 0)
                {
                    var currentKey = trimmed.Substring(0, separatorIndex).Trim();
                    if (currentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key} = {value}";
                        keyFound = true;
                        break;
                    }
                }
            }
        }

        if (inTargetSection && !keyFound)
        {
            lines.Add($"{key} = {value}");
        }
    }

    private void SaveToFile(List<string> lines)
    {
        try
        {
            if (PluginConfigGuard.TryRepairLines(_path, lines, out var repairedLines))
            {
                lines = repairedLines;
            }

            File.WriteAllLines(_path, lines, Encoding.UTF8);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("IniFile_AccessDenied".GetLocalized());
        }
        catch (IOException ex)
        {
            throw new IOException(string.Format("IniFile_WriteFailed".GetLocalized(), ex.Message));
        }
        catch (Exception ex)
        {
            throw new Exception(string.Format("IniFile_WriteError".GetLocalized(), ex.Message));
        }
    }
}