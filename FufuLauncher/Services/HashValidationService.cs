/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
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
                SendNotification("HashCheck_Failed".GetLocalized(), "HashCheck_FileNotFound".GetLocalized(), NotificationType.Error);
                return;
            }

            string[] hashLines = await File.ReadAllLinesAsync(hashFilePath);
            if (hashLines.Length < 2)
            {
                SendNotification("HashCheck_Failed".GetLocalized(), "HashCheck_FormatError".GetLocalized(), NotificationType.Error);
                return;
            }

            string expectedCaptureAppHash = hashLines[1].Trim();

            string captureAppPath = Path.Combine(baseDirectory, "CaptureApp.exe");

            bool captureAppValid = await VerifyFileHashAsync(captureAppPath, expectedCaptureAppHash);

            if (!captureAppValid)
            {
                string errorMessage = "HashCheck_ComponentModified".GetLocalized();
                SendNotification("HashCheck_NotPassed".GetLocalized(), errorMessage.TrimEnd(), NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            SendNotification("HashCheck_Exception".GetLocalized(), string.Format("HashCheck_ExceptionMsg".GetLocalized(), ex.Message), NotificationType.Error);
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