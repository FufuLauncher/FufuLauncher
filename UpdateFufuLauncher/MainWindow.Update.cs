/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace Updater;

public partial class MainWindow
{
    #region Update Check

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string localJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Update.json");
            SetLocalizedText(SubtitleText, "RequestingUpdateConfig");
            string updateContent;
            try
            {
                updateContent = await _httpClient.GetStringAsync(ApiEndpoints.UpdateJsonUrl);
            }
            catch (HttpRequestException)
            {
                try
                {
                    updateContent = await _httpClient.GetStringAsync(ApiEndpoints.UpdateJsonFallbackUrl);
                }
                catch (HttpRequestException)
                {
                    if (!File.Exists(localJsonPath))
                    {
                        throw;
                    }

                    SetLocalizedText(SubtitleText, "ReadingLocalUpdateConfig");
                    updateContent = await File.ReadAllTextAsync(localJsonPath);
                }
            }

            JObject updateJson = JObject.Parse(updateContent);
            _officialVersion = updateJson.GetValue("version", StringComparison.OrdinalIgnoreCase)?.ToString() ?? string.Empty;

            if (_isRollbackMode)
            {
                await CheckRollbackAsync();
                return;
            }

            if (_isPreviewMode)
            {
                await CheckPreviewUpdateAsync(updateJson);
                return;
            }

