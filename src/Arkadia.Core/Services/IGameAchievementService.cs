using Arkadia.Core.Models;

namespace Arkadia.Core.Services;

public interface IGameAchievementService
{
    Task<IReadOnlyList<GameAchievement>> GetAchievementsAsync(
        GameEntry game,
        string apiKey,
        CancellationToken cancellationToken);
}
