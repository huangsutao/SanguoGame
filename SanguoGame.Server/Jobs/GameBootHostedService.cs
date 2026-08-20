using SanguoGame.Server.Services;

namespace SanguoGame.Server.Jobs;

public sealed class GameBootHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameBootHostedService> _logger;

    public GameBootHostedService(IServiceScopeFactory scopes, ILogger<GameBootHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
            var marches = scope.ServiceProvider.GetRequiredService<MarchService>();
            var seed = scope.ServiceProvider.GetRequiredService<SeedService>();
            var world = scope.ServiceProvider.GetRequiredService<WorldService>();
            await buildings.RecoverDueAsync(stoppingToken);
            await marches.RecoverDueAsync(stoppingToken);
            await world.RecoverDueOutpostsAsync(stoppingToken);
            await seed.EnsureWorldAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动补结算或世界种子失败");
        }
    }
}
