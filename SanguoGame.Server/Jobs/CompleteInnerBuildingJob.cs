using Hangfire;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class CompleteInnerBuildingJob
{
    private readonly BuildingService _buildings;

    public CompleteInnerBuildingJob(BuildingService buildings)
    {
        _buildings = buildings;
    }

    [AutomaticRetry(Attempts = 5)]
    public Task Execute(long cityId, string buildingType, int targetLevel) =>
        _buildings.CompleteAsync(cityId, buildingType, targetLevel, CancellationToken.None);
}
