using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Updater;
using Xunit;

namespace UpdateFufuLauncher.Tests;

public sealed class UpdateConfigTests
{
    private const string BackupWithPreview = """{"Version":"2.0","PreReleaseVersion":"2.1Pre-release"}""";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task StableAndRollbackIgnoreInvalidPreviewMetadata(bool isPreviewMode, bool isRollbackMode)
    {
        var handler = new ConfigHandler(() => """{"Version":"1.0","PreReleaseVersion":123}""");
        using var client = new HttpClient(handler);

        var config = await MainWindow.LoadUpdateConfigAsync(client, isPreviewMode, isRollbackMode, () => { });

        Assert.Equal(new Version(1, 0), config.Stable.Number);
        Assert.Null(config.Preview);
        Assert.Equal(new[] { ApiEndpoints.UpdateJsonUrl }, handler.RequestedUrls);
    }

    [Theory]
    [InlineData("""{"Version":"1.0"}""")]
    [InlineData("""{"Version":"1.0","PreReleaseVersion":null}""")]
    [InlineData("""{"Version":"1.0","PreReleaseVersion":123}""")]
    public async Task UnusablePreviewMetadataReadsTheBackup(string primary)
    {
        var handler = new ConfigHandler(() => primary, BackupWithPreview);
        using var client = new HttpClient(handler);

        var config = await MainWindow.LoadUpdateConfigAsync(client, true, false, () => { });

        Assert.Equal(new Version(2, 0), config.Stable.Number);
        Assert.Equal(new Version(2, 1), config.Preview?.Number);
        Assert.Equal(new[] { ApiEndpoints.UpdateJsonUrl, ApiEndpoints.UpdateJsonFallbackUrl }, handler.RequestedUrls);
    }

    [Fact]
    public async Task ExplicitlyEmptyPreviewMeansNoPreviewEvenWhenBackupHasOne()
    {
        var handler = new ConfigHandler(() => """{"Version":"1.0","PreReleaseVersion":""}""", BackupWithPreview);
        using var client = new HttpClient(handler);

        var config = await MainWindow.LoadUpdateConfigAsync(client, true, false, () => { });

        Assert.Equal(new Version(1, 0), config.Stable.Number);
        Assert.Null(config.Preview);
        Assert.Equal(new[] { ApiEndpoints.UpdateJsonUrl }, handler.RequestedUrls);
    }

    [Fact]
    public async Task MissingAndNullPreviewAcrossBothSitesReportFailure()
    {
        var handler = new ConfigHandler(() => """{"Version":"1.0"}""", """{"Version":"2.0","PreReleaseVersion":null}""");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<JsonException>(() => MainWindow.LoadUpdateConfigAsync(client, true, false, () => { }));

        Assert.Equal(new[] { ApiEndpoints.UpdateJsonUrl, ApiEndpoints.UpdateJsonFallbackUrl }, handler.RequestedUrls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrimaryCancellationDoesNotRequireAnInnerTimeoutException(bool hasTransportError)
    {
        var cancellation = new TaskCanceledException("Request canceled",
            hasTransportError ? new HttpRequestException("Connection closed") : null);
        var handler = new ConfigHandler(() => throw cancellation, BackupWithPreview);
        using var client = new HttpClient(handler);

        var config = await MainWindow.LoadUpdateConfigAsync(client, true, false, () => { });

        Assert.Equal(new Version(2, 1), config.Preview?.Number);
        Assert.Equal(new[] { ApiEndpoints.UpdateJsonUrl, ApiEndpoints.UpdateJsonFallbackUrl }, handler.RequestedUrls);
    }

    private sealed class ConfigHandler(Func<string> primary, string? backup = null) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            RequestedUrls.Add(url);
            var content = url switch
            {
                ApiEndpoints.UpdateJsonUrl => primary(),
                ApiEndpoints.UpdateJsonFallbackUrl when backup != null => backup,
                _ => throw new InvalidOperationException($"Unexpected request to {url}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
}
