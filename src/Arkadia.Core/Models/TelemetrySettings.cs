namespace Arkadia.Core.Models;

public sealed record TelemetrySettings(
    bool CrashReports = false,
    bool UsageStatistics = false,
    bool Performance = false,
    string Endpoint = "",
    string InstallationId = "");
