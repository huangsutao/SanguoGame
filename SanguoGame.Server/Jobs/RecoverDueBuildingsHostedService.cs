using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class RecoverDueBuildingsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RecoverDueBuildingsHostedService> _logger;

    public RecoverDueBuildingsHostedService(
        IServiceScopeFactory scopes,
        ILogger<RecoverDueBuildingsHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
            await buildings.RecoverDueAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "补结算到期建筑失败");
        }
    }
}
