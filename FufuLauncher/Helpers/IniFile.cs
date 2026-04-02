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

        string currentSection = string.Empty;
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
        bool inTargetSection = false;
        bool keyFound = false;

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
        
        try
        {
            File.WriteAllLines(_path, lines, Encoding.UTF8);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("拒绝访问 请检查文件或文件夹是否被设置为“只读”，或者尝试以管理员身份重新运行启动器");
        }
        catch (IOException ex)
        {
            throw new IOException($"写入失败：文件可能正被游戏或其他程序占用\n反馈: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"写入配置时发生错误\n反馈: {ex.Message}");
        }
    }
}