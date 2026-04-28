using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;

namespace FufuLauncher.Services;

public class HashValidationService
{
    public static async Task ValidateFilesAsync()
    {
        try
        {
            string baseDirectory = AppContext.BaseDirectory;
            string hashFilePath = Path.Combine(baseDirectory, "Assets", "Launcher" , "hash.txt");

            if (!File.Exists(hashFilePath))
            {
                SendNotification("校验失败", "找不到校验文件，请检查客户端完整性", NotificationType.Error);
                return;
            }

            string[] hashLines = await File.ReadAllLinesAsync(hashFilePath);
            if (hashLines.Length < 3)
            {
                SendNotification("校验失败", "格式错误", NotificationType.Error);
                return;
            }

            string expectedCaptureAppHash = hashLines[1].Trim();
            string expectedLauncherHash = hashLines[2].Trim();

            string captureAppPath = Path.Combine(baseDirectory, "CaptureApp.exe");
            string launcherPath = Path.Combine(baseDirectory, "Launcher.dll");

            bool captureAppValid = await VerifyFileHashAsync(captureAppPath, expectedCaptureAppHash);
            bool launcherValid = await VerifyFileHashAsync(launcherPath, expectedLauncherHash);

            if (!captureAppValid || !launcherValid)
            {
                string errorMessage = "发现组件被修改或缺失，请检查：\n";
                if (!captureAppValid) errorMessage += "CaptureApp\n";
                if (!launcherValid) errorMessage += "Launcher\n";

                SendNotification("校验未通过", errorMessage.TrimEnd(), NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            SendNotification("校验组件异常", $"执行哈希校验时发生错误: {ex.Message}", NotificationType.Error);
        }
    }

    private static async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash)
    {
        if (!File.Exists(filePath)) return false;

        using var sha512 = SHA512.Create();
        using var stream = File.OpenRead(filePath);
        
        byte[] hashBytes = await sha512.ComputeHashAsync(stream);
        string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static void SendNotification(string title, string message, NotificationType type)
    {
        WeakReferenceMessenger.Default.Send(new NotificationMessage(title, message, type, 8000));
    }
}