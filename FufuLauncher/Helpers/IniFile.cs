/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text;

namespace FufuLauncher.Helpers;

public class IniFile
{
    private readonly string _path;

    public IniFile(string path)
    {
        _path = path;
    }

    public Dictionary<string, Dictionary<string, string>> ReadAll()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path)) return result;

        var currentSection = string.Empty;
        var lines = File.ReadAllLines(_path, Encoding.UTF8);

        foreach (var line in lines)
        {
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
            throw new FileNotFoundException($"无法保存配置，未找到目标文件: {_path}");
        }

        var lines = new List<string>(File.ReadAllLines(_path, Encoding.UTF8));
        UpdateLinesForKeyValue(lines, section, key, value);
        SaveToFile(lines);
    }

    public void UpdateMultiple(Dictionary<string, Dictionary<string, string>> updates)
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException($"无法保存配置，未找到目标文件: {_path}");
        }

        var lines = new List<string>(File.ReadAllLines(_path, Encoding.UTF8));

        foreach (var section in updates)
        {
            foreach (var kvp in section.Value)
            {
                UpdateLinesForKeyValue(lines, section.Key, kvp.Key, kvp.Value);
            }
        }

        SaveToFile(lines);
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
