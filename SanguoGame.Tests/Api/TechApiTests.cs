using SanguoGame.Core;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class TechApiTests
{
    private readonly GameApiFactory _factory;

    public TechApiTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task TechHalls_RequireAcademy_AndExposeEffects()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var player = await api.RegisterPlayerAsync();

        var (_, blockedPalace) = await api.Post<BuildingsOverviewDto>(
            "/api/buildings/upgrade",
            new { buildingType = "drillHall" });
        Assert.Equal(ErrorCodes.BuildingPrerequisite, blockedPalace.Code);
        Assert.Contains("主殿", blockedPalace.Message);

        await _factory.SetBuildingLevelAsync(player.CityId, "palace", 3);
        var (_, blockedAcademy) = await api.Post<BuildingsOverviewDto>(
            "/api/buildings/upgrade",
            new { buildingType = "drillHall" });
        Assert.Equal(ErrorCodes.BuildingPrerequisite, blockedAcademy.Code);
        Assert.Contains("书院", blockedAcademy.Message);

        await _factory.SetBuildingLevelAsync(player.CityId, "academy", 1);
        var (_, upgrading) = await api.Post<BuildingsOverviewDto>(
            "/api/buildings/upgrade",
            new { buildingType = "drillHall" });
        Assert.Equal(0, upgrading.Code);
        await _factory.ForceCompleteBuildingsAsync();

        var (_, done) = await api.Get<BuildingsOverviewDto>("/api/buildings");
        var drill = done.Data!.Buildings.Single(b => b.Type == "drillHall");
        Assert.Equal(1, drill.Level);
        Assert.Equal(3, drill.Effects["troopPowerBonusPercent"]);
        Assert.Equal(2, drill.Effects["recruitDiscountPercent"]);
    }

    [SkippableFact]
    public async Task DrillHall_DiscountsRecruitTotal()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var player = await api.RegisterPlayerAsync();
        await _factory.SetBuildingLevelAsync(player.CityId, "barracks", 1);
        await _factory.SetBuildingLevelAsync(player.CityId, "drillHall", 1);

        var (_, army) = await api.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(2, army.Data?.RecruitDiscountPercent);
        Assert.Equal(3, army.Data?.TroopPowerBonusPercent);
        Assert.Equal(19, army.Data!.TroopTypes.Single(t => t.Type == "infantry").UnitCost.Grain);

        var (_, recruited) = await api.Post<ArmyOverviewDto>(
            "/api/army/recruit",
            new { troopType = "infantry", count = 5 });
        Assert.Equal(0, recruited.Code);
        Assert.Equal(5, recruited.Data?.Troops.Infantry);
        Assert.Equal(1902, recruited.Data?.Resources.Grain);
        Assert.Equal(1976, recruited.Data?.Resources.Wood);
        Assert.Equal(1951, recruited.Data?.Resources.Iron);
    }

    [SkippableFact]
    public async Task ResourceHall_BoostsFarmRateAndPending()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var player = await api.RegisterPlayerAsync();
        await _factory.SetBuildingLevelAsync(player.CityId, "farm", 1);
        await _factory.SetBuildingLevelAsync(player.CityId, "resourceHall", 1);

        var (_, fields) = await api.Get<FieldsOverviewDto>("/api/fields");
        var farm = fields.Data!.Fields.Single(f => f.Type == "farm");
        Assert.Equal(630, farm.RatePerHour);
        Assert.Equal(1575, farm.FieldCap);

        await _factory.BackdateFieldAsync(player.CityId, "farm", TimeSpan.FromHours(1));
        var (_, pending) = await api.Get<FieldsOverviewDto>("/api/fields");
        Assert.Equal(630, pending.Data!.Fields.Single(f => f.Type == "farm").Pending);

        var (_, collected) = await api.Post<FieldsCollectDto>("/api/fields/collect", new { fieldType = "farm" });
        Assert.Equal(0, collected.Code);
        Assert.Equal(630, collected.Data?.Collected.Grain);
    }

    [SkippableFact]
    public async Task DefenseHall_AddsFlatWallDefense()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var player = await api.RegisterPlayerAsync();
        await _factory.SetBuildingLevelAsync(player.CityId, "arrowTower", 1);
        await _factory.SetBuildingLevelAsync(player.CityId, "defenseHall", 1);

        var (_, walls) = await api.Get<WallsOverviewDto>("/api/walls");
        Assert.Equal(10, walls.Data?.WallDefense);
        Assert.Equal(8, walls.Data!.Walls.Single(w => w.Type == "arrowTower").Effects["wallDefense"]);

        var (_, army) = await api.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(10, army.Data?.WallDefense);
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
