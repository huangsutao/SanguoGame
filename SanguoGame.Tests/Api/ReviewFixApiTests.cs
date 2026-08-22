using SanguoGame.Core;
using SanguoGame.Core.Social;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Services;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class ReviewFixApiTests
{
    private readonly GameApiFactory _factory;

    public ReviewFixApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task MapCell_RejectsCrossTypeOverlap_AndBlocksRelocate()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var orm = scope.ServiceProvider.GetRequiredService<IFreeSql>();
        var cell = await orm.Select<MapCellEntity>().Where(c => c.X == x && c.Y == y).FirstAsync();
        Assert.NotNull(cell);
        Assert.Equal(MapCellKinds.City, cell.Kind);
        Assert.Equal(cityId, cell.OwnerId);
        Assert.False(await WorldOccupancy.TryClaimAsync(
            orm, x, y, MapCellKinds.Outpost, 99, CancellationToken.None));

        await _factory.SetCityYuanbaoAsync(cityId, 800);
        await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "relocateTarget", count = 1 });
        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        await _factory.InsertOutpostAsync(ox, oy);
        var (_, occupied) = await api.Post<ShopOverviewDto>(
            "/api/shop/use",
            new { itemType = "relocateTarget", count = 1, x = ox, y = oy });
        Assert.Equal(ErrorCodes.InvalidRelocateTarget, occupied.Code);
    }

    [SkippableFact]
    public async Task Alliance_ConcurrentJoin_DoesNotExceedCap()
    {
        SkipIfUnavailable();
        var leader = new ApiClient(_factory.CreateJsonClient());
        await leader.RegisterCityAsync("l");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (_, created) = await leader.Post<AllianceDetailDto>("/api/alliances", new { name = "满" + tag });
        Assert.Equal(0, created.Code);
        await _factory.FillAllianceMembersAsync(created.Data!.Id, AllianceRules.MaxMembers - 3);

        var first = new ApiClient(_factory.CreateJsonClient());
        await first.RegisterCityAsync("a");
        var second = new ApiClient(_factory.CreateJsonClient());
        await second.RegisterCityAsync("b");
        Assert.Equal(0, (await first.Post<object?>($"/api/alliances/{created.Data.Id}/apply")).Body.Code);
        Assert.Equal(0, (await second.Post<object?>($"/api/alliances/{created.Data.Id}/apply")).Body.Code);

        var (_, pending) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        Assert.Equal(2, pending.Data!.Applications.Count);
        var acceptA = leader.Post<object?>($"/api/alliances/applications/{pending.Data.Applications[0].Id}/accept");
        var acceptB = leader.Post<object?>($"/api/alliances/applications/{pending.Data.Applications[1].Id}/accept");
        await Task.WhenAll(acceptA, acceptB);

        var codes = new[] { acceptA.Result.Body.Code, acceptB.Result.Body.Code };
        Assert.Contains(0, codes);
        Assert.Contains(ErrorCodes.AllianceFull, codes);

        var (_, mine) = await leader.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(AllianceRules.MaxMembers, mine.Data?.MemberCount);
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
