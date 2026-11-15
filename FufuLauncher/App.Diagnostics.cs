/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FufuLauncher.Helpers;

namespace FufuLauncher;

public partial class App
{
    #region Crash Handling

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    private void ShowCrashDialog(string source, Exception? ex)
    {
        if (ex == null) return;

        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).Wait();

        var message = string.Format("Crash_Message".GetLocalized(), source, ex.Message, ex.StackTrace);

        MessageBox(IntPtr.Zero, message, "Crash_Title".GetLocalized(), MB_OK | MB_ICONERROR);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();

        var baseEx = e.Exception?.GetBaseException();
        if (baseEx is SocketException
            || baseEx is ObjectDisposedException
            || baseEx is OperationCanceledException)
        {
            DebugLog.Info("App.Diagnostics",
                $"[UnobservedTask] Ignored background exception: {baseEx.GetType().Name}: {baseEx.Message}");
            return;
        }

        LogException(e.Exception, "UnobservedTaskException");
        ShowCrashDialog("Crash_Source_UnobservedTask".GetLocalized(), e.Exception);
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        LogException(e.Exception, "App_UnhandledException");

        if (e.Exception is InvalidCastException ||
            (e.Exception is COMException comEx && comEx.HResult == unchecked((int)0x80000019)))
        {
            DebugLog.Info("App.Diagnostics", $"Suppressed non-fatal exception: {e.Exception.Message}");
            return;
        }

        ShowCrashDialog("Crash_Source_UiUnhandled".GetLocalized(), e.Exception);
        Environment.Exit(-1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogException(ex, "CurrentDomain_UnhandledException");
        ShowCrashDialog("Crash_Source_DomainFatal".GetLocalized(), ex);
        Environment.Exit(-1);
    }

    private void LogException(Exception? ex, string source)
    {
        if (ex == null) return;

        try
        {
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("source", source);
            });

            var logPath = Path.Combine(AppPaths.RootDir, "CrashLog.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n" +
                      $"Exception: {ex.GetType().Name}\n" +
                      $"Message: {ex.Message}\n" +
                      $"StackTrace: {ex.StackTrace}\n" +
                      new string('-', 80) + "\n";

            File.AppendAllText(logPath, log);
        }
        catch
        {
        }
    }

    #endregion
}