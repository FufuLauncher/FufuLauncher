using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FufuLauncher.ViewModels
{
    public partial class AgreementViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        private bool _isAgreementChecked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HintVisibility))]
        private bool _hasReadAgreement;
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        private bool _isIconCheckMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementVisibility))]
        [NotifyPropertyChangedFor(nameof(IconCheckVisibility))]
        [NotifyPropertyChangedFor(nameof(DataDirVisibility))]
        private bool _isDataDirMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AgreementText))]
        private int _selectedLanguageIndex;

        [ObservableProperty]
        private string _dataPath = Helpers.AppPaths.DataDir;

        [ObservableProperty]
        private string _cachePath = Helpers.AppPaths.CacheDir;

        public Visibility AgreementVisibility => (!IsIconCheckMode && !IsDataDirMode) ? Visibility.Visible : Visibility.Collapsed;
        
        public Visibility IconCheckVisibility => (IsIconCheckMode && !IsDataDirMode) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DataDirVisibility => IsDataDirMode ? Visibility.Visible : Visibility.Collapsed;

        public Visibility HintVisibility => HasReadAgreement ? Visibility.Collapsed : Visibility.Visible;

        public string AgreementText => SelectedLanguageIndex switch
        {
            1 => AgreementTextEn,
            2 => AgreementTextTc,
            _ => AgreementTextSc
        };

        public IAsyncRelayCommand NextCommand { get; }
        
        public IAsyncRelayCommand ConfirmIconsCommand { get; }
        
        public IAsyncRelayCommand TroubleshootIconsCommand { get; }

        public IAsyncRelayCommand ConfirmDataDirCommand { get; }

        public IAsyncRelayCommand BrowseDataPathCommand { get; }

        public IAsyncRelayCommand BrowseCachePathCommand { get; }

        public AgreementViewModel(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;

            NextCommand = new AsyncRelayCommand(GoToIconCheckAsync);
            ConfirmIconsCommand = new AsyncRelayCommand(GoToDataDirAsync);
            TroubleshootIconsCommand = new AsyncRelayCommand(OnIconsMissingAsync);
            ConfirmDataDirCommand = new AsyncRelayCommand(FinalizeAgreementAsync);
            BrowseDataPathCommand = new AsyncRelayCommand(PickDataPathAsync);
            BrowseCachePathCommand = new AsyncRelayCommand(PickCachePathAsync);
        }
        
        private async Task GoToIconCheckAsync()
        {
            if (!IsAgreementChecked) return;
            IsIconCheckMode = true;
            await Task.CompletedTask;
        }

        private async Task GoToDataDirAsync()
        {
            IsDataDirMode = true;
            await Task.CompletedTask;
        }
        
        private async Task FinalizeAgreementAsync()
        {
            await _localSettingsService.SaveSettingAsync("UserAgreementAccepted", true);
            Helpers.AppPaths.SaveCustomPaths(DataPath, CachePath);
            WeakReferenceMessenger.Default.Send(new AgreementAcceptedMessage());
        }
        
        private async Task OnIconsMissingAsync()
        {
            var helpUrl = ApiEndpoints.IconTroubleshootUrl; 
            
            if (!string.IsNullOrEmpty(helpUrl) && Uri.TryCreate(helpUrl, UriKind.Absolute, out var uri))
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }

        private async Task PickDataPathAsync()
        {
            var folder = await PickFolderAsync();
            if (folder != null) DataPath = folder;
        }

        private async Task PickCachePathAsync()
        {
            var folder = await PickFolderAsync();
            if (folder != null) CachePath = folder;
        }

        private static async Task<string?> PickFolderAsync()
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private const string AgreementTextSc = "芙芙启动器用户协议\n版本号：v1.0 | 最后更新：2025年12月 | 本软件为免费的第三方辅助工具，如果你花钱购买了请联系卖家进行退款处理\n\n欢迎使用芙芙启动器。本协议是您（用户）与开发者之间关于使用本软件的约定。请您在使用前仔细阅读并理解本协议全部内容。\n\n一、软件性质与合规前提\n本软件是独立开发者制作的第三方《原神》启动辅助工具，与米哈游及其关联公司无任何关联。开发者未获得官方任何形式的授权、认可或技术支持。\n您确认已同意并遵守《原神》官方用户协议及相关规则。若本软件功能与官方协议存在冲突，您应主动停止使用，否则由此产生的后果由您自行承担。\n您理解第三方辅助工具的法律地位存在不确定性，并自愿承担由此带来的一切潜在风险。\n\n二、核心功能与风险告知\n本软件提供以下功能，您需充分了解其风险后自愿选择使用：\n\n账号本地管理：在本地加密存储账号登录令牌。您应妥善保管设备安全，因设备被入侵、病毒感染或密码泄露导致的损失，我们不承担责任。\n注入功能：提供可选的DLL注入接口。重要提示：该功能可能被官方反作弊系统识别为异常行为，存在导致账号封禁（包括永久封禁）的高风险。您理解并同意，一旦选择使用该功能，即视为自愿承担账号安全风险。\n辅助工具：键盘连点器等自动化功能可能违反官方公平游戏声明。使用此类工具导致的账号处罚风险由您自行承担。\n米游社登录：通过WebView2实现网页登录，我们不存储您的密码明文。但登录过程的安全性取决于官方页面和网络环境。\n附加程序启动：您自行配置的第三方程序安全性由您负责，我们不承担审查义务。\n\n三、用户权利与义务\n您有权在遵守本协议及官方规则的前提下使用本软件。\n您不得利用本软件从事任何违反法律法规、侵犯他人权益或破坏游戏公平性的行为。\n您不得对本软件实施逆向工程、反编译、去除标识或制作恶意修改版。\n您应从官方网盘下载本软件，因使用非官方版本导致的安全问题，我们不承担责任。\n\n四、责任限制条款\n在适用法律允许的最大范围内，本软件按\"现状\"提供，我们不提供任何形式的保证：\n\n我们不保证软件无错误、无中断、无病毒，也不保证与所有系统环境完全兼容。\n因使用本软件导致的账号封禁、游戏数据丢失、虚拟财产损失等，我们不承担责任。您理解这是使用第三方工具的固有风险。\n因不可抗力、游戏官方更新、政策法规变化导致软件功能失效，我们不承担赔偿责任。\n您理解我们无义务提供永久技术支持或持续更新服务。\n\n五、数据隐私保护\n本软件为纯客户端工具，您的账号信息、配置数据仅存储在本地设备，不上传至任何服务器。\n我们不收集、分析或分享您的个人信息和游戏数据。\n您有权随时删除本软件及所有本地数据，我们不会保留任何备份。\n\n六、知识产权声明\n本软件源代码遵循MIT开源协议，保留原作者署名权。\n软件名称、原创UI设计、独立功能模块归开发者所有。\n使用的《原神》相关素材仅用于识别游戏本身，知识产权归官方所有。\n\n七、未成年人使用条款\n建议未成年人在监护人协助下使用本软件。\n监护人应监督未成年人合理使用，防止沉迷游戏或不当消费。\n我们将按照本协议同等标准保护未成年人的信息安全。\n\n八、协议变更与终止\n我们有权根据法律法规变化或软件更新需要修改本协议，修改后发布即生效。\n若您不同意修改内容，应停止使用并卸载软件。继续使用视为接受修改。\n您可随时停止使用本软件，本协议即告终止。\n\n九、法律适用与争议解决\n本协议适用中华人民共和国法律。\n因本协议产生的争议，双方应友好协商解决；协商不成的，提交开发者所在地有管辖权的人民法院诉讼解决。\n\n十、其他约定\n本协议构成双方关于本软件的完整约定。\n若本协议任何条款被认定为无效，不影响其他条款的效力。\n您理解并同意，本协议中的责任限制条款是软件免费提供的对价，属于公平合理的商业安排。\n\n十一、联系方式\n请通过内部推荐进入群聊\n\n芙芙启动器开发团队 保留所有权利";

        private const string AgreementTextEn = "FuFu Launcher User Agreement\nVersion: v1.0 | Last Updated: December 2025 | This software is a free third-party utility tool. If you paid money for it, please contact the seller for a refund.\n\nWelcome to FuFu Launcher. This Agreement is a binding contract between you (the User) and the Developer regarding the use of this software. Please read and understand all terms carefully before use.\n\n1. Software Nature and Compliance\nThis software is an independently developed third-party launch utility for Genshin Impact, with no affiliation to miHoYo or its subsidiaries. The developer has not received any form of official authorization, endorsement, or technical support.\nYou confirm that you have agreed to and comply with Genshin Impact's official User Agreement and related rules. If any function of this software conflicts with the official agreement, you should proactively stop using it; otherwise, you bear all consequences.\nYou understand that the legal status of third-party tools is uncertain, and you voluntarily assume all potential risks.\n\n2. Core Features and Risk Disclosure\nThis software provides the following features. You should fully understand the risks before voluntarily choosing to use them:\n\nLocal Account Management: Stores login tokens locally with encryption. You are responsible for device security; we are not liable for losses caused by device intrusion, virus infection, or password leaks.\nInjection Feature: Provides an optional DLL injection interface. IMPORTANT: This feature may be detected by the official anti-cheat system as abnormal behavior, carrying a HIGH RISK of account suspension (including permanent bans). By choosing to use this feature, you voluntarily assume all account security risks.\nUtility Tools: Automation features such as key auto-clickers may violate the official fair play policy. You bear all risks of account penalties from using such tools.\nMiYouShe Login: Web login via WebView2; we do not store your password in plaintext. Login security depends on the official page and network environment.\nAdditional Program Launch: You are responsible for the security of third-party programs you configure; we have no obligation to audit them.\n\n3. User Rights and Obligations\nYou have the right to use this software in compliance with this Agreement and official rules.\nYou shall not use this software for any activity that violates laws, infringes on others' rights, or undermines game fairness.\nYou shall not reverse engineer, decompile, remove identifiers from, or create malicious modified versions of this software.\nYou should download this software from the official source; we are not responsible for security issues caused by unofficial versions.\n\n4. Limitation of Liability\nTo the maximum extent permitted by applicable law, this software is provided \"AS IS\" without any warranty:\n\nWe do not guarantee the software is error-free, uninterrupted, virus-free, or fully compatible with all system environments.\nWe are not liable for account bans, game data loss, or virtual property loss caused by using this software. You understand this is an inherent risk of using third-party tools.\nWe are not liable for compensation when software functionality fails due to force majeure, official game updates, or policy changes.\nYou understand we have no obligation to provide permanent technical support or continuous update services.\n\n5. Data Privacy Protection\nThis software is a purely client-side tool. Your account information and configuration data are stored only on your local device and are not uploaded to any server.\nWe do not collect, analyze, or share your personal information or game data.\nYou have the right to delete this software and all local data at any time; we do not retain any backups.\n\n6. Intellectual Property\nThis software's source code follows the MIT open-source license, preserving the original author's attribution rights.\nThe software name, original UI design, and independent functional modules belong to the developer.\nGenshin Impact-related assets are used solely to identify the game itself; intellectual property belongs to the official rights holder.\n\n7. Minor Users\nMinors are advised to use this software under guardian supervision.\nGuardians should monitor minors' reasonable use to prevent game addiction or inappropriate spending.\nWe protect minors' information security to the same standard as this Agreement.\n\n8. Agreement Modification and Termination\nWe reserve the right to modify this Agreement based on legal changes or software updates; modifications take effect upon publication.\nIf you disagree with modifications, you should stop using and uninstall the software. Continued use constitutes acceptance.\nYou may stop using this software at any time, at which point this Agreement terminates.\n\n9. Governing Law and Dispute Resolution\nThis Agreement is governed by the laws of the People's Republic of China.\nDisputes arising from this Agreement shall be resolved through friendly negotiation; if negotiation fails, they shall be submitted to the competent court at the developer's location.\n\n10. Miscellaneous\nThis Agreement constitutes the complete agreement between both parties regarding this software.\nIf any clause of this Agreement is deemed invalid, it does not affect the validity of other clauses.\nYou understand and agree that the limitation of liability clauses are the consideration for the software being provided free of charge, constituting a fair and reasonable commercial arrangement.\n\n11. Contact\nPlease join via internal referral\n\nFuFu Launcher Development Team. All Rights Reserved.";

        private const string AgreementTextTc = "\u82b1\u82b7\u555f\u52d5\u5668\u7528\u6236\u5354\u8b70\n\u7248\u672c\u865f\uff1av1.0 | \u6700\u5f8c\u66f4\u65b0\uff1a2025\u5e7412\u6708 | \u672c\u8edf\u9ad4\u70ba\u514d\u8cbb\u7684\u7b2c\u4e09\u65b9\u8f14\u52a9\u5de5\u5177\uff0c\u5982\u679c\u4f60\u82b1\u9322\u8cfc\u8cb7\u4e86\u8acb\u806f\u7e6b\u8ce3\u5bb6\u9032\u884c\u9000\u6b3e\u8655\u7406\n\n\u6b61\u8fce\u4f7f\u7528\u82b1\u82b7\u555f\u52d5\u5668\u3002\u672c\u5354\u8b70\u662f\u60a8\uff08\u7528\u6236\uff09\u8207\u958b\u767c\u8005\u4e4b\u9593\u95dc\u65bc\u4f7f\u7528\u672c\u8edf\u9ad4\u7684\u7d04\u5b9a\u3002\u8acb\u60a8\u5728\u4f7f\u7528\u524d\u4ed4\u7d30\u95b1\u8b80\u4e26\u7406\u89e3\u672c\u5354\u8b70\u5168\u90e8\u5167\u5bb9\u3002\n\n\u4e00\u3001\u8edf\u9ad4\u6027\u8cea\u8207\u5408\u898f\u524d\u63d0\n\u672c\u8edf\u9ad4\u662f\u7368\u7acb\u958b\u767c\u8005\u88fd\u4f5c\u7684\u7b2c\u4e09\u65b9\u300a\u539f\u795e\u300b\u555f\u52d5\u8f14\u52a9\u5de5\u5177\uff0c\u8207\u7c73\u54c8\u904a\u53ca\u5176\u95dc\u806f\u516c\u53f8\u7121\u4efb\u4f55\u95dc\u806f\u3002\u958b\u767c\u8005\u672a\u7372\u5f97\u5b98\u65b9\u4efb\u4f55\u5f62\u5f0f\u7684\u6388\u6b0a\u3001\u8a8d\u53ef\u6216\u6280\u8853\u652f\u6301\u3002\n\u60a8\u78ba\u8a8d\u5df2\u540c\u610f\u4e26\u9075\u5b88\u300a\u539f\u795e\u300b\u5b98\u65b9\u7528\u6236\u5354\u8b70\u53ca\u76f8\u95dc\u898f\u5247\u3002\u82e5\u672c\u8edf\u9ad4\u529f\u80fd\u8207\u5b98\u65b9\u5354\u8b70\u5b58\u5728\u885d\u7a81\uff0c\u60a8\u61c9\u4e3b\u52d5\u505c\u6b62\u4f7f\u7528\uff0c\u5426\u5247\u7531\u6b64\u7522\u751f\u7684\u5f8c\u679c\u7531\u60a8\u81ea\u884c\u627f\u64d4\u3002\n\u60a8\u7406\u89e3\u7b2c\u4e09\u65b9\u8f14\u52a9\u5de5\u5177\u7684\u6cd5\u5f8b\u5730\u4f4d\u5b58\u5728\u4e0d\u78ba\u5b9a\u6027\uff0c\u4e26\u81ea\u9858\u627f\u64d4\u7531\u6b64\u5e36\u4f86\u7684\u4e00\u5207\u6f5b\u5728\u98a8\u96aa\u3002\n\n\u4e8c\u3001\u6838\u5fc3\u529f\u80fd\u8207\u98a8\u96aa\u544a\u77e5\n\u672c\u8edf\u9ad4\u63d0\u4f9b\u4ee5\u4e0b\u529f\u80fd\uff0c\u60a8\u9700\u5145\u5206\u4e86\u89e3\u5176\u98a8\u96aa\u5f8c\u81ea\u9858\u9078\u64c7\u4f7f\u7528\uff1a\n\n\u5e33\u865f\u672c\u5730\u7ba1\u7406\uff1a\u5728\u672c\u5730\u52a0\u5bc6\u5b58\u5132\u5e33\u865f\u767b\u5165\u4ee4\u724c\u3002\u60a8\u61c9\u59a5\u5584\u4fdd\u7ba1\u8a2d\u5099\u5b89\u5168\uff0c\u56e0\u8a2d\u5099\u88ab\u5165\u4fb5\u3001\u75c5\u6bd2\u611f\u67d3\u6216\u5bc6\u78bc\u6d29\u9732\u5c0e\u81f4\u7684\u640d\u5931\uff0c\u6211\u5011\u4e0d\u627f\u64d4\u8cac\u4efb\u3002\n\u6ce8\u5165\u529f\u80fd\uff1a\u63d0\u4f9b\u53ef\u9078\u7684DLL\u6ce8\u5165\u63a5\u53e3\u3002\u91cd\u8981\u63d0\u793a\uff1a\u8a72\u529f\u80fd\u53ef\u80fd\u88ab\u5b98\u65b9\u53cd\u4f5c\u5f0a\u7cfb\u7d71\u8b58\u5225\u70ba\u7570\u5e38\u884c\u70ba\uff0c\u5b58\u5728\u5c0e\u81f4\u5e33\u865f\u5c01\u7981\uff08\u5305\u62ec\u6c38\u4e45\u5c01\u7981\uff09\u7684\u9ad8\u98a8\u96aa\u3002\u60a8\u7406\u89e3\u4e26\u540c\u610f\uff0c\u4e00\u65e6\u9078\u64c7\u4f7f\u7528\u8a72\u529f\u80fd\uff0c\u5373\u8996\u70ba\u81ea\u9858\u627f\u64d4\u5e33\u865f\u5b89\u5168\u98a8\u96aa\u3002\n\u8f14\u52a9\u5de5\u5177\uff1a\u9375\u76e4\u9023\u9ede\u5668\u7b49\u81ea\u52d5\u5316\u529f\u80fd\u53ef\u80fd\u9055\u53cd\u5b98\u65b9\u516c\u5e73\u904a\u6232\u8072\u660e\u3002\u4f7f\u7528\u6b64\u985e\u5de5\u5177\u5c0e\u81f4\u7684\u5e33\u865f\u8655\u7f70\u98a8\u96aa\u7531\u60a8\u81ea\u884c\u627f\u64d4\u3002\n\u7c73\u904a\u793e\u767b\u5165\uff1a\u901a\u904eWebView2\u5be6\u73fe\u7db2\u9801\u767b\u5165\uff0c\u6211\u5011\u4e0d\u5b58\u5132\u60a8\u7684\u5bc6\u78bc\u660e\u6587\u3002\u4f46\u767b\u5165\u904e\u7a0b\u7684\u5b89\u5168\u6027\u53d6\u6c7a\u65bc\u5b98\u65b9\u9801\u9762\u548c\u7db2\u8def\u74b0\u5883\u3002\n\u9644\u52a0\u7a0b\u5f0f\u555f\u52d5\uff1a\u60a8\u81ea\u884c\u914d\u7f6e\u7684\u7b2c\u4e09\u65b9\u7a0b\u5f0f\u5b89\u5168\u6027\u7531\u60a8\u8ca0\u8cac\uff0c\u6211\u5011\u4e0d\u627f\u64d4\u5be9\u67e5\u7fa9\u52d9\u3002\n\n\u4e09\u3001\u7528\u6236\u6b0a\u5229\u8207\u7fa9\u52d9\n\u60a8\u6709\u6b0a\u5728\u9075\u5b88\u672c\u5354\u8b70\u53ca\u5b98\u65b9\u898f\u5247\u7684\u524d\u63d0\u4e0b\u4f7f\u7528\u672c\u8edf\u9ad4\u3002\n\u60a8\u4e0d\u5f97\u5229\u7528\u672c\u8edf\u9ad4\u5f9e\u4e8b\u4efb\u4f55\u9055\u53cd\u6cd5\u5f8b\u6cd5\u898f\u3001\u4fb5\u72af\u4ed6\u4eba\u6b0a\u76ca\u6216\u7834\u58de\u904a\u6232\u516c\u5e73\u6027\u7684\u884c\u70ba\u3002\n\u60a8\u4e0d\u5f97\u5c0d\u672c\u8edf\u9ad4\u5be6\u65bd\u9006\u5411\u5de5\u7a0b\u3001\u53cd\u7de8\u8b6f\u3001\u53bb\u9664\u6a19\u8b58\u6216\u88fd\u4f5c\u60e1\u610f\u4fee\u6539\u7248\u3002\n\u60a8\u61c9\u5f9e\u5b98\u65b9\u7db2\u76e4\u4e0b\u8f09\u672c\u8edf\u9ad4\uff0c\u56e0\u4f7f\u7528\u975e\u5b98\u65b9\u7248\u672c\u5c0e\u81f4\u7684\u5b89\u5168\u554f\u984c\uff0c\u6211\u5011\u4e0d\u627f\u64d4\u8cac\u4efb\u3002\n\n\u56db\u3001\u8cac\u4efb\u9650\u5236\u689d\u6b3e\n\u5728\u9069\u7528\u6cd5\u5f8b\u5141\u8a31\u7684\u6700\u5927\u7bc4\u570d\u5167\uff0c\u672c\u8edf\u9ad4\u6309\u300c\u73fe\u72c0\u300d\u63d0\u4f9b\uff0c\u6211\u5011\u4e0d\u63d0\u4f9b\u4efb\u4f55\u5f62\u5f0f\u7684\u4fdd\u8b49\uff1a\n\n\u6211\u5011\u4e0d\u4fdd\u8b49\u8edf\u9ad4\u7121\u932f\u8aa4\u3001\u7121\u4e2d\u65b7\u3001\u7121\u75c5\u6bd2\uff0c\u4e5f\u4e0d\u4fdd\u8b49\u8207\u6240\u6709\u7cfb\u7d71\u74b0\u5883\u5b8c\u5168\u76f8\u5bb9\u3002\n\u56e0\u4f7f\u7528\u672c\u8edf\u9ad4\u5c0e\u81f4\u7684\u5e33\u865f\u5c01\u7981\u3001\u904a\u6232\u6578\u64da\u4e1f\u5931\u3001\u865b\u64ec\u8ca1\u7522\u640d\u5931\u7b49\uff0c\u6211\u5011\u4e0d\u627f\u64d4\u8cac\u4efb\u3002\u60a8\u7406\u89e3\u9019\u662f\u4f7f\u7528\u7b2c\u4e09\u65b9\u5de5\u5177\u7684\u56fa\u6709\u98a8\u96aa\u3002\n\u56e0\u4e0d\u53ef\u6297\u529b\u3001\u904a\u6232\u5b98\u65b9\u66f4\u65b0\u3001\u653f\u7b56\u6cd5\u898f\u8b8a\u5316\u5c0e\u81f4\u8edf\u9ad4\u529f\u80fd\u5931\u6548\uff0c\u6211\u5011\u4e0d\u627f\u64d4\u8ce0\u511f\u8cac\u4efb\u3002\n\u60a8\u7406\u89e3\u6211\u5011\u7121\u7fa9\u52d9\u63d0\u4f9b\u6c38\u4e45\u6280\u8853\u652f\u6301\u6216\u6301\u7e8c\u66f4\u65b0\u670d\u52d9\u3002\n\n\u4e94\u3001\u6578\u64da\u96b1\u79c1\u4fdd\u8b77\n\u672c\u8edf\u9ad4\u70ba\u7d14\u5ba2\u6236\u7aef\u5de5\u5177\uff0c\u60a8\u7684\u5e33\u865f\u8cc7\u8a0a\u3001\u914d\u7f6e\u6578\u64da\u50c5\u5b58\u5132\u5728\u672c\u5730\u8a2d\u5099\uff0c\u4e0d\u4e0a\u50b3\u81f3\u4efb\u4f55\u4f3a\u670d\u5668\u3002\n\u6211\u5011\u4e0d\u6536\u96c6\u3001\u5206\u6790\u6216\u5206\u4eab\u60a8\u7684\u500b\u4eba\u8cc7\u8a0a\u548c\u904a\u6232\u6578\u64da\u3002\n\u60a8\u6709\u6b0a\u96a8\u6642\u522a\u9664\u672c\u8edf\u9ad4\u53ca\u6240\u6709\u672c\u5730\u6578\u64da\uff0c\u6211\u5011\u4e0d\u6703\u4fdd\u7559\u4efb\u4f55\u5099\u4efd\u3002\n\n\u516d\u3001\u77e5\u8b58\u7522\u6b0a\u8072\u660e\n\u672c\u8edf\u9ad4\u539f\u59cb\u78bc\u9075\u5faaMIT\u958b\u6e90\u5354\u8b70\uff0c\u4fdd\u7559\u539f\u4f5c\u8005\u7f72\u540d\u6b0a\u3002\n\u8edf\u9ad4\u540d\u7a31\u3001\u539f\u5275UI\u8a2d\u8a08\u3001\u7368\u7acb\u529f\u80fd\u6a21\u7d44\u6b78\u958b\u767c\u8005\u6240\u6709\u3002\n\u4f7f\u7528\u7684\u300a\u539f\u795e\u300b\u76f8\u95dc\u7d20\u6750\u50c5\u7528\u65bc\u8b58\u5225\u904a\u6232\u672c\u8eab\uff0c\u77e5\u8b58\u7522\u6b0a\u6b78\u5b98\u65b9\u6240\u6709\u3002\n\n\u4e03\u3001\u672a\u6210\u5e74\u4eba\u4f7f\u7528\u689d\u6b3e\n\u5efa\u8b70\u672a\u6210\u5e74\u4eba\u5728\u76e3\u8b77\u4eba\u5354\u52a9\u4e0b\u4f7f\u7528\u672c\u8edf\u9ad4\u3002\n\u76e3\u8b77\u4eba\u61c9\u76e3\u7763\u672a\u6210\u5e74\u4eba\u5408\u7406\u4f7f\u7528\uff0c\u9632\u6b62\u6c89\u8ff7\u904a\u6232\u6216\u4e0d\u7576\u6d88\u8cbb\u3002\n\u6211\u5011\u5c07\u6309\u7167\u672c\u5354\u8b70\u540c\u7b49\u6a19\u6e96\u4fdd\u8b77\u672a\u6210\u5e74\u4eba\u7684\u8cc7\u8a0a\u5b89\u5168\u3002\n\n\u516b\u3001\u5354\u8b70\u8b8a\u66f4\u8207\u7d42\u6b62\n\u6211\u5011\u6709\u6b0a\u6839\u64da\u6cd5\u5f8b\u6cd5\u898f\u8b8a\u5316\u6216\u8edf\u9ad4\u66f4\u65b0\u9700\u8981\u4fee\u6539\u672c\u5354\u8b70\uff0c\u4fee\u6539\u5f8c\u767c\u4f48\u5373\u751f\u6548\u3002\n\u82e5\u60a8\u4e0d\u540c\u610f\u4fee\u6539\u5167\u5bb9\uff0c\u61c9\u505c\u6b62\u4f7f\u7528\u4e26\u5378\u8f09\u8edf\u9ad4\u3002\u7e7c\u7e8c\u4f7f\u7528\u8996\u70ba\u63a5\u53d7\u4fee\u6539\u3002\n\u60a8\u53ef\u96a8\u6642\u505c\u6b62\u4f7f\u7528\u672c\u8edf\u9ad4\uff0c\u672c\u5354\u8b70\u5373\u544a\u7d42\u6b62\u3002\n\n\u4e5d\u3001\u6cd5\u5f8b\u9069\u7528\u8207\u722d\u8b70\u89e3\u6c7a\n\u672c\u5354\u8b70\u9069\u7528\u4e2d\u83ef\u4eba\u6c11\u5171\u548c\u570b\u6cd5\u5f8b\u3002\n\u56e0\u672c\u5354\u8b70\u7522\u751f\u7684\u722d\u8b70\uff0c\u96d9\u65b9\u61c9\u53cb\u597d\u5354\u5546\u89e3\u6c7a\uff1b\u5354\u5546\u4e0d\u6210\u7684\uff0c\u63d0\u4ea4\u958b\u767c\u8005\u6240\u5728\u5730\u6709\u7ba1\u8f44\u6b0a\u7684\u4eba\u6c11\u6cd5\u9662\u8a34\u8a1f\u89e3\u6c7a\u3002\n\n\u5341\u3001\u5176\u4ed6\u7d04\u5b9a\n\u672c\u5354\u8b70\u69cb\u6210\u96d9\u65b9\u95dc\u65bc\u672c\u8edf\u9ad4\u7684\u5b8c\u6574\u7d04\u5b9a\u3002\n\u82e5\u672c\u5354\u8b70\u4efb\u4f55\u689d\u6b3e\u88ab\u8a8d\u5b9a\u70ba\u7121\u6548\uff0c\u4e0d\u5f71\u97ff\u5176\u4ed6\u689d\u6b3e\u7684\u6548\u529b\u3002\n\u60a8\u7406\u89e3\u4e26\u540c\u610f\uff0c\u672c\u5354\u8b70\u4e2d\u7684\u8cac\u4efb\u9650\u5236\u689d\u6b3e\u662f\u8edf\u9ad4\u514d\u8cbb\u63d0\u4f9b\u7684\u5c0d\u50f9\uff0c\u5c6c\u65bc\u516c\u5e73\u5408\u7406\u7684\u5546\u696d\u5b89\u6392\u3002\n\n\u5341\u4e00\u3001\u806f\u7e6b\u65b9\u5f0f\n\u8acb\u901a\u904e\u5167\u90e8\u63a8\u85a6\u9032\u5165\u7fa4\u804a\n\n\u82b1\u82b7\u555f\u52d5\u5668\u958b\u767c\u5718\u968a \u4fdd\u7559\u6240\u6709\u6b0a\u5229";
    }
}
