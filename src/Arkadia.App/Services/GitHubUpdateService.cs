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

namespace Arkadia.App.Services;

public enum UpdateCheckStatus
{
    NoRelease,
    UpToDate,
    UpdateAvailable
}

public sealed record UpdateRelease(Version Version, Uri InstallerUri, Uri ChecksumUri);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateRelease? Release = null);

public sealed class GitHubUpdateService(HttpClient httpClient)
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/Zyarto13/ARKADIA/releases/latest";
    private const string UninstallRegistryKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B1E07485-B83F-4F47-BD2A-3921A797B73E}_is1";
    private const string InstallerPrefix = "ARKADIA-Setup-";
    private const string InstallerSuffix = "-x64.exe";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static bool IsInstalled
    {
        get
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
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
        request.Headers.UserAgent.ParseAdd("ARKADIA-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoRelease);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
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

        if (version <= CurrentVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }

        var installerName = $"{InstallerPrefix}{version}{InstallerSuffix}";
        if (!root.TryGetProperty("assets", out var assetList)
            || assetList.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The latest GitHub release does not contain an asset list.");
        }

        var assets = assetList.EnumerateArray().ToArray();
        var installerUri = FindAssetUri(assets, installerName);
        var checksumUri = FindAssetUri(assets, $"{installerName}.sha256");

        return new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            new UpdateRelease(version, installerUri, checksumUri));
    }

    public async Task DownloadAndLaunchInstallerAsync(
        UpdateRelease release,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled || Environment.ProcessPath is not { } applicationPath)
        {
            throw new InvalidOperationException("Automatic updates require the installed ARKADIA application.");
        }

        var downloadPath = Path.Combine(
            Path.GetTempPath(),
            $"ARKADIA-Update-{release.Version}-{Guid.NewGuid():N}.exe");
        var helperPath = Path.Combine(
            Path.GetTempPath(),
            $"ARKADIA-Update-{Guid.NewGuid():N}.ps1");

        try
        {
            await DownloadAssetAsync(release.InstallerUri, downloadPath, cancellationToken);
            await VerifyChecksumAsync(downloadPath, release.ChecksumUri, cancellationToken);
            await File.WriteAllTextAsync(
                helperPath,
                CreateUpdateHelper(downloadPath, applicationPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

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

            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("The update helper could not be started.");
            }
        }
        catch
        {
            TryDelete(downloadPath);
            TryDelete(helperPath);
            throw;
        }
    }

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
        using var response = await httpClient.GetAsync(
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
        using var response = await httpClient.GetAsync(checksumAddress, cancellationToken);
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

    private static string CreateUpdateHelper(string installerPath, string applicationPath)
    {
        var encodedInstallerPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(installerPath));
        var encodedApplicationPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(applicationPath));
        return $$"""
            $ErrorActionPreference = 'Stop'
            $installerPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedInstallerPath}}'))
            $applicationPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedApplicationPath}}'))
            try {
                Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                $installDirectory = Split-Path -Parent $applicationPath
                $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CLOSEAPPLICATIONS', ('/DIR="' + $installDirectory + '"'))
                $result = Start-Process -FilePath $installerPath -ArgumentList $arguments -Wait -PassThru
                if ($result.ExitCode -ne 0) {
                    throw "Installer exited with code $($result.ExitCode)."
                }
            }
            catch {
                $logDirectory = Join-Path $env:APPDATA 'Arkadia\logs'
                New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
                Add-Content -LiteralPath (Join-Path $logDirectory 'update.log') -Value "$(Get-Date -Format o) $_"
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show(
                    'ARKADIA could not be updated. The previous version will be reopened. See %AppData%\Arkadia\logs\update.log for details.',
                    'ARKADIA update',
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
            }
            finally {
                Start-Process -FilePath $applicationPath -WorkingDirectory (Split-Path -Parent $applicationPath)
                Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            }
            """;
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
