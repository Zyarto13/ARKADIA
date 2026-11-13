namespace Arkadia.Core.Models;

public sealed record GameEntry(
    string ProviderId,
    string ExternalId,
    string Title,
    string? InstallPath,
    bool IsInstalled,
    bool IsOwned,
    int? PlaytimeMinutes = null,
    string? ArtworkFilePath = null)
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
            ? new Uri($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{ExternalId}/header.jpg")
            : null;

    public IReadOnlyList<Uri> ArtworkUris =>
        ProviderId == "steam" && uint.TryParse(ExternalId, out _)
            ?
            [
                new Uri($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{ExternalId}/header.jpg"),
                new Uri($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{ExternalId}/capsule_616x353.jpg"),
                new Uri($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{ExternalId}/library_600x900.jpg"),
                new Uri($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{ExternalId}/library_hero.jpg"),
                new Uri($"https://cdn.akamai.steamstatic.com/steam/apps/{ExternalId}/header.jpg"),
                new Uri($"https://cdn.akamai.steamstatic.com/steam/apps/{ExternalId}/library_600x900.jpg")
            ]
            : [];

    public string ProviderDisplayName => ProviderId switch
    {
        "steam" => "Steam",
        "epic" => "Epic Games",
        "minecraft" => "Minecraft",
        "curseforge" => "CurseForge",
        "rinaorc" => "Rinaorc",
        "manual" => "Manual",
        _ => ProviderId
    };
}
