using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.World;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class RoamingOutpostApiTests
{
    private readonly GameApiFactory _factory;

    public RoamingOutpostApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task ExpiredRoaming_IsHiddenAndPurged()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (_, x, y) = await api.RegisterCityAsync();
        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var id = await _factory.InsertRoamingOutpostAsync(ox, oy, DateTime.UtcNow.AddMinutes(-1));

        var (_, beforeTick) = await api.Get<WorldDto>("/api/world");
        Assert.Equal(0, beforeTick.Code);
        Assert.DoesNotContain(beforeTick.Data!.Outposts, o => o.Id == id);

        await _factory.TickRoamingOutpostsAsync();
        var (_, afterTick) = await api.Get<WorldDto>("/api/world");
        Assert.DoesNotContain(afterTick.Data!.Outposts, o => o.Id == id);

        var (_, march) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = id,
            infantry = 1,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(ErrorCodes.NotFound, march.Code);
    }

    [SkippableFact]
    public async Task LiveRoaming_ShowsKindAndExpires_ThenVanishesOnWin()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (_, x, y) = await api.RegisterCityAsync();
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "barracks");
        var (_, recruited) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 20 });
        Assert.Equal(0, recruited.Code);
        await _factory.ForceCompleteRecruitsAsync();

        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var expires = DateTime.UtcNow.AddHours(1);
        var id = await _factory.InsertRoamingOutpostAsync(ox, oy, expires, garrison: 1);

        var (_, world) = await api.Get<WorldDto>("/api/world");
        var shown = Assert.Single(world.Data!.Outposts, o => o.Id == id);
        Assert.Equal(OutpostKind.Roaming, shown.Kind);
        Assert.Equal("bandit", shown.Type);
        Assert.NotNull(shown.ExpiresAt);

        var (_, marched) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = id,
            infantry = 20,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        await _factory.ForceCompleteMarchesAsync();

        var (_, worldAfter) = await api.Get<WorldDto>("/api/world");
        Assert.DoesNotContain(worldAfter.Data!.Outposts, o => o.Id == id);

        var (_, reports) = await api.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=20");
        Assert.True(reports.Data?.Total >= 1);
        Assert.True(reports.Data!.Items[0].AttackerWon);
        Assert.True(reports.Data.Items[0].Loot.Grain > 0);
    }

    [SkippableFact]
    public async Task PermanentOutpost_StillStaysAfterWin()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (_, x, y) = await api.RegisterCityAsync();
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "barracks");
        var (_, recruited) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 20 });
        Assert.Equal(0, recruited.Code);
        await _factory.ForceCompleteRecruitsAsync();

        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var id = await _factory.InsertOutpostAsync(ox, oy, garrison: 1);

        var (_, world) = await api.Get<WorldDto>("/api/world");
        Assert.Equal(OutpostKind.Permanent, world.Data!.Outposts.Single(o => o.Id == id).Kind);

        var (_, marched) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = id,
            infantry = 20,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        await _factory.ForceCompleteMarchesAsync();

        var (_, worldAfter) = await api.Get<WorldDto>("/api/world");
        var left = Assert.Single(worldAfter.Data!.Outposts, o => o.Id == id);
        Assert.Equal(OutpostKind.Permanent, left.Kind);
        Assert.Equal(0, left.Garrison);
    }

    private async Task UpgradeAndFinish(ApiClient api, string buildingType)
    {
        var (_, body) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType });
        Assert.Equal(0, body.Code);
        await _factory.ForceCompleteBuildingsAsync();
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
