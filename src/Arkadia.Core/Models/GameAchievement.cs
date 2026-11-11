namespace Arkadia.Core.Models;

public sealed record GameAchievement(
    string Name,
    string Description,
    string? IconUri,
    bool IsUnlocked,
    DateTimeOffset? UnlockedAt);
