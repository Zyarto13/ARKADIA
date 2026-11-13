using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Arkadia.App.Services;

public enum UpdateCheckStatus
{
    NoRelease,
    NotInstalled,
    UpToDate,
    UpdateAvailable
}

public sealed record UpdateRelease(
    Version Version,
    UpdateInfo? VelopackUpdate,
    Uri? InstallerUri,
    Uri? ChecksumUri,
    bool IsLegacyMigration = false);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateRelease? Release = null);

public sealed class GitHubUpdateService
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/Zyarto13/ARKADIA/releases/latest";
    private const string RepositoryUrl = "https://github.com/Zyarto13/ARKADIA";
    private const string UninstallRegistryKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B1E07485-B83F-4F47-BD2A-3921A797B73E}_is1";
    private const string VelopackInstallerName = "ARKADIA-win-Setup.exe";

    private readonly HttpClient _httpClient;
    private readonly Func<UpdateManager> _updateManagerFactory;
    private readonly Func<bool> _legacyInstallState;
    private readonly Func<bool>? _velopackInstallState;

    public GitHubUpdateService(
        HttpClient httpClient,
        Func<UpdateManager>? updateManagerFactory = null,
        Func<bool>? legacyInstallState = null,
        Func<bool>? velopackInstallState = null)
    {
        _httpClient = httpClient;
        _updateManagerFactory = updateManagerFactory ?? CreateUpdateManager;
        _legacyInstallState = legacyInstallState ?? IsLegacyInstalled;
        _velopackInstallState = velopackInstallState;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public bool IsInstalled => _legacyInstallState() || IsVelopackInstalled();

    public bool CanApply(UpdateRelease release) =>
        release.IsLegacyMigration
            ? _legacyInstallState() && GetLegacyUninstallerPath() is not null
            : IsVelopackInstalled();

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_legacyInstallState())
        {
            return await CheckForLegacyMigrationAsync(cancellationToken);
        }

        if (IsVelopackInstalled())
        {
            var updateManager = _updateManagerFactory();
            var update = await updateManager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            return update is null
                ? new UpdateCheckResult(UpdateCheckStatus.UpToDate)
                : new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    new UpdateRelease(
                        update.TargetFullRelease.Version.Version,
                        update,
                        InstallerUri: null,
                        ChecksumUri: null));
        }

        return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);
    }

    public async Task DownloadAndApplyUpdateAsync(
        UpdateRelease release,
        CancellationToken cancellationToken = default)
    {
        if (release.VelopackUpdate is { } velopackUpdate)
        {
            var updateManager = _updateManagerFactory();
            if (!IsVelopackInstalled())
            {
                throw new InvalidOperationException("The Velopack installation is not available.");
            }

            await updateManager.DownloadUpdatesAsync(velopackUpdate, cancelToken: cancellationToken);
            updateManager.ApplyUpdatesAndRestart(velopackUpdate.TargetFullRelease);
            return;
        }

        if (!release.IsLegacyMigration
            || release.InstallerUri is null
            || release.ChecksumUri is null
            || !_legacyInstallState()
            || Environment.ProcessPath is not { } applicationPath
            || GetLegacyUninstallerPath() is not { } uninstallerPath)
        {
            throw new InvalidOperationException("The update cannot be applied to this installation.");
        }

        await DownloadAndLaunchMigrationAsync(
            release,
            applicationPath,
            uninstallerPath,
            cancellationToken);
    }

    private async Task<UpdateCheckResult> CheckForLegacyMigrationAsync(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
        request.Headers.UserAgent.ParseAdd("ARKADIA-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoRelease);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var version = ReadReleaseVersion(root);
        if (version < CurrentVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }

        if (!root.TryGetProperty("assets", out var assetList)
            || assetList.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The latest GitHub release does not contain an asset list.");
        }

        var assets = assetList.EnumerateArray().ToArray();
        return new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            new UpdateRelease(
                version,
                VelopackUpdate: null,
                FindAssetUri(assets, VelopackInstallerName),
                FindAssetUri(assets, $"{VelopackInstallerName}.sha256"),
                IsLegacyMigration: true));
    }

    private async Task DownloadAndLaunchMigrationAsync(
        UpdateRelease release,
        string applicationPath,
        string uninstallerPath,
        CancellationToken cancellationToken)
    {
        var downloadPath = Path.Combine(
            Path.GetTempPath(),
            $"ARKADIA-Migration-{release.Version}-{Guid.NewGuid():N}.exe");
        var helperPath = Path.Combine(
            Path.GetTempPath(),
            $"ARKADIA-Migration-{Guid.NewGuid():N}.ps1");

        try
        {
            await DownloadAssetAsync(release.InstallerUri!, downloadPath, cancellationToken);
            await VerifyChecksumAsync(downloadPath, release.ChecksumUri!, cancellationToken);
            await File.WriteAllTextAsync(
                helperPath,
                CreateMigrationHelper(downloadPath, applicationPath, uninstallerPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var startInfo = CreatePowerShellStartInfo(helperPath);
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("The migration helper could not be started.");
            }
        }
        catch
        {
            TryDelete(downloadPath);
            TryDelete(helperPath);
            throw;
        }
    }

    private static Version ReadReleaseVersion(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("The latest GitHub release is missing its version tag.");
        }

        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag)
            || !Version.TryParse(tag.TrimStart('v', 'V'), out var version))
        {
            throw new InvalidDataException("The latest GitHub release has an invalid version tag.");
        }

        return version;
    }

    private static UpdateManager CreateUpdateManager() =>
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    private bool IsVelopackInstalled() =>
        _velopackInstallState?.Invoke() ?? _updateManagerFactory().IsInstalled;

    private static Uri FindAssetUri(JsonElement[] assets, string assetName)
    {
        foreach (var asset in assets)
        {
            if (!asset.TryGetProperty("name", out var nameElement)
                || !string.Equals(nameElement.GetString(), assetName, StringComparison.Ordinal))
            {
                continue;
            }

            var address = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            throw new InvalidDataException($"The GitHub release asset '{assetName}' has an invalid URL.");
        }

        throw new InvalidDataException($"The GitHub release is missing the required asset '{assetName}'.");
    }

    private async Task DownloadAssetAsync(
        Uri address,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            address,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
    }

    private async Task VerifyChecksumAsync(
        string installerPath,
        Uri checksumAddress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(checksumAddress, cancellationToken);
        response.EnsureSuccessStatusCode();
        var checksumText = await response.Content.ReadAsStringAsync(cancellationToken);
        var expectedHash = checksumText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (expectedHash is null
            || expectedHash.Length != 64
            || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The update checksum file is invalid.");
        }

        await using var installer = File.OpenRead(installerPath);
        var actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(installer, cancellationToken));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHash),
                Convert.FromHexString(actualHash)))
        {
            throw new InvalidDataException("The downloaded update failed its SHA-256 checksum.");
        }
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string helperPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);
        return startInfo;
    }

    private static string CreateMigrationHelper(
        string installerPath,
        string applicationPath,
        string uninstallerPath)
    {
        var encodedInstallerPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(installerPath));
        var encodedApplicationPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(applicationPath));
        var encodedUninstallerPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(uninstallerPath));
        var installedExecutablePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ARKADIA",
            "Arkadia.App.exe");
        var encodedInstalledExecutablePath =
            Convert.ToBase64String(Encoding.UTF8.GetBytes(installedExecutablePath));

        return $$"""
            $ErrorActionPreference = 'Stop'
            $installerPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedInstallerPath}}'))
            $applicationPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedApplicationPath}}'))
            $uninstallerPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedUninstallerPath}}'))
            $installedExecutablePath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedInstalledExecutablePath}}'))
            $migrationSucceeded = $false
            try {
                Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                $uninstall = Start-Process -FilePath $uninstallerPath -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') -Wait -PassThru
                if ($uninstall.ExitCode -ne 0) {
                    throw "Legacy uninstaller exited with code $($uninstall.ExitCode)."
                }

                $setup = Start-Process -FilePath $installerPath -ArgumentList @('--silent') -Wait -PassThru
                if ($setup.ExitCode -ne 0) {
                    throw "Velopack setup exited with code $($setup.ExitCode)."
                }
                $migrationSucceeded = $true
            }
            catch {
                $logDirectory = Join-Path $env:APPDATA 'Arkadia\logs'
                New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
                Add-Content -LiteralPath (Join-Path $logDirectory 'update.log') -Value "$(Get-Date -Format o) Migration failed: $_"
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show(
                    'ARKADIA could not be migrated. The downloaded setup has been kept in the temporary folder. See %AppData%\Arkadia\logs\update.log for details.',
                    'ARKADIA update',
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
            }
            finally {
                if ($migrationSucceeded -and (Test-Path -LiteralPath $installedExecutablePath)) {
                    Start-Process -FilePath $installedExecutablePath -WorkingDirectory (Split-Path -Parent $installedExecutablePath)
                }
                elseif (Test-Path -LiteralPath $applicationPath) {
                    Start-Process -FilePath $applicationPath -WorkingDirectory (Split-Path -Parent $applicationPath)
                }

                if ($migrationSucceeded) {
                    Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
                }
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            }
            """;
    }

    private static bool IsLegacyInstalled() =>
        IsRunningRegisteredExecutable() && GetLegacyUninstallerPath() is not null;

    private static bool IsRunningRegisteredExecutable()
    {
        var registeredExecutable = GetRegisteredExecutablePath();
        var runningExecutable = Environment.ProcessPath;
        return registeredExecutable is not null
            && runningExecutable is not null
            && string.Equals(
                Path.GetFullPath(registeredExecutable),
                Path.GetFullPath(runningExecutable),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLegacyUninstallerPath()
    {
        if (!IsRunningRegisteredExecutable()
            || GetRegisteredExecutablePath() is not { } registeredExecutable)
        {
            return null;
        }

        var uninstallerPath = Path.Combine(
            Path.GetDirectoryName(registeredExecutable)!,
            "unins000.exe");
        return File.Exists(uninstallerPath) ? uninstallerPath : null;
    }

    private static string? GetRegisteredExecutablePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var uninstallKey = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey);
        if (uninstallKey?.GetValue("DisplayIcon") is not string displayIcon
            || string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var executablePath = displayIcon.Trim().Trim('"');
        var iconIndexSeparator = executablePath.LastIndexOf(',');
        if (iconIndexSeparator >= 0
            && int.TryParse(executablePath[(iconIndexSeparator + 1)..], out _))
        {
            executablePath = executablePath[..iconIndexSeparator].Trim().Trim('"');
        }

        return File.Exists(executablePath) ? executablePath : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            Log.Warning("Could not remove temporary update file {Path}.", path);
        }
        catch (UnauthorizedAccessException)
        {
            Log.Warning("Access was denied while removing temporary update file {Path}.", path);
        }
    }
}
