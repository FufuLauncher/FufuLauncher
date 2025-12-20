using System.Diagnostics;

namespace FufuLauncher.Services
{
    public interface IFilePickerService
    {
        Task<string> PickImageOrVideoAsync();
        Task<string> PickAudioFileAsync();
    }

    public class FilePickerService : IFilePickerService
    {
        public async Task<string> PickAudioFileAsync()
        {
            try
            {
                var window = App.MainWindow;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

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

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

                var file = await filePicker.PickSingleFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"文件选择失败: {ex.Message}");
                return null;
            }
        }
    }
}