using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Arkadia.App.Services;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsyncDoesNotContactGitHubWhenNotInstalled()
    {
        using var client = CreateClient((_, _) =>
            throw new InvalidOperationException("GitHub should not be queried for an unpackaged app."));
        var service = new GitHubUpdateService(
            client,
            legacyInstallState: () => false,
            velopackInstallState: () => false);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.NotInstalled, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncReportsWhenNoReleaseExistsForLegacyInstall()
    {
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new GitHubUpdateService(
            client,
            legacyInstallState: () => true,
            velopackInstallState: () => false);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.NoRelease, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncReturnsVelopackSetupForLegacyMigration()
    {
        var version = GitHubUpdateService.CurrentVersion;
        var json = CreateReleaseJson(version, "ARKADIA-win-Setup.exe");
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));
        var service = new GitHubUpdateService(
            client,
            legacyInstallState: () => true,
            velopackInstallState: () => false);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Release);
        Assert.Equal(version, result.Release.Version);
        Assert.True(result.Release.IsLegacyMigration);
        Assert.Null(result.Release.VelopackUpdate);
        Assert.EndsWith("ARKADIA-win-Setup.exe", result.Release.InstallerUri!.AbsolutePath);
        Assert.EndsWith(
            "ARKADIA-win-Setup.exe.sha256",
            result.Release.ChecksumUri!.AbsolutePath);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncRejectsMigrationAssetsFromNonGitHubHosts()
    {
        var version = GitHubUpdateService.CurrentVersion;
        var json = CreateReleaseJson(version, "ARKADIA-win-Setup.exe", "https://example.com/setup.exe");
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));
        var service = new GitHubUpdateService(
            client,
            legacyInstallState: () => true,
            velopackInstallState: () => false);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdatesAsync());
    }

    private static string CreateReleaseJson(
        Version version,
        string installerName,
        string? installerUrl = null)
    {
        installerUrl ??=
            $"https://github.com/Zyarto13/ARKADIA/releases/download/v{version}/{installerName}";
        var checksumName = $"{installerName}.sha256";
        return $$"""
            {
              "tag_name": "v{{version}}",
              "assets": [
                {
                  "name": "{{installerName}}",
                  "browser_download_url": "{{installerUrl}}"
                },
                {
                  "name": "{{checksumName}}",
                  "browser_download_url": "https://github.com/Zyarto13/ARKADIA/releases/download/v{{version}}/{{checksumName}}"
                }
              ]
            }
            """;
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
