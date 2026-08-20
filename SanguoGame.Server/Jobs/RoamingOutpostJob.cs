using Hangfire;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class RoamingOutpostJob
{
    private readonly WorldService _world;

    public RoamingOutpostJob(WorldService world)
    {
        _world = world;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(120)]
    public Task Execute() =>
        _world.TickRoamingAsync(CancellationToken.None);
}
