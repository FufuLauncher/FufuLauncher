/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using Microsoft.UI.Xaml;

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
        public static IntPtr GetValidWindowHandle(Window window = null)
        {
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                if (hwnd != IntPtr.Zero) return hwnd;
            }

            var mainWindow = App.MainWindow;
            if (mainWindow != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
                if (hwnd != IntPtr.Zero) return hwnd;
            }

            return IntPtr.Zero;
        }

        public static bool InitializeWithValidWindow(object target, Window window = null)
        {
            var hwnd = GetValidWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("[FilePickerService] 无法获取有效窗口句柄");
                return false;
            }
            WinRT.Interop.InitializeWithWindow.Initialize(target, hwnd);
            return true;
        }

        public async Task<string> PickAudioFileAsync()
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                if (!InitializeWithValidWindow(picker)) return null;

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;

                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".wma");
                picker.FileTypeFilter.Add(".m4a");
                picker.FileTypeFilter.Add(".flac");
                picker.FileTypeFilter.Add(".aac");

                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择音频文件失败: {ex.Message}");
                return null;
            }
        }
        public async Task<string> PickImageOrVideoAsync()
        {
            try
            {
                var filePicker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
                };

                string[] fileTypes = { ".jpg", ".jpeg", ".png", ".bmp", ".mp4", ".webm", ".mkv", ".avi", ".mov" };
                foreach (var type in fileTypes)
                {
                    filePicker.FileTypeFilter.Add(type);
                }

                if (!InitializeWithValidWindow(filePicker)) return null;

                var file = await filePicker.PickSingleFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"文件选择失败: {ex.Message}");
                return null;
            }
        }
        public async Task<string> PickFolderAsync()
        {
            try
            {
                var folderPicker = new Windows.Storage.Pickers.FolderPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
                };
                folderPicker.FileTypeFilter.Add("*");

                if (!InitializeWithValidWindow(folderPicker)) return null;

                var folder = await folderPicker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"文件夹选择失败: {ex.Message}");
                return null;
            }
        }
    }
}
