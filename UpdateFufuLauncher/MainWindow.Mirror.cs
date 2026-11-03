/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Updater;

public partial class MainWindow
{
    #region Mirror Nodes

    private async Task TestMirrorsAsync()
    {
        string[] proxyDomains = {
            "gh.llkk.cc", "github.dpik.top", "gh-proxy.com", "ghfile.geekertao.top",
            "github-proxy.memory-echoes.cn", "cdn.gh-proxy.com", "github.tbedu.top",
            "gh.927223.xyz", "gh.bugdey.us.kg", "git.yylx.win", "cdn.akaere.online",
            "ghproxy.net", "down.mxw.xx.kg", "down.mxw.qzz.io", "github.mxw.qzz.io",
            "gh.dpik.top", "gh.acmsz.top", "gitproxy.127731.xyz", "gh.jjj.gv.uy",
            "github.chenc.dev", "gh.felicity.ac.cn", "gh.inkchills.cn", "gh.jasonzeng.dev",
            "gh.ddlc.top", "gp.zkitefly.eu.org", "gitproxy.mrhjx.cn", "ghfast.top",
            "fastgit.cc", "gh.sixyin.com", "ghp.arslantu.xyz", "github.ednovas.xyz",
            "ghproxy.cxkpro.top", "gh.idayer.com", "gh-proxy.net",
            "j.1lin.dpdns.org", "github.starrlzy.cn", "ghm.078465.xyz", "ghf.无名氏.top",
            "jiashu.1win.eu.org", "tvv.tw", "j.1win.ggff.net", "gh.catmak.name",
            "gh.b52m.cn", "slink.ltd", "github.tmby.shop", "ghpr.cc", "gh.tryxd.cn",
            "gh.monlor.com", "ghpxy.hwinzniej.top", "git.669966.xyz",
            "github.geekery.cn", "ghproxy.1888866.xyz", "github.xxlab.tech", "free.cn.eu.org",
            "gh.chjina.com", "ghp.keleyaa.com", "proxy.yaoyaoling.net", "ghproxy.monkeyray.net",
            "gh.noki.icu", "g.blfrp.cn"
        };

        int completed = 0;
        var tasks = proxyDomains.Select(async domain =>
        {
            try
            {
                string testProxyUrl = ApiEndpoints.GetMirrorUrl(domain, ApiEndpoints.TestFileUrl);
                string exeProxyUrl = ApiEndpoints.GetMirrorUrl(domain, _targetExeUrl!);

                var result = await VerifyAndMeasureNodeAsync(domain, testProxyUrl, exeProxyUrl);

                int current = Interlocked.Increment(ref completed);
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (TestStatusText != null)
                    {
                        SetLocalizedText(TestStatusText, "MirrorTestProgress", current, proxyDomains.Length);
                    }
                });

                return result;
            }
            catch
            {
                Interlocked.Increment(ref completed);
                return new MirrorInfo { Domain = domain, IsSuccess = false, ResponseTimeMs = long.MaxValue };
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        _mirrors = results.Where(r => r.IsSuccess).OrderBy(r => r.ResponseTimeMs).ToList();
    }

    private async Task<MirrorInfo> VerifyAndMeasureNodeAsync(string domain, string testUrl, string exeUrl)
    {
        var info = new MirrorInfo { Domain = domain, FullUrl = exeUrl, IsSuccess = false, ResponseTimeMs = long.MaxValue };
        var stopwatch = new Stopwatch();
        try
        {
            stopwatch.Start();
            using var response = await _httpClient.GetAsync(testUrl, HttpCompletionOption.ResponseContentRead);
            response.EnsureSuccessStatusCode();

            byte[] fileData = await response.Content.ReadAsByteArrayAsync();
            stopwatch.Stop();

            string hash = CalculateMD5(fileData);
            if (hash.Equals(ExpectedTestFileMD5, StringComparison.OrdinalIgnoreCase))
            {
                info.IsSuccess = true;
                info.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                double speedKbps = (fileData.Length / 1024.0) / (info.ResponseTimeMs / 1000.0);
                info.SpeedKey = speedKbps > 500 ? "MirrorSpeedExcellent" : "MirrorSpeedNormal";
            }
        }
        catch
        {
            stopwatch.Stop();
            info.IsSuccess = false;
            info.ResponseTimeMs = long.MaxValue;
        }
        return info;
    }

    private string CalculateMD5(byte[] data)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hashBytes = md5.ComputeHash(data);
            StringBuilder sb = new();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }

    #endregion
}

public class MirrorInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Domain { get; set; } = string.Empty;
    public string FullUrl { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public long ResponseTimeMs { get; set; }
    public string SpeedKey { get; set; } = string.Empty;

    public string StatusDesc => string.IsNullOrEmpty(SpeedKey)
        ? string.Empty
        : Strings.Format("MirrorStatusDesc", ResponseTimeMs, Strings.Get(SpeedKey));

    public void RefreshStatusDesc()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusDesc)));
    }
}
