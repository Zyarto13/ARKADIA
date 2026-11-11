namespace Arkadia.Core.Models;

public sealed record LaunchResult(bool Succeeded, string? ErrorMessage = null);
