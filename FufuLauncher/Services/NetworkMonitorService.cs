using System.Net;
using System.Net.NetworkInformation;
using Microsoft.UI.Xaml;
using FufuLauncher.Constants;

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

    public event EventHandler<NetworkStatusChangedEventArgs>? NetworkStatusChanged;

    public NetworkMonitorService()
    {
        _networkCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _networkCheckTimer.Tick += async (_, _) => await CheckNetworkAndProxyStatusAsync();
    }

    public void Start() => _networkCheckTimer.Start();

    public void Stop() => _networkCheckTimer.Stop();

    public bool IsEnabled => _networkCheckTimer.IsEnabled;

    public async Task CheckNetworkAndProxyStatusAsync()
    {
        var (currentNetwork, currentProxy) = await Task.Run(() =>
        {
            var isNet = NetworkInterface.GetIsNetworkAvailable();
            var isProxy = false;
            
            if (isNet)
            {
                try
                {
                    var proxy = WebRequest.GetSystemWebProxy();
                    Uri resource = new(ApiEndpoints.MicrosoftNetworkCheckUrl);
                    isProxy = !proxy.IsBypassed(resource);
                }
                catch 
                { 
                    isProxy = false; 
                }
            }
            return (isNet, isProxy);
        });

        var isNetworkLost = !currentNetwork && (_lastNetworkAvailable == null || _lastNetworkAvailable == true);
        var isProxyNewlyEnabled = currentNetwork && currentProxy && (_lastProxyEnabled == null || _lastProxyEnabled == false);

        if (isNetworkLost || isProxyNewlyEnabled)
        {
            NetworkStatusChanged?.Invoke(this, new NetworkStatusChangedEventArgs(isNetworkLost, isProxyNewlyEnabled));
        }

        _lastNetworkAvailable = currentNetwork;
        _lastProxyEnabled = currentProxy;
    }
}