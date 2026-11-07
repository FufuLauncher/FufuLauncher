/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace Updater;

public static class ApiEndpoints
{
    public const string UpdateJsonUrl = "https://fufu.skin/Update.json";
    public const string UpdateJsonFallbackUrl = "https://fu1.fun/Update.json";

    public const string GithubApiBase = "https://api.github.com";
    public const string GithubWebBase = "https://github.com";
    public const string GithubRepository = "FufuLauncher/FufuLauncher";
    public const string GithubTagsUrl = GithubApiBase + "/repos/" + GithubRepository + "/tags?per_page=100";

    public const string NodeBaseUrl = "https://fufu.668851.xyz";
    public const string NodeStatusUrl = NodeBaseUrl + "/api/status";
    public const string R2BaseUrl = "https://cdn.fufu.skin";

    public const string TestFileUrl = "https://raw.githubusercontent.com/moodlehq/moodle-exttests/master/test.html";
    public const string DownloadPageUrl = "https://wwaoi.lanzn.com/b00wnb99ef";
    public const string DownloadPagePassword = "6hnh";

    public static string GetInstallerFileName(string version) => $"FufuLauncher_Setup_v{version}.exe";

    public static string GetLatestReleaseDownloadUrl(string fileName) =>
        $"{GithubWebBase}/{GithubRepository}/releases/latest/download/{fileName}";

    public static string GetReleaseByTagUrl(string tagName) =>
        $"{GithubApiBase}/repos/{GithubRepository}/releases/tags/{Uri.EscapeDataString(tagName)}";

    public static string GetR2InstallerUrl(string version) =>
        $"{R2BaseUrl}/{GetInstallerFileName(version)}";

    public static string GetNodeDownloadUrl(string downloadPath) =>
        downloadPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? downloadPath
            : $"{NodeBaseUrl}/{(downloadPath.StartsWith('/') ? downloadPath.Substring(1) : downloadPath)}";

    public static string GetMirrorUrl(string domain, string targetUrl) => $"https://{domain}/{targetUrl}";
}
