namespace Arkadia.Core.Services;

public interface IStartupTaskService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
    Task SetEnabledAsync(bool enabled, bool startMinimized, CancellationToken cancellationToken);
}
