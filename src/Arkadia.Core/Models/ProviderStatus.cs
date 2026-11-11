namespace Arkadia.Core.Models;

public sealed record ProviderStatus(bool IsAvailable, string? DisplayName, string? Message);
