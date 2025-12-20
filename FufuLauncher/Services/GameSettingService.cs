using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace FufuLauncher.Services;

/// <summary>
/// 游戏设置服务（参考Starward实现）
/// </summary>
public static class GameSettingService
{
    // HDR注册表键名（与Starward保持一致）
    private const string WINDOWS_HDR_ON = "WINDOWS_HDR_ON_h3132281285";
    private const string GENERAL_DATA = "GENERAL_DATA_h2389025596";

    /// <summary>
    /// 获取原神HDR开关状态
    /// </summary>
    public static bool GetGenshinEnableHDR()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\原神", false);
            var val = key?.GetValue(WINDOWS_HDR_ON);
            if (val is int intVal) return intVal == 1;
            if (val != null && int.TryParse(val.ToString(), out int parsedVal)) return parsedVal == 1;
            return false; // 默认关闭
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ [GameSettingService.GetGenshinEnableHDR] 读取失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 设置原神HDR开关状态
    /// </summary>
    public static void SetGenshinEnableHDR(bool enable)
    {
        try
        {
            Debug.WriteLine($"📝 [GameSettingService.SetGenshinEnableHDR] 设置HDR: {enable}");

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\原神", true);
            if (key == null)
            {
                // 创建键
                using var newKey = Registry.CurrentUser.CreateSubKey(@"Software\miHoYo\原神");
                newKey?.SetValue(WINDOWS_HDR_ON, enable ? 1 : 0, RegistryValueKind.DWord);
                Debug.WriteLine($"✅ [GameSettingService.SetGenshinEnableHDR] 创建新键并设置: {enable}");
                return;
            }

            key.SetValue(WINDOWS_HDR_ON, enable ? 1 : 0, RegistryValueKind.DWord);
            Debug.WriteLine($"✅ [GameSettingService.SetGenshinEnableHDR] 设置成功: {enable}");
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine($"🔐 [GameSettingService.SetGenshinEnableHDR] 权限不足！");
            throw new Exception("没有权限写入注册表，请尝试以管理员身份运行程序");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ [GameSettingService.SetGenshinEnableHDR] 设置失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 获取原神HDR亮度设置
    /// </summary>
    public static (int MaxLuminance, int SceneLuminance, int UILuminance) GetGenshinHDRLuminance()
    {
        int max = 1000, scene = 300, ui = 350; // 默认值

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\原神", false);
            var data = key?.GetValue(GENERAL_DATA) as byte[];

            if (data != null)
            {
                var str = Encoding.UTF8.GetString(data).TrimEnd('\0');
                var node = JsonNode.Parse(str);
                if (node != null)
                {
                    max = (int)(node["maxLuminosity"]?.GetValue<float>() ?? 1000);
                    scene = (int)(node["scenePaperWhite"]?.GetValue<float>() ?? 300);
                    ui = (int)(node["uiPaperWhite"]?.GetValue<float>() ?? 350);

                    Debug.WriteLine($"📊 [GameSettingService.GetGenshinHDRLuminance] 读取亮度: Max={max}, Scene={scene}, UI={ui}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ [GameSettingService.GetGenshinHDRLuminance] 读取失败: {ex.Message}");
        }

        // 参数范围验证
        max = Math.Clamp(max, 300, 2000);
        scene = Math.Clamp(scene, 100, 500);
        ui = Math.Clamp(ui, 150, 550);

        return (max, scene, ui);
    }

    /// <summary>
    /// 设置原神HDR亮度参数
    /// </summary>
    public static void SetGenshinHDRLuminance(int maxLuminance, int sceneLuminance, int uiLuminance)
    {
        // 参数范围验证（与Starward保持一致）
        maxLuminance = Math.Clamp(maxLuminance, 300, 2000);
        sceneLuminance = Math.Clamp(sceneLuminance, 100, 500);
        uiLuminance = Math.Clamp(uiLuminance, 150, 550);

        try
        {
            Debug.WriteLine($"📝 [GameSettingService.SetGenshinHDRLuminance] 设置亮度: Max={maxLuminance}, Scene={sceneLuminance}, UI={uiLuminance}");

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\原神", true);
            var data = key?.GetValue(GENERAL_DATA) as byte[];

            JsonNode? node = null;
            if (data != null)
            {
                var str = Encoding.UTF8.GetString(data).TrimEnd('\0');
                node = JsonNode.Parse(str);
            }
            else
            {
                node = new JsonObject();
            }

            node["maxLuminosity"] = maxLuminance;
            node["scenePaperWhite"] = sceneLuminance;
            node["uiPaperWhite"] = uiLuminance;

            var value = node.ToJsonString() + "\0";
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\miHoYo\原神", GENERAL_DATA, Encoding.UTF8.GetBytes(value));

            Debug.WriteLine($"✅ [GameSettingService.SetGenshinHDRLuminance] 设置成功");
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine($"🔐 [GameSettingService.SetGenshinHDRLuminance] 权限不足！");
            throw new Exception("没有权限写入注册表，请尝试以管理员身份运行程序");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ [GameSettingService.SetGenshinHDRLuminance] 设置失败: {ex.Message}");
            throw;
        }
    }
}