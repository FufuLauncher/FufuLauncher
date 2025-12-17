namespace FufuLauncher.Models;

public class ControlPanelConfig
{
    public bool EnableFpsOverride { get; set; }
    public int TargetFps { get; set; } = 60;
    public bool EnableFovOverride { get; set; }
    public float TargetFov { get; set; } = 45.0f;
    public bool EnableFogOverride { get; set; }
    public bool EnablePerspectiveOverride { get; set; }
    public bool EnableSyncCountOverride { get; set; }
    
    public Dictionary<string, long> GamePlayTimeData { get; set; } = new();
    public string LastPlayDate { get; set; } = string.Empty;
}