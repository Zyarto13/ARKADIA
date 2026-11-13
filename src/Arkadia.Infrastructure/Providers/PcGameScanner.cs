using Arkadia.Core.Models;

namespace Arkadia.Infrastructure.Providers;

public sealed record PcGameScanResult(
    IReadOnlyList<GameScanCandidate> Candidates,
    int SkippedDirectories,
    bool WasTruncated);

public static class PcGameScanner
{
    private const int MaximumDirectories = 10_000;
    private const int MaximumCandidates = 2_000;
    private const int MaximumDepth = 8;

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "System Volume Information",
        "Windows",
        "WindowsApps"
    };

    private static readonly HashSet<string> GameMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityPlayer.dll",
        "GameAssembly.dll",
        "steam_api.dll",
        "steam_api64.dll"
    };

    public static Task<PcGameScanResult> ScanAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A scan directory is required.", nameof(rootDirectory));
        }

        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The scan directory '{root}' does not exist.");
        }

        var pendingDirectories = new Queue<(string Path, int Depth)>();
        pendingDirectories.Enqueue((root, 0));
        var candidates = new Dictionary<string, GameScanCandidate>(StringComparer.OrdinalIgnoreCase);
        var scannedDirectories = 0;
        var skippedDirectories = 0;
        var wasTruncated = false;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scannedDirectories >= MaximumDirectories || candidates.Count >= MaximumCandidates)
            {
                wasTruncated = true;
                break;
            }

            var (directory, depth) = pendingDirectories.Dequeue();
            scannedDirectories++;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (ContainsGameMarker(directory))
                {
                    foreach (var executable in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!IsGameExecutable(executable))
                        {
                            continue;
                        }

                        candidates.TryAdd(
                            Path.GetFullPath(executable),
                            new GameScanCandidate(Path.GetFileNameWithoutExtension(executable), Path.GetFullPath(executable)));
                    }
                }

                if (depth >= MaximumDepth)
                {
                    continue;
                }

                foreach (var childDirectory in Directory.EnumerateDirectories(directory))
                {
                    var childName = Path.GetFileName(childDirectory);
                    if (!ExcludedDirectoryNames.Contains(childName))
                    {
                        pendingDirectories.Enqueue((childDirectory, depth + 1));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                skippedDirectories++;
            }
            catch (IOException)
            {
                skippedDirectories++;
            }
        }

        IReadOnlyList<GameScanCandidate> orderedCandidates = candidates.Values
            .OrderBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult(new PcGameScanResult(orderedCandidates, skippedDirectories, wasTruncated));
    }

    private static bool ContainsGameMarker(string directory)
    {
        foreach (var marker in GameMarkers)
        {
            if (File.Exists(Path.Combine(directory, marker)))
            {
                return true;
            }
        }

        var contentPaks = Path.Combine(directory, "Content", "Paks");
        if (Directory.Exists(contentPaks))
        {
            try
            {
                if (Directory.EnumerateFiles(contentPaks, "*.pak", SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        return Directory.Exists(Path.Combine(directory, "UnrealEngine"));
    }

    private static bool IsGameExecutable(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        return !fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains("installer", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains("crash", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, "unrealcefsubprocess", StringComparison.OrdinalIgnoreCase);
    }
}
