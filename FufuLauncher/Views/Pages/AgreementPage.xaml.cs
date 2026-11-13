/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Services.CodeSigning;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views
{
    public sealed partial class AgreementPage : Page
    {
        private enum OnboardingStep
        {
            Agreement,
            IconCheck,
            DataDir,
            Trust,
            Complete
        }

        private const int CompleteBadgeHoldMilliseconds = 1000;

        private readonly OnboardingMotion _motion = new();
        private bool _hasEntered;
        private bool _isTransitioning;
        private bool _isFinishing;
        private bool _isRestarting;

        public AgreementViewModel ViewModel
        {
            get;
        }

        public AgreementPage()
        {
            ViewModel = App.GetService<AgreementViewModel>();
            InitializeComponent();
        }

        private OnboardingStep CurrentStep => ViewModel.IsCompleteMode
            ? OnboardingStep.Complete
            : ViewModel.IsTrustMode
                ? OnboardingStep.Trust
                : ViewModel.IsDataDirMode
                    ? OnboardingStep.DataDir
                    : ViewModel.IsIconCheckMode
                        ? OnboardingStep.IconCheck
                        : OnboardingStep.Agreement;

        private IReadOnlyList<FrameworkElement> GetStepItems(OnboardingStep step)
        {
            return step switch
            {
                OnboardingStep.IconCheck => OnboardingMotion.CollectVisible(IconCheckSection),
                OnboardingStep.DataDir => OnboardingMotion.CollectVisible(DataDirSection),
                OnboardingStep.Trust => OnboardingMotion.CollectVisible(TrustSection),
                OnboardingStep.Complete => GetCompleteBadgeItems(),
                _ => new FrameworkElement[]
                {
                    IconImage,
                    TitleText,
                    DescText,
                    AgreementArea,
                    AgreementActionBar
                }
            };
        }

        private IReadOnlyList<FrameworkElement> GetAllMotionItems()
        {
            var items = new List<FrameworkElement>
            {
                IconImage,
                TitleText,
                DescText,
                AgreementArea,
                AgreementActionBar
            };

            items.AddRange(OnboardingMotion.CollectVisible(IconCheckSection));
            items.AddRange(OnboardingMotion.CollectVisible(DataDirSection));
            items.AddRange(OnboardingMotion.CollectVisible(TrustSection));
            items.AddRange(GetCompleteBadgeItems());
            items.AddRange(GetWelcomeItems());

            return items;
        }

        private IReadOnlyList<FrameworkElement> GetCompleteBadgeItems()
        {
            return OnboardingMotion.CollectVisible(CompleteBadgeArea);
        }

        private IReadOnlyList<FrameworkElement> GetWelcomeItems()
        {
            return OnboardingMotion.CollectVisible(WelcomePanel);
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_hasEntered) return;
            _hasEntered = true;

            _motion.Prepare(GetAllMotionItems());
            _ = EnterStepAsync(CurrentStep);
            _ = LoadAgreementAsync();
            _ = ViewModel.InitTelemetryChoiceAsync();
        }

        private async void OnReloadAgreementClick(object sender, RoutedEventArgs e)
        {
            await LoadAgreementAsync();
        }

        private async Task LoadAgreementAsync()
        {
            await ViewModel.InitializeAgreementTextAsync();

            if (string.IsNullOrEmpty(ViewModel.AgreementText)) return;

            AgreementScrollViewer.UpdateLayout();
            if (AgreementScrollViewer.ScrollableHeight <= 0)
            {
                ViewModel.HasReadAgreement = true;
            }
        }

        private async Task EnterStepAsync(OnboardingStep step)
        {
            if (step == OnboardingStep.Complete)
            {
                await EnterCompleteStepAsync();
                return;
            }

            var items = GetStepItems(step);
            _motion.Prepare(items);
            await _motion.EnterAsync(items);
        }

        private async Task EnterCompleteStepAsync()
        {
            if (!_motion.AnimationEnabled)
            {
                ShowWelcomePanel();
                _ = _motion.EnterAsync(GetWelcomeItems());
                return;
            }

            var badgeItems = GetCompleteBadgeItems();
            _motion.Prepare(badgeItems);
            await _motion.EnterAsync(badgeItems);

            await Task.Delay(CompleteBadgeHoldMilliseconds);

            await _motion.ExitAsync(badgeItems);

            ShowWelcomePanel();

            var welcomeItems = GetWelcomeItems();
            _motion.Prepare(welcomeItems);
            await _motion.EnterAsync(welcomeItems);
        }

        private void ShowWelcomePanel()
        {
            CompleteBadgeArea.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Visible;
        }

        private async Task AdvanceAsync(Action advance)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;

            try
            {
                var currentItems = GetStepItems(CurrentStep);
                _motion.Settle(currentItems);

                await _motion.ExitAsync(currentItems);

                advance();

                await EnterStepAsync(CurrentStep);
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        private async void OnAgreementNextClick(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsAgreementChecked) return;

            await AdvanceAsync(ViewModel.EnterIconCheckStep);
        }

        private async void OnIconCheckConfirmClick(object sender, RoutedEventArgs e)
        {
            await AdvanceAsync(ViewModel.EnterDataDirStep);
        }

        private async void OnDataDirNextClick(object sender, RoutedEventArgs e)
        {
            if (!await ViewModel.TryPrepareDataDirStepAsync()) return;

            await AdvanceAsync(ViewModel.EnterTrustStep);
        }

        private void OnAgreementScrollChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ViewModel.HasReadAgreement) return;
            var sv = (ScrollViewer)sender;
            if (sv.VerticalOffset >= sv.ScrollableHeight - 10)
            {
                ViewModel.HasReadAgreement = true;
            }
        }

        private async void OnTrustInstallClick(object sender, RoutedEventArgs e)
        {
            if (_isFinishing) return;
            _isFinishing = true;

            try
            {
                var service = App.GetService<CodeSigningTrustService>();
                var result = await TrustInstallFlow.InstallAsync(service, XamlRoot, TrustStoreScope.CurrentUser);

                if (result == null)
                {
                    _isFinishing = false;
                    return;
                }

                TrustStatusText.Text = result.Message;

                if (!result.Ok)
                {
                    _isFinishing = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("AgreementPage", $"[Agreement] Failed to install platform certificate: {ex}");
                TrustStatusText.Text = $"安装失败：{ex.Message}";
                _isFinishing = false;
                return;
            }

            await FinishTrustStepAsync();
            _isFinishing = false;
        }

        private async void OnTrustSkipClick(object sender, RoutedEventArgs e)
        {
            if (_isFinishing) return;
            _isFinishing = true;

            await FinishTrustStepAsync();
            _isFinishing = false;
        }

        private async Task FinishTrustStepAsync()
        {
            if (!await ViewModel.SaveAgreementAcceptedAsync())
            {
                TrustStatusText.Text = "保存配置失败，请重试。";
                return;
            }

            await AdvanceAsync(ViewModel.EnterCompleteStep);
        }

        private void OnRestartClick(object sender, RoutedEventArgs e)
        {
            if (_isRestarting) return;
            _isRestarting = true;

            RestartButton.IsEnabled = false;
            RestartStatusText.Text = "正在重启启动器…";

            if (ViewModel.TryRestart()) return;

            RestartStatusText.Text = "重启失败，请再次点击按钮，或手动关闭后重新打开启动器。";
            RestartButton.IsEnabled = true;
            _isRestarting = false;
        }
    }
}