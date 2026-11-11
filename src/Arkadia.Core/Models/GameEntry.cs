namespace Arkadia.Core.Models;

public sealed record GameEntry(
    string ProviderId,
    string ExternalId,
    string Title,
    string? InstallPath,
    bool IsInstalled,
    bool IsOwned,
    int? PlaytimeMinutes = null)
{
    public bool HasPlaytime => PlaytimeMinutes is not null;

    public string PlaytimeText => PlaytimeMinutes switch
    {
        null => string.Empty,
        < 60 => "<1 h",
        var minutes => $"{minutes.Value / 60d:0.#} h"
    };

    public Uri? ArtworkUri =>
        ProviderId == "steam" && uint.TryParse(ExternalId, out _)
            ? new Uri($"https://cdn.akamai.steamstatic.com/steam/apps/{ExternalId}/header.jpg")
            : null;

    public IReadOnlyList<Uri> ArtworkUris =>
        ProviderId == "steam" && uint.TryParse(ExternalId, out _)
            ?
            [
                new Uri($"https://cdn.akamai.steamstatic.com/steam/apps/{ExternalId}/header.jpg"),
                new Uri($"https://steamcdn-a.akamaihd.net/steam/apps/{ExternalId}/header.jpg")
            ]
            : [];

    public string ProviderDisplayName => ProviderId switch
    {
        "steam" => "Steam",
        "epic" => "Epic Games",
        _ => ProviderId
    };
}
