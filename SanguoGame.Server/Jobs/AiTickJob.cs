using Hangfire;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class AiTickJob
{
    private readonly AiService _ai;

    public AiTickJob(AiService ai)
    {
        _ai = ai;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(600)]
    public Task Execute() =>
        _ai.TickAsync(CancellationToken.None);
}
