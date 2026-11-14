/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net.Http;
using System.Net.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Downloader;

namespace Updater;

public partial class MainWindow : Window
{
    #region Fields & Constants

    private const string AppVersion = "1.7.2.4";
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";

    private static readonly HttpClient _httpClient = new() 
    { 
        Timeout = TimeSpan.FromSeconds(15) 
    };

    private static readonly HttpClient _r2HttpClient = CreateR2HttpClient();

    private string? _targetExeUrl;
    private string? _r2ExeUrl;
    private string? _fileName;
    private string? _expectedSha256;
    private List<MirrorInfo> _mirrors = new();
    private DownloadService? _downloader;
    private Stopwatch _uiUpdateTimer = new();

    private DispatcherTimer? _stuckTimer;
    private long _currentReceivedBytes = 0;
    private long _lastReceivedBytes = 0;
    private int _stuckTicks = 0;
    private bool _isDownloading = false;
    private bool _isNodeDownload = false;
    private bool _isR2Download = false;
    private bool _useThirdPartyCDN = true;
    private bool _isPreviewMode = false;
    private bool _isRollbackMode = false;
    private bool _isTestForceUpdate = false;
    private string _installedVersion = AppVersion;
    private bool _isInstalledPreviewBuild = false;
    private string _officialVersion = string.Empty;

    private const string ExpectedTestFileMD5 = "47250a973d1b88d9445f94db4ef2c97a";

    #endregion

    #region Initialization

    public MainWindow()
    {
        InitializeComponent();
        Strings.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Strings.LanguageChanged -= OnLanguageChanged;

        _httpClient.DefaultRequestHeaders.Add("User-Agent", DefaultUserAgent);

        _stuckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _stuckTimer.Tick += StuckTimer_Tick;

        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--use-third-party-cdn=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring("--use-third-party-cdn=".Length);
                _useThirdPartyCDN = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            else if (arg.Equals("--preview", StringComparison.OrdinalIgnoreCase))
            {
                _isPreviewMode = true;
            }
            else if (arg.Equals("--rollback", StringComparison.OrdinalIgnoreCase))
            {
                _isRollbackMode = true;
            }
            else if (arg.Equals("--test-force-update", StringComparison.OrdinalIgnoreCase))
            {
                _isTestForceUpdate = true;
            }
            else if (arg.StartsWith("--installed-version=", StringComparison.OrdinalIgnoreCase))
            {
                _installedVersion = arg.Substring("--installed-version=".Length);
            }
        }

        if (_isTestForceUpdate)
        {
            Title = Strings.Get("WindowTitleTestMode");
        }

        UpdateLanguageToggle();
        _isInstalledPreviewBuild = _installedVersion.IndexOf("Pre-release", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    #region HTTP Clients

    private static HttpClient CreateR2HttpClient(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) => sslPolicyErrors == SslPolicyErrors.None
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.Add("User-Agent", DefaultUserAgent);
        return client;
    }

    #endregion

    #region Localization

    private sealed class LocalizedMessage
    {
        public LocalizedMessage(string key, object?[] args)
        {
            Key = key;
            Args = args;
        }

        public string Key { get; }
        public object?[] Args { get; }
    }

    private void SetLocalizedText(TextBlock target, string key, params object?[] args)
    {
        target.Tag = new LocalizedMessage(key, args);
        target.Text = Strings.Format(key, args);
    }

    private static void RefreshLocalizedText(TextBlock target)
    {
        if (target.Tag is LocalizedMessage message)
        {
            target.Text = Strings.Format(message.Key, message.Args);
        }
    }

    private void RefreshLocalizedTexts()
    {
        RefreshLocalizedText(SubtitleText);
        RefreshLocalizedText(DownloadMainText);
        RefreshLocalizedText(DownloadDetailText);
        RefreshLocalizedText(NoUpdateText);
        RefreshLocalizedText(TestStatusText);

        foreach (MirrorInfo mirror in _mirrors)
        {
            mirror.RefreshStatusDesc();
        }
    }

    private void UpdateLanguageToggle()
    {
        LanguageToggleBtn.Content = Strings.Get(Strings.CurrentLanguage == "zh-CN" ? "LanguageShortChinese" : "LanguageShortEnglish");

        string tooltip = Strings.Get(Strings.CurrentLanguage == "zh-CN" ? "SwitchToEnglish" : "SwitchToChinese");
        LanguageToggleBtn.ToolTip = tooltip;
        AutomationProperties.SetName(LanguageToggleBtn, tooltip);
    }

    private void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        Strings.ToggleLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLanguageToggle();

        Title = _isTestForceUpdate
            ? Strings.Get("WindowTitleTestMode")
            : Strings.Get("WindowTitle");

        RefreshLocalizedTexts();
    }

    #endregion
}
