using System.Text.Json;
using System.Text.Json.Serialization;
using FreeSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server;
using SanguoGame.Server.Services;
using Testcontainers.PostgreSql;
using Xunit;

namespace SanguoGame.Tests.Api;

public sealed class GameApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";

    public bool Available { get; private set; }

    public string? UnavailableReason { get; private set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task InitializeAsync()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            _connectionString = fromEnv;
            Available = true;
        }
        else
        {
            try
            {
                _container = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .WithDatabase("sanguogame_test")
                    .WithUsername("sanguo")
                    .WithPassword("sanguo")
                    .Build();
                await _container.StartAsync();
                _connectionString = _container.GetConnectionString();
                Available = true;
            }
            catch (Exception ex)
            {
                Available = false;
                UnavailableReason = ex.Message;
                return;
            }
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connectionString);
        Environment.SetEnvironmentVariable("FreeSql__AutoSyncStructure", "true");
        Environment.SetEnvironmentVariable("Testing__DisableBackgroundJobs", "true");
        Environment.SetEnvironmentVariable("WorldMap__AiCityCount", "0");
        Environment.SetEnvironmentVariable("WorldMap__OutpostCount", "0");
        Environment.SetEnvironmentVariable("WorldMap__Width", "40");
        Environment.SetEnvironmentVariable("WorldMap__Height", "40");
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Testing:DisableBackgroundJobs", "true");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["FreeSql:AutoSyncStructure"] = "true",
                ["Testing:DisableBackgroundJobs"] = "true",
                ["Jwt:Issuer"] = "SanguoGame",
                ["Jwt:Audience"] = "SanguoGame.Web",
                ["Jwt:SigningKey"] = "dev-only-change-me-use-a-32-byte-secret-key!",
                ["Jwt:AccessTokenMinutes"] = "120",
                ["Jwt:RefreshTokenDays"] = "14",
                ["WorldMap:Width"] = "40",
                ["WorldMap:Height"] = "40",
                ["WorldMap:PlacementMaxAttempts"] = "64",
                ["WorldMap:OutpostCount"] = "0",
                ["WorldMap:AiCityCount"] = "0",
                ["WorldMap:SecondsPerTile"] = "1",
                ["WorldMap:MinMarchSeconds"] = "30",
                ["WorldMap:MaxMarchesPerCity"] = "3",
                ["WorldMap:ProtectionSeconds"] = "7200",
                ["Cors:Origins:0"] = "http://localhost:5173"
            });
        });
    }

    public HttpClient CreateJsonClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public async Task ForceCompleteBuildingsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        await orm.Update<BuildingEntity>()
            .Where(b => b.Status == BuildingStatus.Upgrading)
            .Set(b => b.FinishAt, DateTime.UtcNow.AddMinutes(-1))
            .ExecuteAffrowsAsync();
        await scope.ServiceProvider.GetRequiredService<BuildingService>()
            .RecoverDueAsync(CancellationToken.None);
    }

    public async Task ForceCompleteMarchesAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        await orm.Update<MarchEntity>()
            .Where(m => m.Status == MarchStatus.Marching)
            .Set(m => m.ArriveAt, DateTime.UtcNow.AddMinutes(-1))
            .ExecuteAffrowsAsync();
        await scope.ServiceProvider.GetRequiredService<MarchService>()
            .RecoverDueAsync(CancellationToken.None);
    }

    public async Task<long> InsertOutpostAsync(int x, int y)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        return await orm.Insert(new OutpostEntity
        {
            Type = "village",
            Name = $"测试村·{x},{y}",
            X = x,
            Y = y,
            Garrison = 1
        }).ExecuteIdentityAsync();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<GameApiFactory>;