            if (!TryParseVersion(_installedVersion, out Version currentVersion) ||
                !TryParseVersion(_officialVersion, out Version remoteVersion))
            {
                MessageBox.Show(Strings.Get("InvalidVersionMessage"), Strings.Get("InvalidVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
                return;
            }

            if (currentVersion >= remoteVersion)
            {
                if (!_isTestForceUpdate)
                {
                    ShowNoUpdate("NoUpdateMessage", _isInstalledPreviewBuild);
                    return;
                }

                SetLocalizedText(SubtitleText, "TestModeContinuing");
            }

            PrepareOfficialReleaseDownload();
            await PrepareDownloadAsync("SelectDownloadRoute", "DirectGitHubDownload");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Strings.Format("UpdateInitFailed", ApiEndpoints.DownloadPageUrl, ApiEndpoints.DownloadPagePassword, ex.Message),
                Strings.Get("ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private void ShowNoUpdate(string messageKey, bool showRollback = false)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        NoUpdatePanel.Visibility = Visibility.Visible;
        SetLocalizedText(NoUpdateText, messageKey);
        RollbackBtn.Visibility = showRollback ? Visibility.Visible : Visibility.Collapsed;
        SetLocalizedText(SubtitleText, "CheckComplete");
    }

    private void PrepareOfficialReleaseDownload()
    {
        string version = _officialVersion.Trim();
        string fileName = ApiEndpoints.GetInstallerFileName(version);
        _fileName = fileName;
        _targetExeUrl = ApiEndpoints.GetLatestReleaseDownloadUrl(fileName);
        _r2ExeUrl = ApiEndpoints.GetR2InstallerUrl(version);
    }

    private async Task PrepareDownloadAsync(string selectionSubtitleKey, string directSubtitleKey)
    {
        if (_useThirdPartyCDN && !_isPreviewMode)
        {
            SetLocalizedText(SubtitleText, "DetectingPriorityNode");
            if (await TryStartNodeDownloadAsync())
            {
                return;
            }
        }

        if (await TryStartR2DownloadAsync())
        {
            return;
        }

        await PrepareGitHubDownloadAsync(selectionSubtitleKey, directSubtitleKey);
    }

    private async Task PrepareGitHubDownloadAsync(string selectionSubtitleKey, string directSubtitleKey)
    {
        if (_useThirdPartyCDN)
        {
            await ShowMirrorSelectionAsync(selectionSubtitleKey);
        }
        else
        {
            SetLocalizedText(SubtitleText, directSubtitleKey);
            StartMultiThreadDownload(_targetExeUrl!, "DownloadingFromGitHub");
        }
    }

    private async Task<bool> TryStartR2DownloadAsync()
    {
        string? r2Url = _r2ExeUrl;
        if (_isPreviewMode || string.IsNullOrWhiteSpace(r2Url))
        {
            return false;
        }

        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        SetLocalizedText(SubtitleText, "DetectingR2");
        string? resolvedR2Url = await ResolveR2InstallerUrlAsync(r2Url);
        if (resolvedR2Url == null)
        {
            return false;
        }

        _isR2Download = true;
        SetLocalizedText(SubtitleText, "R2Available");
        StartMultiThreadDownload(resolvedR2Url, "DownloadingFromR2");
        return true;
    }

    private async Task<string?> ResolveR2InstallerUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) ||
            parsedUri is null ||
            !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Uri currentUri = parsedUri;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            for (int redirectCount = 0; redirectCount <= 3; redirectCount++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.Range = new RangeHeaderValue(0, 1);

                using var response = await _r2HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    Uri? redirectUri = response.Headers.Location;
                    if (redirectUri == null)
                    {
                        return null;
                    }

                    if (!redirectUri.IsAbsoluteUri)
                    {
                        redirectUri = new Uri(currentUri, redirectUri);
                    }

                    if (!string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    currentUri = redirectUri;
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    return null;
                }

                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null &&
                    (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
                     mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                byte[] signature = new byte[2];
                int read = await stream.ReadAtLeastAsync(signature, signature.Length, throwOnEndOfStream: false, cancellationToken: cts.Token);
                return read == 2 && signature[0] == (byte)'M' && signature[1] == (byte)'Z'
                    ? currentUri.AbsoluteUri
                    : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task FallbackToGitHubPathsAsync(string directFallbackKey)
    {
        if (_useThirdPartyCDN)
        {
            await FallbackToMirrorSelectionAsync();
            return;
        }

        ResetToSelectionUI();
        SetLocalizedText(SubtitleText, directFallbackKey);
    }

    private async Task FallbackFromNodeAsync()
    {
        if (await TryStartR2DownloadAsync())
        {
            return;
        }

        await FallbackToGitHubPathsAsync("NodeAndR2Unavailable");
    }

    private async Task<bool> TryStartNodeDownloadAsync()
    {
        if (_isPreviewMode)
        {
            return false;
        }

        if (!await IsNodeEnabledAsync())
        {
            return false;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var statusResponse = await _httpClient.GetStringAsync(ApiEndpoints.NodeStatusUrl, cts.Token);
            var status = JObject.Parse(statusResponse);

            if (status["download_available"]?.Value<bool>() != true)
            {
                return false;
            }

            if ((status["quota"]?["remaining"]?.Value<long>() ?? 0) <= 0)
            {
                return false;
            }

            string nodeVersion = status["current_version"]?.ToString()?.Trim() ?? string.Empty;
            if (!string.Equals(nodeVersion, _officialVersion.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string downloadPath = status["download_url"]?.ToString()?.Trim() ?? string.Empty;
            if (downloadPath.Length == 0)
            {
                return false;
            }

            string nodeDownloadUrl = ApiEndpoints.GetNodeDownloadUrl(downloadPath);

            _isNodeDownload = true;
            StartMultiThreadDownload(nodeDownloadUrl, "DownloadingFromNode");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsNodeEnabledAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string switchResponse = await _httpClient.GetStringAsync(ApiEndpoints.NodeSwitchUrl, cts.Token);
            JObject switchJson = JObject.Parse(switchResponse);
            return IsStatusEnabled(switchJson.GetValue("Status", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsStatusEnabled(JToken? statusToken)
    {
        if (statusToken == null || statusToken.Type == JTokenType.Null)
        {
            return false;
        }

        if (statusToken.Type == JTokenType.Boolean)
        {
            return statusToken.Value<bool>();
        }

        return bool.TryParse(statusToken.ToString().Trim(), out bool enabled) && enabled;
    }

    private async Task ShowMirrorSelectionAsync(string selectionSubtitleKey)
    {
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        SetLocalizedText(SubtitleText, "MirrorChecking");
        await TestMirrorsAsync();

        LoadingPanel.Visibility = Visibility.Collapsed;
        SelectionPanel.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Visible;
        MirrorListView.ItemsSource = _mirrors;

        if (_mirrors.Count > 0)
        {
            MirrorListView.SelectedIndex = 0;
            SetLocalizedText(SubtitleText, selectionSubtitleKey);
        }
        else
        {
            SetLocalizedText(SubtitleText, "AllMirrorsFailed");
            MessageBox.Show(Strings.Get("AllMirrorsFailedMessage"), Strings.Get("NetworkPoorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task FallbackToMirrorSelectionAsync()
    {
        try
        {
            await ShowMirrorSelectionAsync("SelectDownloadRoute");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Format("MirrorRouteFetchFailed", ex.Message), Strings.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckRollbackAsync()
    {
        SetLocalizedText(SubtitleText, "PreparingRollback");

        string versionLabel = string.IsNullOrEmpty(_officialVersion) ? string.Empty : $" v{_officialVersion}";
        var confirm = MessageBox.Show(
            Strings.Format("RollbackConfirm", versionLabel),
            Strings.Get("RollbackToStable"), MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            Environment.Exit(0);
            return;
        }

        PrepareOfficialReleaseDownload();
        await PrepareDownloadAsync("SelectDownloadRouteRollback", "DirectGitHubDownloadStable");
    }

    private async void RollbackToStable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckRollbackAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Format("RollbackFailed", ex.Message), Strings.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private async Task CheckPreviewUpdateAsync(JObject updateJson)
    {
        string previewVersionStr = updateJson.GetValue("PreReleaseVersion", StringComparison.OrdinalIgnoreCase)?.ToString()?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(previewVersionStr))
        {
            ShowNoUpdate("NoPreviewAvailable", _isInstalledPreviewBuild);
            return;
        }

        if (!TryParseVersion(previewVersionStr, out Version previewVersion))
        {
            MessageBox.Show(Strings.Get("InvalidPreviewVersion"), Strings.Get("InvalidVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(0);
            return;
        }

        string officialVersionStr = updateJson.GetValue("version", StringComparison.OrdinalIgnoreCase)?.ToString();
        if (!TryParseVersion(officialVersionStr, out Version officialVersion))
        {
            MessageBox.Show(Strings.Get("InvalidOfficialVersion"), Strings.Get("InvalidVersionTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(0);
            return;
        }

        if (previewVersion <= officialVersion)
        {
            ShowNoUpdate("PreviewNotNewerMessage", _isInstalledPreviewBuild);
            return;
        }

        SetLocalizedText(SubtitleText, "FetchingGitHubTags");
        var tagsResponse = await _httpClient.GetStringAsync(ApiEndpoints.GithubTagsUrl);
        JArray tags = JArray.Parse(tagsResponse);


        string previewTagName = $"{StripPreReleaseSuffix(previewVersionStr)}Pre-release";
        JToken matchedTag = tags.FirstOrDefault(t => string.Equals(t["name"]?.ToString(), previewTagName, StringComparison.OrdinalIgnoreCase));

        if (matchedTag == null)
        {
            MessageBox.Show(Strings.Format("PreviewPackageNotFound", previewTagName), Strings.Get("PreviewPackageNotFoundTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            Environment.Exit(0);
            return;
        }

        SetLocalizedText(SubtitleText, "FetchingPreviewRelease");
        string tagName = matchedTag["name"]!.ToString();
        string releaseUrl = ApiEndpoints.GetReleaseByTagUrl(tagName);
        var releaseResponse = await _httpClient.GetStringAsync(releaseUrl);
        JObject releaseJson = JObject.Parse(releaseResponse);

        JToken targetAsset = releaseJson["assets"]?.FirstOrDefault(a => a["name"]?.ToString().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

        if (targetAsset == null)
        {
            throw new Exception(Strings.Get("PreviewReleaseAssetMissing"));
        }

        _targetExeUrl = targetAsset["browser_download_url"]!.ToString();
        _fileName = targetAsset["name"]!.ToString();
        _r2ExeUrl = null;


        string digest = targetAsset["digest"]?.ToString();
        if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            _expectedSha256 = digest.Substring("sha256:".Length);
        }

        await PrepareDownloadAsync("SelectDownloadRoutePreview", "DirectGitHubDownloadPreview");
    }

    #endregion

    #region Version Helpers

    private static string StripPreReleaseSuffix(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return string.Empty;

        var trimmed = version.Trim();
        if (trimmed.EndsWith("Pre-release", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - "Pre-release".Length).Trim();
        }
        return trimmed;
    }

    private static bool TryParseVersion(string input, out Version version)
    {
        return Version.TryParse(StripPreReleaseSuffix(input), out version);
    }

    #endregion
}
