namespace FufuLauncher.Models;

public enum PostLaunchBehavior
{
    /// <summary>不执行任何操作</summary>
    None = 0,

    /// <summary>最小化到系统托盘</summary>
    MinimizeToTray = 1,

    /// <summary>保存状态后退出启动器</summary>
    Exit = 2
}
