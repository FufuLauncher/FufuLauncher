using Microsoft.Win32;
using System;
using System.Text;
using System.Text.Json.Nodes;

namespace FufuLauncher.Services;

internal static class GameSettingService
{
    private const string GENERAL_DATA_h2389025596 = "GENERAL_DATA_h2389025596";
    private const string WINDOWS_HDR_ON_h3132281285 = "WINDOWS_HDR_ON_h3132281285";
    private const string GenshinRegistryPath = @"HKEY_CURRENT_USER\Software\miHoYo\原神";
    
    public static void SetGenshinHDRState(bool isEnabled)
    {
        try
        {
            int value = isEnabled ? 1 : 0;
            Registry.SetValue(GenshinRegistryPath, WINDOWS_HDR_ON_h3132281285, value, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameSettingService] 设置HDR注册表失败: {ex.Message}");
        }
    }
    
    public static bool GetGenshinHDRState()
    {
        try
        {
            var value = Registry.GetValue(GenshinRegistryPath, WINDOWS_HDR_ON_h3132281285, 0);
            if (value is int intValue)
            {
                return intValue == 1;
            }
        }
        catch { }
        return false;
    }

    public static (int MaxLuminance, int SceneLuminance, int UILuminance) GetGenshinHDRLuminance()
    {
        int max = 1000, scene = 300, ui = 350;
        try 
        {
            byte[]? data = Registry.GetValue(GenshinRegistryPath, GENERAL_DATA_h2389025596, null) as byte[];
            if (data is not null)
            {
                string str = Encoding.UTF8.GetString(data).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(str)) 
                {
                    JsonNode? node = JsonNode.Parse(str);
                    if (node is not null)
                    {
                        max = (int)(node["maxLuminosity"]?.GetValue<float>() ?? 1000);
                        scene = (int)(node["scenePaperWhite"]?.GetValue<float>() ?? 300);
                        ui = (int)(node["uiPaperWhite"]?.GetValue<float>() ?? 350);
                    }
                }
            }
        }
        catch { }

        max = Math.Clamp(max, 300, 2000);
        scene = Math.Clamp(scene, 100, 500);
        ui = Math.Clamp(ui, 150, 550);
        return (max, scene, ui);
    }

    public static void SetGenshinHDRLuminance(int maxLuminance, int sceneLuminance, int uiLuminance)
    {
        maxLuminance = Math.Clamp(maxLuminance, 300, 2000);
        sceneLuminance = Math.Clamp(sceneLuminance, 100, 500);
        uiLuminance = Math.Clamp(uiLuminance, 150, 550);
        
        byte[]? data = Registry.GetValue(GenshinRegistryPath, GENERAL_DATA_h2389025596, null) as byte[];
        JsonNode? node = null;
        if (data is not null)
        {
            try 
            {
                string str = Encoding.UTF8.GetString(data).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(str))
                {
                    node = JsonNode.Parse(str);
                }
            }
            catch { }
        }

        if (node == null) node = JsonNode.Parse("{}");
        
        node["maxLuminosity"] = maxLuminance;
        node["scenePaperWhite"] = sceneLuminance;
        node["uiPaperWhite"] = uiLuminance;
        
        string value = $"{node.ToJsonString()}\0";
        Registry.SetValue(GenshinRegistryPath, GENERAL_DATA_h2389025596, Encoding.UTF8.GetBytes(value));
    }
}