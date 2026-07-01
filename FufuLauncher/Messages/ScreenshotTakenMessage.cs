/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Messages;

public class ScreenshotTakenMessage
{
    public string FilePath { get; }
    public bool Success { get; }
    public string ErrorMessage { get; }

    public ScreenshotTakenMessage(string filePath, bool success, string errorMessage = "")
    {
        FilePath = filePath;
        Success = success;
        ErrorMessage = errorMessage;
    }
}
