/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Net.NetworkInformation;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace FufuLauncher.Services;

public class NetworkStatusChangedEventArgs : EventArgs
{
    public bool IsNetworkLost { get; }
    public bool IsProxyNewlyEnabled { get; }

    public NetworkStatusChangedEventArgs(bool isNetworkLost, bool isProxyNewlyEnabled)
    {
        IsNetworkLost = isNetworkLost;
        IsProxyNewlyEnabled = isProxyNewlyEnabled;
    }
}

public class NetworkMonitorService
{
    private readonly DispatcherTimer _networkCheckTimer;
    private bool? _lastNetworkAvailable;
    private bool? _lastProxyEnabled;
    private bool _isChecking;

    public event EventHandler<NetworkStatusChangedEventArgs>? NetworkStatusChanged;

    public NetworkMonitorService()
    {
        _networkCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _networkCheckTimer.Tick += async (_, _) => await CheckNetworkAndProxyStatusAsync();
    }

    public void Start() => _networkCheckTimer.Start();

    public void Stop() => _networkCheckTimer.Stop();

    public bool IsEnabled => _networkCheckTimer.IsEnabled;

    public async Task CheckNetworkAndProxyStatusAsync()
    {
        if (_isChecking) return;

        _isChecking = true;
        try
        {
            var currentNetwork = NetworkInterface.GetIsNetworkAvailable();
            var currentProxy = currentNetwork && IsSystemProxyEnabled();

            var isNetworkLost = !currentNetwork && (_lastNetworkAvailable == null || _lastNetworkAvailable == true);
            var isProxyNewlyEnabled = currentNetwork && currentProxy && (_lastProxyEnabled == null || _lastProxyEnabled == false);

            if (isNetworkLost || isProxyNewlyEnabled)
            {
                NetworkStatusChanged?.Invoke(this, new NetworkStatusChangedEventArgs(isNetworkLost, isProxyNewlyEnabled));
            }

            _lastNetworkAvailable = currentNetwork;
            _lastProxyEnabled = currentProxy;
        }
        finally
        {
            _isChecking = false;
        }

        await Task.CompletedTask;
    }

    private static bool IsSystemProxyEnabled()
    {
        using var internetSettings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return internetSettings?.GetValue("ProxyEnable") is int proxyEnable && proxyEnable != 0;
    }
}
