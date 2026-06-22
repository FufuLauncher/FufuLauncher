using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class AccountPage : Page
{
    #region 字段
    private bool _isDeleting;
    private bool _hasAnimatedButtons;
    private bool _hasAnimatedRightCards;
    private bool _wasLoggedInOnLoad;
    #endregion

    #region 属性
    public AccountViewModel ViewModel
    {
        get;
    }

    public ControlPanelModel ControlPanelViewModel
    {
        get;
    }
    #endregion

    #region 构造函数
    public AccountPage()
    {
        ViewModel = App.GetService<AccountViewModel>();
        ControlPanelViewModel = App.GetService<ControlPanelModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Debug.WriteLine("AccountPage initialized");
    }
    #endregion

    #region 页面加载与动画
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _wasLoggedInOnLoad = ViewModel.IsLoggedIn;

        await Task.Delay(250);
        if (ViewModel.IsLoggedIn)
        {
            PlayEntranceAnimations();
        }

        await ViewModel.LoadUserInfoAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is AccountViewModel vm)
        {
            await vm.RefreshDataAsync();
        }
    }

    private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.IsLoggedIn))
        {
            if (ViewModel.IsLoggedIn && !_wasLoggedInOnLoad)
            {

                _wasLoggedInOnLoad = true;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
                {
                    await Task.Delay(150);
                    PlayEntranceAnimations();
                });
            }
            else if (!ViewModel.IsLoggedIn)
            {
  
                _hasAnimatedButtons = false;
                _hasAnimatedRightCards = false;
                _wasLoggedInOnLoad = false;
                ResetAnimationState();
            }
        }
    }

    private void ResetAnimationState()
    {
  
        ButtonsStaggerStoryboard?.Stop();
        RightCardsEntranceStoryboard?.Stop();

        // 按钮复位
        BtnSwitchAccount.Opacity = 0; BtnSwitchAccountTransform.Y = -80;
        BtnRefreshInfo.Opacity = 0; BtnRefreshInfoTransform.Y = -80;
        BtnGenshinData.Opacity = 0; BtnGenshinDataTransform.Y = -80;
        BtnGachaAnalysis.Opacity = 0; BtnGachaAnalysisTransform.Y = -80;
        BtnSecurityCenter.Opacity = 0; BtnSecurityCenterTransform.Y = -80;
        BtnLockAccount.Opacity = 0; BtnLockAccountTransform.Y = -80;
        BtnCopyCookie.Opacity = 0; BtnCopyCookieTransform.Y = -80;
        BtnDeleteAccount.Opacity = 0; BtnDeleteAccountTransform.Y = -80;
        BtnLogout.Opacity = 0; BtnLogoutTransform.Y = -80;


        CommunityFeedCard.Opacity = 0; CommunityFeedCardTransform.X = 50;
        BoundRolesCard.Opacity = 0; BoundRolesCardTransform.X = 50;
        GameTimeCard.Opacity = 0; GameTimeCardTransform.X = 50;
    }

    private void PlayEntranceAnimations()
    {
        if (!_hasAnimatedButtons)
        {
            _hasAnimatedButtons = true;
            ButtonsStaggerStoryboard?.Begin();
        }

        if (!_hasAnimatedRightCards)
        {
            _hasAnimatedRightCards = true;
            RightCardsEntranceStoryboard?.Begin();
        }
    }

    private void AvatarPicture_Loaded(object sender, RoutedEventArgs e)
    {
        AvatarEntranceStoryboard.Begin();
    }
    #endregion

    #region 账户切换
    private void OnSwitchAccountClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AccountInfo account)
        {
            ViewModel.SwitchAccountCommand.Execute(account);
        }
    }
    #endregion

    #region 账户删除
    private async void OnDeleteSavedAccountClicked(object sender, RoutedEventArgs e)
    {
        if (_isDeleting) return;
        if (sender is not Button button || button.DataContext is not AccountInfo account)
            return;

        await DeleteAccountWithConfirmationAsync(account, false);
    }

    private async void OnDeleteCurrentAccountClicked(object sender, RoutedEventArgs e)
    {
        if (_isDeleting) return;
        var accountToDelete = ViewModel.CurrentAccount;
        if (accountToDelete == null) return;

        await DeleteAccountWithConfirmationAsync(accountToDelete, true);
    }

    private async Task DeleteAccountWithConfirmationAsync(AccountInfo account, bool isCurrentAccount)
    {
        _isDeleting = true;
        try
        {
            string title = isCurrentAccount ? "删除当前账号" : "删除账号";
            string content = isCurrentAccount
                ? $"确定要删除当前账号 {account.Nickname} ({account.GameUid}) 吗？\n此操作将删除该账号的所有相关数据，且无法恢复。"
                : $"确定要删除账号 {account.Nickname} ({account.GameUid}) 吗？\n\n此操作将删除该账号的所有相关数据，包括凭证、祈愿记录和云游戏凭证，且无法恢复。";

            var result = await ShowDeleteConfirmationDialogAsync(title, content);
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteAccountAsync(account);
                Debug.WriteLine($"[Page] 账号 {account.Nickname} 删除完成");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"删除账号异常: {ex.Message}");
        }
        finally
        {
            _isDeleting = false;
        }
    }

    private async Task<ContentDialogResult> ShowDeleteConfirmationDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        return await dialog.ShowAsync();
    }
    #endregion

    #region 动画
    private void Btn_RipplePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid grid) return;
        var host = grid.Children[0] as Canvas;
        if (host == null || host.ActualWidth <= 0) return;

        var pt = e.GetCurrentPoint(host).Position;
        double w = host.ActualWidth, h = host.ActualHeight;
        double x = pt.X, y = pt.Y;

        double targetR = new[]
        {
            Math.Sqrt(x * x + y * y),
            Math.Sqrt((w - x) * (w - x) + y * y),
            Math.Sqrt(x * x + (h - y) * (h - y)),
            Math.Sqrt((w - x) * (w - x) + (h - y) * (h - y))
        }.Max();

        var ripple = new Ellipse
        {
            Width = targetR * 2,
            Height = targetR * 2,
            Fill = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform { ScaleX = 0, ScaleY = 0 }
        };
        Canvas.SetLeft(ripple, x - targetR);
        Canvas.SetTop(ripple, y - targetR);
        host.Children.Add(ripple);

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleX = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = ease };
        var scaleY = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = ease };
        var fade = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(500) };

        Storyboard.SetTarget(scaleX, ripple);
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTarget(scaleY, ripple);
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        Storyboard.SetTarget(fade, ripple);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var task = ripple;
        sb.Completed += (_, _) => { try { host.Children.Remove(task); } catch { } };
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Children.Add(fade);
        sb.Begin();
    }
    #endregion

    #region 其他 UI 操作
    private void OnGachaAnalysisClicked(object sender, RoutedEventArgs e)
    {
        var window = new GachaAnalysisWindow();
        window.Activate();
    }

    private async void OnCopyGameUidClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string uid && !string.IsNullOrEmpty(uid))
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(uid);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            var icon = button.Content as FontIcon;
            if (icon != null)
            {
                var originalGlyph = icon.Glyph;
                icon.Glyph = "";
                await Task.Delay(800);
                icon.Glyph = originalGlyph;
            }
        }
    }
    #endregion
}
