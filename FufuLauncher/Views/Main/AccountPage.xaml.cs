/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
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
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class AccountPage : Page
{
    #region 字段
    private readonly IUnifiedCheckinService _unifiedCheckinService;
    private readonly INotificationService _notificationService;
    private bool _isDeleting;
    private bool _hasAnimatedButtons;
    private bool _hasAnimatedProfileCard;
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
        _unifiedCheckinService = App.GetService<IUnifiedCheckinService>();
        _notificationService = App.GetService<INotificationService>();
        DataContext = ViewModel;
        InitializeComponent();
        RegisterRippleHandlers();
        Debug.WriteLine("AccountPage initialized");
    }
    #endregion

    #region 页面加载与动画
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Cleanup();
        this.SizeChanged -= OnPageSizeChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _wasLoggedInOnLoad = ViewModel.IsLoggedIn;

      
        this.SizeChanged += OnPageSizeChanged;

        await Task.Delay(250);
        if (ViewModel.IsLoggedIn)
        {
          
            AdjustButtonSpacing();
            PlayEntranceAnimations();
        }

        await ViewModel.LoadUserInfoAsync();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        AdjustButtonSpacing();
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

                    ProfileCard.Opacity = 0;
                    ProfileCardTransform.Y = -30;

                    // 强制刷新头像
                    var avatarUrl = ViewModel.CurrentAccount?.AvatarUrl;
                    if (!string.IsNullOrEmpty(avatarUrl) && AvatarPicture.Fill is ImageBrush brush)
                    {
                        brush.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(avatarUrl));
                    }

                    await Task.Delay(150);
                    AdjustButtonSpacing();
                    PlayEntranceAnimations();
                });
            }
            else if (!ViewModel.IsLoggedIn)
            {
                _hasAnimatedButtons = false;
                _hasAnimatedProfileCard = false;
                _hasAnimatedRightCards = false;
                _wasLoggedInOnLoad = false;
                ResetAnimationState();
                EntranceStoryboard.Begin();
            }
        }
    }

    private void RegisterRippleHandlers()
    {
        var rippleButtons = new[] { BtnSwitchAccount, BtnRefreshInfo, BtnGenshinData, BtnGachaAnalysis, BtnSecurityCenter, BtnLockAccount, BtnCopyCookie, BtnDeleteAccount, BtnLogout };
        foreach (var btn in rippleButtons)
        {
            btn.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Btn_RipplePressed), true);
        }
    }

    private void Btn_RipplePressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var host = (button.Content as Grid)?.Children[0] as Canvas;
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

    private void ResetAnimationState()
    {

        ButtonsStaggerStoryboard?.Stop();
        RightCardsEntranceStoryboard?.Stop();
        ProfileCardEntranceStoryboard?.Stop();

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

        // 角色卡片复位
        ProfileCard.Opacity = 0; ProfileCardTransform.Y = -30;

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

        if (!_hasAnimatedProfileCard)
        {
            _hasAnimatedProfileCard = true;
            ProfileCardEntranceStoryboard?.Begin();
        }

        if (!_hasAnimatedRightCards)
        {
            _hasAnimatedRightCards = true;
            RightCardsEntranceStoryboard?.Begin();
        }
    }

    #endregion

    #region 账户登录
    private async void OnSwitchAccountClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AccountInfo account)
        {
            try
            {
                if (ViewModel.SwitchAccountCommand is IAsyncRelayCommand<AccountInfo> asyncCmd)
                    await asyncCmd.ExecuteAsync(account);
            }
            catch (Exception ex)
            {
                _notificationService.Show("Account_LoginFailed".GetLocalized(), ex.Message, NotificationType.Error, 3000);
            }
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
            string title = isCurrentAccount ? "Account_DeleteCurrent".GetLocalized() : "Account_DeleteAccount".GetLocalized();
            string content = isCurrentAccount
                ? string.Format("Account_DeleteCurrentConfirm_Format".GetLocalized(), account.Nickname, account.GameUid)
                : string.Format("Account_DeleteAccountConfirm_Format".GetLocalized(), account.Nickname, account.GameUid);

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
            PrimaryButtonText = "DeleteLabel".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        return await dialog.ShowAsync();
    }
    #endregion

    #region 签到
    private async void OnCheckinClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn) btn.IsEnabled = false;
        BtnCheckinText.Text = "Account_CheckingIn".GetLocalized();
        try
        {
            var progress = new Progress<string>(msg =>
            {
                DispatcherQueue.TryEnqueue(() => BtnCheckinText.Text = msg);
            });
            var result = await _unifiedCheckinService.ExecuteAllCheckinsAsync(progress);
            BtnCheckinText.Text = result.OverallSuccess ? "Account_CheckinComplete".GetLocalized() : "Account_CheckinFailed".GetLocalized();

            _notificationService.Show(
                result.OverallSuccess ? "Account_CheckinComplete".GetLocalized() : "Account_CheckinFailed".GetLocalized(),
                result.SummaryMessage,
                result.OverallSuccess ? NotificationType.Success : NotificationType.Warning,
                5000);
        }
        catch (Exception ex)
        {
            BtnCheckinText.Text = "Account_CheckinException".GetLocalized();
            _notificationService.Show("Account_CheckinException".GetLocalized(), ex.Message, NotificationType.Error, 3000);
            Debug.WriteLine($"签到异常: {ex.Message}");
        }
        finally
        {
            if (sender is Button btn2) btn2.IsEnabled = true;
        }
    }
    #endregion

    #region 右侧高度自适应
    private void LeftColumnGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        
        if (e.NewSize.Height > 100)
            RightGrid.Height = e.NewSize.Height;
    }

  
    private void AdjustButtonSpacing()
    {
        if (BtnGrid == null || ProfileCard == null) return;

            const int baseSpacing = 8;
            const int maxSpacing = 28;
            const int buttonCount = 9;
            const double titleBarOffset = 40;  
            const double outerMargin = 36;  
            const double safetyMargin = 16; 

         
            double viewportHeight = this.ActualHeight - titleBarOffset - outerMargin - safetyMargin;
            if (viewportHeight <= 0) return;

           
            double profileHeight = ProfileCard.ActualHeight;
            double buttonAreaHeight = viewportHeight - profileHeight - 24;

        
            double totalButtonsHeight = 0;
            totalButtonsHeight += BtnSwitchAccount?.ActualHeight ?? 44;
            totalButtonsHeight += BtnRefreshInfo?.ActualHeight ?? 44;
            totalButtonsHeight += BtnGenshinData?.ActualHeight ?? 44;
            totalButtonsHeight += BtnGachaAnalysis?.ActualHeight ?? 44;
            totalButtonsHeight += BtnSecurityCenter?.ActualHeight ?? 44;
            totalButtonsHeight += BtnLockAccount?.ActualHeight ?? 44;
            totalButtonsHeight += BtnCopyCookie?.ActualHeight ?? 44;
            totalButtonsHeight += BtnDeleteAccount?.ActualHeight ?? 44;
            totalButtonsHeight += BtnLogout?.ActualHeight ?? 44;

            double spacing;
            const double minSpacing = 2;
          
            if (buttonAreaHeight > totalButtonsHeight + (buttonCount - 1) * minSpacing)
                spacing = Math.Min((buttonAreaHeight - totalButtonsHeight) / (buttonCount - 1), maxSpacing);
            else
                spacing = minSpacing;

            BtnGrid.RowSpacing = spacing;
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

