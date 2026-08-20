using System.Net;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Shop;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class ShopApiTests
{
    private readonly GameApiFactory _factory;

    public ShopApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Shop_RequiresLogin_AndStartsWithZeroYuanbao()
    {
        SkipIfUnavailable();
        var anon = new ApiClient(_factory.CreateJsonClient());
        var (status, denied) = await anon.Get<ShopOverviewDto>("/api/shop");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(ErrorCodes.Unauthorized, denied.Code);

        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterCityAsync();
        var (_, shop) = await api.Get<ShopOverviewDto>("/api/shop");
        Assert.Equal(0, shop.Code);
        Assert.Equal(0, shop.Data?.Yuanbao);
        Assert.Equal(ItemCatalog.All.Count, shop.Data?.Catalog.Count);
        Assert.Empty(shop.Data!.Buffs);
        Assert.All(shop.Data.Catalog, item => Assert.Equal(0, item.Owned));
    }

    [SkippableFact]
    public async Task Buy_RequiresYuanbao_ThenInventoryAndBalanceUpdate()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();

        var (_, poor) = await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "speedBuild", count = 1 });
        Assert.Equal(ErrorCodes.InsufficientYuanbao, poor.Code);

        var (_, unknown) = await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "noSuchItem", count = 1 });
        Assert.Equal(ErrorCodes.ValidationFailed, unknown.Code);

        await _factory.SetCityYuanbaoAsync(cityId, 200);
        var (_, bought) = await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "speedBuild", count = 2 });
        Assert.Equal(0, bought.Code);
        Assert.Equal(200 - ItemCatalog.Find("speedBuild")!.Price * 2, bought.Data?.Yuanbao);
        Assert.Equal(2, bought.Data!.Catalog.Single(i => i.Type == "speedBuild").Owned);

        var (_, useMissing) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "speedTech", count = 1 });
        Assert.Equal(ErrorCodes.ItemNotEnough, useMissing.Code);
    }

    [SkippableFact]
    public async Task SpeedBuild_ShortensQueue_AndStacksDuration()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        await _factory.SetCityYuanbaoAsync(cityId, 1000);
        var (_, bought) = await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "speedBuild", count = 2 });
        Assert.Equal(0, bought.Code);

        var (_, upgrading) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "palace" });
        Assert.Equal(0, upgrading.Code);
        Assert.NotNull(upgrading.Data?.Queue);
        var originalFinish = upgrading.Data!.Queue!.FinishAt;

        var (_, used) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "speedBuild", count = 1 });
        Assert.Equal(0, used.Code);
        var buff = Assert.Single(used.Data!.Buffs);
        Assert.Equal("speedBuild", buff.Type);
        Assert.True(buff.ExpireAt > DateTime.UtcNow.AddHours(4.5));

        var (_, after) = await api.Get<BuildingsOverviewDto>("/api/buildings");
        Assert.NotNull(after.Data?.Queue);
        Assert.True(after.Data!.Queue!.FinishAt < originalFinish);
        var expected = ItemCatalog.ApplySpeed(15, 50);
        Assert.Equal(expected, after.Data.Buildings.Single(b => b.Type == "palace").Next?.DurationSeconds);

        var firstExpire = buff.ExpireAt;
        var (_, stacked) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "speedBuild", count = 1 });
        Assert.Equal(0, stacked.Code);
        var stackedBuff = Assert.Single(stacked.Data!.Buffs);
        Assert.True(stackedBuff.ExpireAt >= firstExpire.AddHours(4.5));
        Assert.Equal(0, stacked.Data.Catalog.Single(i => i.Type == "speedBuild").Owned);
    }

    [SkippableFact]
    public async Task ResourceBoost_RaisesFarmPending()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "farm", 1);
        await _factory.SetCityYuanbaoAsync(cityId, 200);
        var (_, bought) = await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "resourceBoost", count = 1 });
        Assert.Equal(0, bought.Code);
        var (_, used) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "resourceBoost", count = 1 });
        Assert.Equal(0, used.Code);
        Assert.Contains(used.Data!.Buffs, b => b.Type == "resourceBoost");

        await _factory.BackdateFieldAsync(cityId, "farm", TimeSpan.FromHours(1));
        var (_, fields) = await api.Get<FieldsOverviewDto>("/api/fields");
        var farm = fields.Data!.Fields.Single(f => f.Type == "farm");
        Assert.Equal(900, farm.RatePerHour);
        Assert.Equal(900, farm.Pending);
    }

    [SkippableFact]
    public async Task RecruitQueue_BlocksUntilComplete_AndSpeedShortens()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "palace", 2);
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        await _factory.SetCityYuanbaoAsync(cityId, 200);
        await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "speedRecruit", count = 1 });

        var (_, first) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 5 });
        Assert.Equal(0, first.Code);
        Assert.NotNull(first.Data?.RecruitQueue);
        Assert.Equal(0, first.Data?.Troops.Infantry);
        var originalFinish = first.Data!.RecruitQueue!.FinishAt;

        var (_, busy) = await api.Post<ArmyOverviewDto>("/api/army/recruit", new { troopType = "infantry", count = 1 });
        Assert.Equal(ErrorCodes.RecruitQueueBusy, busy.Code);

        var (_, sped) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "speedRecruit", count = 1 });
        Assert.Equal(0, sped.Code);
        var (_, army) = await api.Get<ArmyOverviewDto>("/api/army");
        Assert.True(army.Data!.RecruitQueue!.FinishAt < originalFinish);

        await _factory.ForceCompleteRecruitsAsync();
        var (_, done) = await api.Get<ArmyOverviewDto>("/api/army");
        Assert.Null(done.Data?.RecruitQueue);
        Assert.Equal(5, done.Data?.Troops.Infantry);
    }

    [SkippableFact]
    public async Task Battle_GrantsYuanbaoMatchingFormula_WinAndLose()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "palace", 2);
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        await _factory.SetCityTroopsAsync(cityId, 70);

        var (ox, oy) = await _factory.PickEmptyCellAsync(x, y);
        var winId = await _factory.InsertOutpostAsync(ox, oy, garrison: 1);
        var (_, winMarch) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = winId,
            infantry = 60,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, winMarch.Code);
        await _factory.ForceCompleteMarchesAsync();
        var (_, reports) = await api.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=20");
        var win = reports.Data!.Items[0];
        Assert.True(win.AttackerWon);
        Assert.Equal(YuanbaoLoot.Roll(win.Seed, true), win.Yuanbao);
        var (_, shop) = await api.Get<ShopOverviewDto>("/api/shop");
        Assert.Equal(win.Yuanbao, shop.Data?.Yuanbao);

        await _factory.SetCityTroopsAsync(cityId, 1);
        var (lx, ly) = await _factory.PickEmptyCellAsync(x, y);
        var loseId = await _factory.InsertOutpostAsync(lx, ly, garrison: 800);
        var (_, loseMarch) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = loseId,
            infantry = 1,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, loseMarch.Code);
        await _factory.ForceCompleteMarchesAsync();
        var (_, reports2) = await api.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=20");
        var lose = reports2.Data!.Items[0];
        Assert.False(lose.AttackerWon);
        Assert.Equal(YuanbaoLoot.Roll(lose.Seed, false), lose.Yuanbao);
        var (_, shop2) = await api.Get<ShopOverviewDto>("/api/shop");
        Assert.Equal(win.Yuanbao + lose.Yuanbao, shop2.Data?.Yuanbao);
        if (lose.Yuanbao > 0 && win.Yuanbao > 0)
        {
            Assert.True(win.Yuanbao >= lose.Yuanbao);
        }
    }

    [SkippableFact]
    public async Task Relocate_RandomAndTarget_AndRejectsOccupiedOrBusy()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, x, y) = await api.RegisterCityAsync();
        await _factory.SetCityYuanbaoAsync(cityId, 2000);
        await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "relocateRandom", count = 1 });
        await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = "relocateTarget", count = 2 });

        var (_, missing) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "relocateTarget", count = 1 });
        Assert.Equal(ErrorCodes.ValidationFailed, missing.Code);

        var (_, same) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "relocateTarget", count = 1, x, y });
        Assert.Equal(ErrorCodes.InvalidRelocateTarget, same.Code);

        var other = new ApiClient(_factory.CreateJsonClient());
        var (_, ox, oy) = await other.RegisterCityAsync("o");
        var (_, occupied) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "relocateTarget", count = 1, x = ox, y = oy });
        Assert.Equal(ErrorCodes.InvalidRelocateTarget, occupied.Code);

        var (tx, ty) = await _factory.PickEmptyCellAsync(x, y);
        var (_, moved) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "relocateTarget", count = 1, x = tx, y = ty });
        Assert.Equal(0, moved.Code);
        Assert.Equal(tx, moved.Data?.X);
        Assert.Equal(ty, moved.Data?.Y);
        Assert.NotNull(moved.Data?.ProtectionUntil);
        Assert.Equal(1, moved.Data!.Catalog.Single(i => i.Type == "relocateTarget").Owned);

        var (_, random) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "relocateRandom", count = 1 });
        Assert.Equal(0, random.Code);
        Assert.False(random.Data?.X == tx && random.Data?.Y == ty);

        await _factory.SetBuildingLevelAsync(cityId, "palace", 2);
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        await _factory.SetCityTroopsAsync(cityId, 20);
        var (px, py) = await _factory.PickEmptyCellAsync(random.Data!.X, random.Data.Y);
        var outpostId = await _factory.InsertOutpostAsync(px, py);
        var (_, marched) = await api.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "outpost",
            targetId = outpostId,
            infantry = 5,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        var (nx, ny) = await _factory.PickEmptyCellAsync(random.Data.X, random.Data.Y);
        var (_, blocked) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = "relocateTarget", count = 1, x = nx, y = ny });
        Assert.Equal(ErrorCodes.RelocateBlocked, blocked.Code);
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "PostgreSQL unavailable");
}
