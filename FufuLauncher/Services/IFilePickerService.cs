/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Runtime.InteropServices;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace FufuLauncher.Services
{
    public interface IFilePickerService
    {
        Task<string> PickImageOrVideoAsync();
        Task<string> PickAudioFileAsync();
        Task<string> PickFolderAsync();
    }

    public class FilePickerService : IFilePickerService
    {
        private const string AdminMessage = "当前以管理员权限运行，无法使用文件选择器，请关闭管理员权限后重试";

        private static readonly IReadOnlyList<(string Label, string[] Extensions)> AudioFilters =
            new[] { ("音频文件", new[] { "*.mp3", "*.wav", "*.wma", "*.m4a", "*.flac", "*.aac" }) };

        private static readonly IReadOnlyList<(string Label, string[] Extensions)> ImageOrVideoFilters =
            new[] { ("图片或视频", new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.mp4", "*.webm", "*.mkv", "*.avi", "*.mov" }) };

        public static bool InitializeWithValidWindow(object target, out string? errorMessage, Window? window = null)
        {
            if (SystemEnvironmentHelper.IsRunningAsAdministrator())
            {
                errorMessage = AdminMessage;
                Debug.WriteLine($"[FilePickerService] {errorMessage}");
                return false;
            }

            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                if (hwnd != IntPtr.Zero)
                {
                    WinRT.Interop.InitializeWithWindow.Initialize(target, hwnd);
                    errorMessage = null;
                    return true;
                }
            }

            var mainWindow = App.MainWindow;
            if (mainWindow == null)
            {
                errorMessage = "FilePicker_MainWindowNull".GetLocalized();
                Debug.WriteLine($"[FilePickerService] {errorMessage}");
                return false;
            }

            var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            if (mainHwnd == IntPtr.Zero)
            {
                errorMessage = "FilePicker_InvalidWindowHandle".GetLocalized();
                Debug.WriteLine($"[FilePickerService] {errorMessage}");
                return false;
            }

            WinRT.Interop.InitializeWithWindow.Initialize(target, mainHwnd);
            errorMessage = null;
            return true;
        }

        public static async Task<string?> PickOpenFileAsync(
            Window? owner,
            IReadOnlyList<(string Label, string[] Extensions)> filters,
            PickerLocationId? startLocation = null,
            Action<string>? onError = null)
        {
            if (SystemEnvironmentHelper.IsRunningAsAdministrator())
            {
                return await RunOnStaThread(() =>
                {
                    using var dlg = new System.Windows.Forms.OpenFileDialog();
                    ApplyOpenDialogSettings(dlg, filters, startLocation);
                    return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : null;
                }, onError);
            }

            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                if (!InitializeWithValidWindow(picker, out var err, owner))
                {
                    onError?.Invoke(err ?? "无法打开文件选择器");
                    return null;
                }

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                if (startLocation.HasValue)
                    picker.SuggestedStartLocation = startLocation.Value;

                foreach (var (label, exts) in filters)
                    foreach (var ext in exts)
                        picker.FileTypeFilter.Add(ext.StartsWith("*") ? ext : "*" + ext);

                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch (COMException)
            {
                onError?.Invoke(AdminMessage);
                return null;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex.Message);
                return null;
            }
        }

        public static async Task<string?> PickSaveFileAsync(
            Window? owner,
            IReadOnlyList<(string Label, string[] Extensions)> filters,
            string defaultFileName,
            PickerLocationId? startLocation = null,
            Action<string>? onError = null)
        {
            if (SystemEnvironmentHelper.IsRunningAsAdministrator())
            {
                return await RunOnStaThread(() =>
                {
                    using var dlg = new System.Windows.Forms.SaveFileDialog();
                    ApplySaveDialogSettings(dlg, filters, defaultFileName, startLocation);
                    return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : null;
                }, onError);
            }

            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                if (!InitializeWithValidWindow(picker, out var err, owner))
                {
                    onError?.Invoke(err ?? "无法打开文件选择器");
                    return null;
                }

                if (startLocation.HasValue)
                    picker.SuggestedStartLocation = startLocation.Value;

                foreach (var (label, exts) in filters)
                {
                    var cleanExts = exts.Select(e => e.StartsWith("*") ? e : "*" + e).ToList();
                    picker.FileTypeChoices.Add(label, cleanExts);
                }

                if (!string.IsNullOrEmpty(defaultFileName))
                    picker.SuggestedFileName = defaultFileName;

                var file = await picker.PickSaveFileAsync();
                return file?.Path;
            }
            catch (COMException)
            {
                onError?.Invoke(AdminMessage);
                return null;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex.Message);
                return null;
            }
        }

        public static async Task<string?> PickFolderAsync(
            Window? owner,
            PickerLocationId? startLocation = null,
            Action<string>? onError = null)
        {
            if (SystemEnvironmentHelper.IsRunningAsAdministrator())
            {
                return await RunOnStaThread(() =>
                {
                    using var dlg = new System.Windows.Forms.FolderBrowserDialog
                    {
                        ShowNewFolderButton = true
                    };
                    var initialPath = MapStartLocationPath(startLocation);
                    if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                        dlg.InitialDirectory = initialPath;
                    return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
                }, onError);
            }

            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                if (!InitializeWithValidWindow(picker, out var err, owner))
                {
                    onError?.Invoke(err ?? "无法打开文件选择器");
                    return null;
                }

                if (startLocation.HasValue)
                    picker.SuggestedStartLocation = startLocation.Value;
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch (COMException)
            {
                onError?.Invoke(AdminMessage);
                return null;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex.Message);
                return null;
            }
        }

        private static async Task<T?> RunOnStaThread<T>(Func<T?> func, Action<string>? onError)
        {
            var tcs = new TaskCompletionSource<T?>();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FilePickerService] WinForms 对话框失败: {ex}");
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            try
            {
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"文件选择器调用失败：{ex.Message}");
                return default;
            }
        }

        private static void ApplyOpenDialogSettings(System.Windows.Forms.OpenFileDialog dlg,
            IReadOnlyList<(string Label, string[] Extensions)> filters, PickerLocationId? startLocation)
        {
            dlg.Filter = BuildWinFormsFilter(filters);
            if (string.IsNullOrEmpty(dlg.Filter))
                dlg.Filter = "所有文件|*.*";
            dlg.Multiselect = false;
            var initialPath = MapStartLocationPath(startLocation);
            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                dlg.InitialDirectory = initialPath;
        }

        private static void ApplySaveDialogSettings(System.Windows.Forms.SaveFileDialog dlg,
            IReadOnlyList<(string Label, string[] Extensions)> filters, string defaultFileName, PickerLocationId? startLocation)
        {
            dlg.Filter = BuildWinFormsFilter(filters);
            if (string.IsNullOrEmpty(dlg.Filter))
                dlg.Filter = "所有文件|*.*";
            if (!string.IsNullOrEmpty(defaultFileName))
                dlg.FileName = defaultFileName;
            var initialPath = MapStartLocationPath(startLocation);
            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                dlg.InitialDirectory = initialPath;
        }

        private static string BuildWinFormsFilter(IReadOnlyList<(string Label, string[] Extensions)> filters)
        {
            if (filters == null || filters.Count == 0) return "";
            var parts = new List<string>();
            foreach (var (label, exts) in filters)
            {
                var spec = string.Join(";", exts.Select(e => e.StartsWith("*") ? e : "*" + e));
                parts.Add($"{label}|{spec}");
            }
            parts.Add($"所有文件|*.*");
            return string.Join("|", parts);
        }

        private static string? MapStartLocationPath(PickerLocationId? startLocation)
        {
            if (!startLocation.HasValue) return null;
            return startLocation.Value switch
            {
                PickerLocationId.DocumentsLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                PickerLocationId.PicturesLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                PickerLocationId.MusicLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                PickerLocationId.VideosLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                PickerLocationId.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                _ => null,
            };
        }

        public async Task<string> PickAudioFileAsync()
            => await PickOpenFileAsync(null, AudioFilters, PickerLocationId.MusicLibrary, onError: null)
               ?? string.Empty;

        public async Task<string> PickImageOrVideoAsync()
            => await PickOpenFileAsync(null, ImageOrVideoFilters, PickerLocationId.PicturesLibrary, onError: null)
               ?? string.Empty;

        public async Task<string> PickFolderAsync()
            => await PickFolderAsync(null, PickerLocationId.PicturesLibrary, onError: null)
               ?? string.Empty;
    }
}
