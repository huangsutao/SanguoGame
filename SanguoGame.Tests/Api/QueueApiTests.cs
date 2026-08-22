using SanguoGame.Core;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Shop;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class QueueApiTests
{
    private readonly GameApiFactory _factory;

    public QueueApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task DifferentKinds_CanRunInParallel()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "palace", 2);

        var (_, palace) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "palace" });
        Assert.Equal(0, palace.Code);
        var (_, farm) = await api.Post<FieldsOverviewDto>("/api/fields/upgrade", new { fieldType = "farm" });
        Assert.Equal(0, farm.Code);
        var (_, academy) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "academy" });
        Assert.Equal(0, academy.Code);

        Assert.Equal(1, palace.Data?.BuildSlots?.Used);
        Assert.Equal(QueueRules.BaseSlots, palace.Data?.BuildSlots?.Limit);
        Assert.Contains(academy.Data!.Queues!, q => q.BuildingType == "palace");
        Assert.Contains(academy.Data.Queues!, q => q.BuildingType == "academy");
        Assert.Equal(1, academy.Data.TechSlots?.Used);
        Assert.Equal("farm", farm.Data?.Queue?.BuildingType);
        Assert.Equal(1, farm.Data?.FieldSlots?.Used);
    }

    [SkippableFact]
    public async Task RecruitSlots_FillThenShopExtraAddsOne()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "palace", 2);
        await _factory.SetBuildingLevelAsync(cityId, "barracks", 1);
        await _factory.SetCityYuanbaoAsync(cityId, 200);

        for (var i = 0; i < QueueRules.BaseSlots; i++)
        {
            var (_, recruited) = await api.Post<ArmyOverviewDto>(
                "/api/army/recruit",
                new { troopType = "infantry", count = 1 });
            Assert.Equal(0, recruited.Code);
            Assert.Equal(i + 1, recruited.Data?.RecruitQueues?.Count);
            Assert.Equal(i + 1, recruited.Data?.RecruitSlots?.Used);
        }

        var (_, busy) = await api.Post<ArmyOverviewDto>(
            "/api/army/recruit",
            new { troopType = "infantry", count = 1 });
        Assert.Equal(ErrorCodes.RecruitQueueBusy, busy.Code);

        var (_, bought) = await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = ItemCatalog.QueueRecruit, count = 1 });
        Assert.Equal(0, bought.Code);
        var (_, used) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = ItemCatalog.QueueRecruit, count = 1 });
        Assert.Equal(0, used.Code);
        Assert.Equal(1, used.Data?.Slots?.Recruit.Extra);
        Assert.Equal(6, used.Data?.Slots?.Recruit.Limit);

        var (_, extra) = await api.Post<ArmyOverviewDto>(
            "/api/army/recruit",
            new { troopType = "infantry", count = 1 });
        Assert.Equal(0, extra.Code);
        Assert.Equal(6, extra.Data?.RecruitSlots?.Used);
        Assert.Equal(6, extra.Data?.RecruitSlots?.Limit);

        var (_, again) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = ItemCatalog.QueueRecruit, count = 1 });
        Assert.Equal(ErrorCodes.QueueSlotMaxed, again.Code);

        await _factory.SetCityYuanbaoAsync(cityId, 200);
        await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = ItemCatalog.QueueRecruit, count = 1 });
        var (_, maxed) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = ItemCatalog.QueueRecruit, count = 1 });
        Assert.Equal(ErrorCodes.QueueSlotMaxed, maxed.Code);
    }

    [SkippableFact]
    public async Task BuildSlots_FillThenShopExtraAddsOne()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var (cityId, _, _) = await api.RegisterCityAsync();
        await _factory.SetBuildingLevelAsync(cityId, "palace", 3);
        await _factory.SetCityResourcesAsync(cityId, 20000, 20000, 20000, 20000);
        await _factory.SetCityYuanbaoAsync(cityId, 200);

        foreach (var type in new[] { "house", "warehouse", "barracks", "arrowTower", "gate" })
        {
            var path = type is "arrowTower" or "gate" ? "/api/walls/upgrade" : "/api/buildings/upgrade";
            var body = type is "arrowTower" or "gate" ? new { wallType = type } : (object)new { buildingType = type };
            var (_, started) = await api.Post<BuildingsOverviewDto>(path, body);
            Assert.Equal(0, started.Code);
        }

        var (_, busy) = await api.Post<WallsOverviewDto>("/api/walls/upgrade", new { wallType = "trap" });
        Assert.Equal(ErrorCodes.BuildingQueueBusy, busy.Code);

        await api.Post<ShopOverviewDto>("/api/shop/buy", new { itemType = ItemCatalog.QueueBuild, count = 1 });
        var (_, used) = await api.Post<ShopOverviewDto>("/api/shop/use", new { itemType = ItemCatalog.QueueBuild, count = 1 });
        Assert.Equal(0, used.Code);
        Assert.Equal(1, used.Data?.Slots?.Build.Extra);

        var (_, extra) = await api.Post<WallsOverviewDto>("/api/walls/upgrade", new { wallType = "trap" });
        Assert.Equal(0, extra.Code);
        Assert.Contains(extra.Data!.Queues!, q => q.BuildingType == "trap");
        Assert.Equal(6, extra.Data?.BuildSlots?.Used);
        Assert.Equal(6, extra.Data?.BuildSlots?.Limit);
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.Available ? "" : _factory.UnavailableReason ?? "PostgreSQL unavailable");
}
