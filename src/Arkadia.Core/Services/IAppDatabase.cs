namespace Arkadia.Core.Services;

public interface IAppDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> QuickCheckAsync(CancellationToken cancellationToken);
}
