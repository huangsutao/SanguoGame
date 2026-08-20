using Hangfire;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class CompleteMarchJob
{
    private readonly MarchService _marches;

    public CompleteMarchJob(MarchService marches)
    {
        _marches = marches;
    }

    [AutomaticRetry(Attempts = 5)]
    public Task Execute(long marchId) =>
        _marches.CompleteAsync(marchId, CancellationToken.None);
}
