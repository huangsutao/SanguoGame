using Hangfire;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class CompleteTransportJob
{
    private readonly TransportService _transports;

    public CompleteTransportJob(TransportService transports)
    {
        _transports = transports;
    }

    [AutomaticRetry(Attempts = 5)]
    public Task Execute(long transportId) =>
        _transports.CompleteAsync(transportId, CancellationToken.None);
}
