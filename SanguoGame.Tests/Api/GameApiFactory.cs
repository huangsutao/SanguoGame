using System.Text.Json;
using System.Text.Json.Serialization;
using FreeSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Market;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server;
using SanguoGame.Server.Services;
using Testcontainers.PostgreSql;
using Xunit;

namespace SanguoGame.Tests.Api;

public sealed class GameApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    static GameApiFactory()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

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
            const string local = "Host=127.0.0.1;Port=5432;Database=sanguogame_test;Username=sanguo;Password=sanguo";
            if (await CanConnectAsync(local))
            {
                _connectionString = local;
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
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connectionString);
        Environment.SetEnvironmentVariable("FreeSql__AutoSyncStructure", "true");
        Environment.SetEnvironmentVariable("Testing__DisableBackgroundJobs", "true");
        Environment.SetEnvironmentVariable("WorldMap__AiCityCount", "0");
        Environment.SetEnvironmentVariable("WorldMap__OutpostCount", "0");
        Environment.SetEnvironmentVariable("WorldMap__RoamingOutpostCount", "0");
        Environment.SetEnvironmentVariable("WorldMap__MarketCount", "0");
        Environment.SetEnvironmentVariable("WorldMap__Width", "40");
        Environment.SetEnvironmentVariable("WorldMap__Height", "40");
    }

    private static async Task<bool> CanConnectAsync(string connectionString)
    {
        try
        {
            var orm = new FreeSqlBuilder()
                .UseConnectionString(DataType.PostgreSQL, connectionString)
                .UseAutoSyncStructure(false)
                .Build();
            try
            {
                await orm.Ado.ExecuteNonQueryAsync("SELECT 1");
                return true;
            }
            finally
            {
                orm.Dispose();
            }
        }
        catch
        {
            return false;
        }
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
                ["WorldMap:RoamingOutpostCount"] = "0",
                ["WorldMap:RoamingOutpostLifetimeSeconds"] = "1800",
                ["WorldMap:MarketCount"] = "0",
                ["WorldMap:AiCityCount"] = "0",
                ["WorldMap:SecondsPerTile"] = "1",
                ["WorldMap:MinMarchSeconds"] = "30",
                ["WorldMap:MaxMarchesPerCity"] = "3",
                ["WorldMap:MaxTransportsPerCity"] = "3",
                ["WorldMap:ProtectionSeconds"] = "7200",
                ["Cors:Origins:0"] = "http://localhost:5173"
            });
        });
    }

    public HttpClient CreateJsonClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        ResetDatabaseOnce();
        return client;
    }

    private int _reset;

    private void ResetDatabaseOnce()
    {
        if (Interlocked.Exchange(ref _reset, 1) == 1)
        {
            return;
        }

        using var scope = Services.CreateScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        orm.Ado.ExecuteNonQuery("""
            TRUNCATE TABLE
                sg_alliance_application,
                sg_alliance_invite,
                sg_alliance_member,
                sg_alliance,
                sg_daily_quest,
                sg_item,
                sg_buff,
                sg_mail,
                sg_battle_report,
                sg_march,
                sg_transport,
                sg_building,
                sg_outpost,
                sg_market,
                sg_city,
                sg_refresh_token,
                sg_character,
                sg_account
            RESTART IDENTITY CASCADE
            """);
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

    public async Task ForceCompleteRecruitsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        await orm.Update<CityEntity>()
            .Where(c => c.RecruitFinishAt != null)
            .Set(c => c.RecruitFinishAt, DateTime.UtcNow.AddMinutes(-1))
            .ExecuteAffrowsAsync();
        await scope.ServiceProvider.GetRequiredService<ArmyService>()
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

    public async Task ForceCompleteTransportsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        await orm.Update<TransportEntity>()
            .Where(t => t.Status == TransportStatus.InTransit)
            .Set(t => t.ArriveAt, DateTime.UtcNow.AddMinutes(-1))
            .ExecuteAffrowsAsync();
        await scope.ServiceProvider.GetRequiredService<TransportService>()
            .RecoverDueAsync(CancellationToken.None);
    }

    public async Task<long> InsertMarketAsync(int x, int y)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        return await orm.Insert(new MarketEntity
        {
            Name = $"测试市集·{x},{y}",
            X = x,
            Y = y
        }).ExecuteIdentityAsync();
    }

    public async Task<long> InsertOutpostAsync(int x, int y, int garrison = 1)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        return await orm.Insert(new OutpostEntity
        {
            Type = "village",
            Name = $"测试村·{x},{y}",
            X = x,
            Y = y,
            Garrison = garrison,
            Kind = OutpostKind.Permanent
        }).ExecuteIdentityAsync();
    }

    public async Task<long> InsertRoamingOutpostAsync(int x, int y, DateTime expiresAt, int garrison = 25)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        return await orm.Insert(new OutpostEntity
        {
            Type = "bandit",
            Name = $"测试流寇·{x},{y}",
            X = x,
            Y = y,
            Garrison = garrison,
            Kind = OutpostKind.Roaming,
            ExpiresAt = expiresAt
        }).ExecuteIdentityAsync();
    }

    public async Task TickRoamingOutpostsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorldService>()
            .TickRoamingAsync(CancellationToken.None);
    }

    public async Task<(int X, int Y)> PickEmptyCellAsync(int nearX, int nearY)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var occupied = new HashSet<(int, int)>();
        foreach (var city in await orm.Select<CityEntity>().ToListAsync(CancellationToken.None))
        {
            occupied.Add((city.X, city.Y));
        }

        foreach (var outpost in await orm.Select<OutpostEntity>().ToListAsync(CancellationToken.None))
        {
            occupied.Add((outpost.X, outpost.Y));
        }

        foreach (var market in await orm.Select<MarketEntity>().ToListAsync(CancellationToken.None))
        {
            occupied.Add((market.X, market.Y));
        }

        for (var distance = 1; distance < 80; distance++)
        {
            for (var x = 0; x < 40; x++)
            {
                for (var y = 0; y < 40; y++)
                {
                    if (Math.Abs(x - nearX) + Math.Abs(y - nearY) != distance || occupied.Contains((x, y)))
                    {
                        continue;
                    }

                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("测试地图没有空地");
    }

    public async Task SetCityResourcesAsync(long cityId, int grain, int wood, int iron, int copper)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var updated = await orm.Update<CityEntity>()
            .Where(c => c.Id == cityId)
            .Set(c => c.Grain, grain)
            .Set(c => c.Wood, wood)
            .Set(c => c.Iron, iron)
            .Set(c => c.Copper, copper)
            .ExecuteAffrowsAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException($"未能写入城资源 cityId={cityId}");
        }
    }

    public async Task SetCityYuanbaoAsync(long cityId, int yuanbao)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var updated = await orm.Update<CityEntity>()
            .Where(c => c.Id == cityId)
            .Set(c => c.Yuanbao, yuanbao)
            .ExecuteAffrowsAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException($"未能写入元宝 cityId={cityId}");
        }
    }

    public async Task SetCityTroopsAsync(long cityId, int infantry, int archer = 0, int cavalry = 0)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var updated = await orm.Update<CityEntity>()
            .Where(c => c.Id == cityId)
            .Set(c => c.Infantry, infantry)
            .Set(c => c.Archer, archer)
            .Set(c => c.Cavalry, cavalry)
            .ExecuteAffrowsAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException($"未能写入城兵力 cityId={cityId}");
        }
    }

    public async Task BackdateFieldAsync(long cityId, string fieldType, TimeSpan age)
    {
        var at = DateTime.UtcNow.Add(-age);
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var updated = await orm.Update<BuildingEntity>()
            .Where(b => b.CityId == cityId && b.Type == fieldType)
            .Set(b => b.LastCollectedAt, at)
            .ExecuteAffrowsAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException($"未能回拨田收取时间 cityId={cityId} type={fieldType}");
        }
    }

    public async Task SetBuildingLevelAsync(long cityId, string type, int level)
    {
        await using var scope = Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var now = DateTime.UtcNow;
        var row = await orm.Select<BuildingEntity>()
            .Where(b => b.CityId == cityId && b.Type == type)
            .FirstAsync();
        if (row is null)
        {
            await orm.Insert(new BuildingEntity
            {
                CityId = cityId,
                Type = type,
                Level = level,
                Status = BuildingStatus.Idle,
                LastCollectedAt = OuterFieldCatalog.IsField(type) && level >= 1 ? now : null,
                UpdatedAt = now
            }).ExecuteAffrowsAsync();
            return;
        }

        row.Level = level;
        row.Status = BuildingStatus.Idle;
        row.TargetLevel = null;
        row.FinishAt = null;
        row.UpdatedAt = now;
        if (OuterFieldCatalog.IsField(type) && level >= 1 && row.LastCollectedAt is null)
        {
            row.LastCollectedAt = now;
        }

        var updated = await orm.Update<BuildingEntity>().SetSource(row).ExecuteAffrowsAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException($"未能写入建筑等级 cityId={cityId} type={type}");
        }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<GameApiFactory>;
