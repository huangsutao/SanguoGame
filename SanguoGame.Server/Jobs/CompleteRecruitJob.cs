using Hangfire;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class CompleteRecruitJob
{
    private readonly ArmyService _army;

    public CompleteRecruitJob(ArmyService army)
    {
        _army = army;
    }

    [AutomaticRetry(Attempts = 5)]
    public Task Execute(long cityId, string troopType, int count) =>
        _army.CompleteRecruitAsync(cityId, troopType, count, CancellationToken.None);
}
