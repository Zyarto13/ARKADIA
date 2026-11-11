using System.Net;
using System.Net.Http;
using System.Text;
using Arkadia.App.Services;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsyncReportsWhenNoReleaseExists()
    {
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new GitHubUpdateService(client);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.NoRelease, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncReturnsInstallerAndChecksumForANewerRelease()
    {
        var currentVersion = GitHubUpdateService.CurrentVersion;
        var version = new Version(
            currentVersion.Major,
            currentVersion.Minor,
            currentVersion.Build + 1);
        var installerName = $"ARKADIA-Setup-{version}-x64.exe";
        var json = $$"""
            {
              "tag_name": "v{{version}}",
              "assets": [
                {
                  "name": "{{installerName}}",
                  "browser_download_url": "https://github.com/Zyarto13/ARKADIA/releases/download/v{{version}}/{{installerName}}"
                },
                {
                  "name": "{{installerName}}.sha256",
                  "browser_download_url": "https://github.com/Zyarto13/ARKADIA/releases/download/v{{version}}/{{installerName}}.sha256"
                }
              ]
            }
            """;
        using var client = CreateClient((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        var service = new GitHubUpdateService(client);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Release);
        Assert.Equal(version, result.Release.Version);
        Assert.EndsWith(".exe", result.Release.InstallerUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".exe.sha256", result.Release.ChecksumUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncReportsCurrentVersionAsUpToDate()
    {
        var version = GitHubUpdateService.CurrentVersion;
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"tag_name":"v{{version.ToString(3)}}","assets":[]}""",
                    Encoding.UTF8,
                    "application/json")
            }));
        var service = new GitHubUpdateService(client);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
