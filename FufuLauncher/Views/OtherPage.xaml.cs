using System.Diagnostics;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FufuLauncher.Views
{
    public sealed partial class OtherPage : Page
    {
        public OtherViewModel ViewModel
        {
            get;
        }

        public OtherPage()
        {
            ViewModel = App.GetService<OtherViewModel>();
            InitializeComponent();

            try
            {
                if (App.MainWindow?.Content is UIElement content)
                {
                    content.KeyDown -= GlobalKeyDown;
                    content.KeyDown += GlobalKeyDown;
                    Debug.WriteLine("[OtherPage] 全局按键事件已注册到Window.Content");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OtherPage] 注册按键事件失败: {ex.Message}");
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox) textBox.Text = textBox.Text.Trim('"');
        }

        private void ProgramPath_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                _ = ViewModel.ApplyProgramPathCommand.ExecuteAsync(null);
            }
        }

        private void GlobalKeyDown(object sender, KeyRoutedEventArgs args)
        {
            try
            {
                if (ViewModel.IsRecordingTriggerKey || ViewModel.IsRecordingClickKey)
                {
                    var key = args.Key;
                    Debug.WriteLine($"[OtherPage] 捕获按键: {key}");

                    if (key == VirtualKey.None) return;

                    if (ViewModel.IsRecordingTriggerKey)
                    {
                        ViewModel.UpdateKey("Trigger", key);
                        Debug.WriteLine($"[OtherPage] 触发键设置完成: {key}");
                    }
                    else if (ViewModel.IsRecordingClickKey)
                    {
                        ViewModel.UpdateKey("Click", key);
                        Debug.WriteLine($"[OtherPage] 连点键设置完成: {key}");
                    }
                    args.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OtherPage] 按键处理异常: {ex.Message}");
            }
        }
    }
}